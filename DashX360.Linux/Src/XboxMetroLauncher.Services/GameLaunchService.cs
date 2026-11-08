using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class GameLaunchService : IGameLaunchService
{
	private static readonly string DebugLogPath = Path.Combine(AppPaths.LogsFolder, "running-game-debug.log");

	private static readonly HashSet<string> IgnoredProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"steam", "gameoverlayui", "steamwebhelper", "explorer", "xboxmetrolauncher", "steamservice", "steamerrorreporter", "crashhandler", "crashpad_handler", "conhost",
		"cmd", "rundll32", "dashx360", "reaper", "pressure-vessel-wrap", "pv-bwrap", "srt-bwrap", "wineserver", "explorer.exe", "services.exe",
		"winedevice.exe", "plugplay.exe", "rpcss.exe", "svchost.exe", "tabtip.exe", "steam.exe", "bash", "sh", "python3", "proton", "steam-runtime-launcher-service",
		"steam-runtime-supervisor", "wine64-preloader", "wine-preloader", "gamescope", "gamemoded", "xdg-open", "gio", "kioclient5", "gvfsd"
	};

	public async Task<GameLaunchResult> LaunchAsync(GameMetadata game, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.Equals(game.LaunchType, "Url", StringComparison.OrdinalIgnoreCase))
		{
			string url = NormalizeUrl(game.LaunchCommand);
			if (string.IsNullOrWhiteSpace(url))
			{
				throw new InvalidOperationException("'" + game.Title + "' does not have a URL.");
			}
			Process process = XboxMetroLauncher.Platform.LinuxApps.OpenUri(url);
			Log($"url launch request | title={game.Title} | url={url}");
			return new GameLaunchResult
			{
				TrackedProcess = process
			};
		}
		if (string.Equals(game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(game.LaunchCommand))
			{
				throw new InvalidOperationException("'" + game.Title + "' does not have a Steam launch command.");
			}
			string? installPath = NormalizeFolderPath(game.InstallPath);
			HashSet<int> knownProcessIds = CaptureProcessIds();
			Log($"steam launch request | title={game.Title} | steamAppId={game.SteamAppId} | installPath={game.InstallPath} | exePath={game.ExecutablePath}");
			XboxMetroLauncher.Platform.LinuxApps.OpenUri(game.LaunchCommand);
			Process process = await TryFindSteamGameProcessAsync(installPath, knownProcessIds, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			Log((process == null) ? ("steam launch unresolved | title=" + game.Title) : $"steam launch matched | title={game.Title} | pid={process.Id} | process={process.ProcessName} | path={TryGetProcessPath(process)}");
			return new GameLaunchResult
			{
				TrackedProcess = process
			};
		}
		if (string.IsNullOrWhiteSpace(game.ExecutablePath))
		{
			throw new InvalidOperationException("'" + game.Title + "' does not have an executable path.");
		}
		string text = (string.IsNullOrWhiteSpace(game.WorkingDirectory) ? Path.GetDirectoryName(game.ExecutablePath) : game.WorkingDirectory);
		Process process2 = Process.Start(XboxMetroLauncher.Platform.LinuxApps.CreateLaunchInfo(game.ExecutablePath, game.Arguments, text));
		Log($"exe launch request | title={game.Title} | pid={process2?.Id} | exePath={game.ExecutablePath}");
		return new GameLaunchResult
		{
			TrackedProcess = process2
		};
	}

	private static HashSet<int> CaptureProcessIds()
	{
		HashSet<int> hashSet = new HashSet<int>();
		Process[] processes = Process.GetProcesses();
		foreach (Process process in processes)
		{
			try
			{
				hashSet.Add(process.Id);
			}
			catch
			{
			}
			finally
			{
				process.Dispose();
			}
		}
		return hashSet;
	}

	private static async Task<Process?> TryFindSteamGameProcessAsync(string? installPath, HashSet<int> knownProcessIds, CancellationToken cancellationToken)
	{
		Process bestCandidate = null;
		int bestScore = int.MinValue;
		int stableBestProcessId = -1;
		int stableBestCount = 0;
		for (int attempt = 0; attempt < 24; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int foregroundProcessId = TryGetForegroundProcessId();
			List<(Process, int)> list = new List<(Process, int)>();
			Process[] processes = Process.GetProcesses();
			foreach (Process process in processes)
			{
				try
				{
					if (process.HasExited || knownProcessIds.Contains(process.Id) || IsIgnoredProcess(process))
					{
						process.Dispose();
						continue;
					}
					string text = TryGetProcessPath(process);
					int num = ScoreCandidate(process, text, installPath, foregroundProcessId);
					if (num <= 0)
					{
						process.Dispose();
						continue;
					}
					Log($"steam match attempt | candidate={process.ProcessName} | pid={process.Id} | score={num} | path={text}");
					list.Add((process, num));
				}
				catch
				{
					process.Dispose();
				}
			}
			(Process, int) tuple = (from candidate in list
				orderby candidate.Item2 descending, GetProcessStartTicks(candidate.Item1) descending
				select candidate).FirstOrDefault();
			foreach (var item in list)
			{
				if (item.Item1 != tuple.Item1)
				{
					item.Item1.Dispose();
				}
			}
			if (tuple.Item1 != null)
			{
				if (tuple.Item1.Id == stableBestProcessId)
				{
					stableBestCount++;
				}
				else
				{
					stableBestProcessId = tuple.Item1.Id;
					stableBestCount = 1;
				}
				if (tuple.Item2 > bestScore)
				{
					bestCandidate?.Dispose();
					(bestCandidate, bestScore) = tuple;
				}
				else if (bestCandidate != tuple.Item1)
				{
					tuple.Item1.Dispose();
				}
				if (tuple.Item2 >= 120 || (stableBestCount >= 2 && tuple.Item2 >= 80))
				{
					return bestCandidate;
				}
			}
			await Task.Delay(TimeSpan.FromMilliseconds(450.0), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		return bestCandidate;
	}

	private static string? TryGetProcessPath(Process process)
	{
		try
		{
			return XboxMetroLauncher.Platform.LinuxWindows.GetGamePath(process);
		}
		catch
		{
			return null;
		}
	}

	private static int ScoreCandidate(Process process, string? processPath, string? installPath, int foregroundProcessId)
	{
		int num = 0;
		if (!string.IsNullOrWhiteSpace(processPath))
		{
			num += 10;
		}
		if (!string.IsNullOrWhiteSpace(installPath) && !string.IsNullOrWhiteSpace(processPath) && processPath.StartsWith(installPath, StringComparison.OrdinalIgnoreCase))
		{
			num += 80;
		}
		if (process.Id == foregroundProcessId)
		{
			num += 120;
		}
		try
		{
			if (XboxMetroLauncher.Platform.LinuxWindows.HasWindow(process.Id))
			{
				num += 70;
			}
		}
		catch
		{
		}
		try
		{
			if (process.Responding)
			{
				num += 15;
			}
		}
		catch
		{
		}
		try
		{
			if (DateTime.Now - process.StartTime.ToLocalTime() < TimeSpan.FromSeconds(15.0))
			{
				num += 15;
			}
		}
		catch
		{
		}
		return num;
	}

	private static int TryGetForegroundProcessId()
	{
		return XboxMetroLauncher.Platform.LinuxWindows.GetForegroundProcessId();
	}

	private static long GetProcessStartTicks(Process process)
	{
		try
		{
			return process.StartTime.ToUniversalTime().Ticks;
		}
		catch
		{
			return 0L;
		}
	}

	private static bool IsIgnoredProcess(Process process)
	{
		try
		{
			return IgnoredProcessNames.Contains(process.ProcessName);
		}
		catch
		{
			return true;
		}
	}

	private static string? NormalizeFolderPath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		try
		{
			return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		}
		catch
		{
			return null;
		}
	}

	private static string NormalizeUrl(string? url)
	{
		string text = url?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
		{
			return text;
		}
		return "https://" + text;
	}

	private static void Log(string message)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(DebugLogPath));
			File.AppendAllText(DebugLogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}", Encoding.UTF8);
		}
		catch
		{
		}
	}
}
