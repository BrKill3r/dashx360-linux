using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class RunningGameService : IRunningGameService
{
	private static class NativeMethods
	{

	}

	private static readonly string DebugLogPath = Path.Combine(AppPaths.LogsFolder, "running-game-debug.log");

	private static readonly HashSet<string> IgnoredProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"steam", "gameoverlayui", "steamwebhelper", "explorer", "xboxmetrolauncher", "steamservice", "steamerrorreporter", "crashhandler", "crashpad_handler", "conhost",
		"cmd", "rundll32", "dashx360", "steamwebhelper", "reaper", "pressure-vessel-wrap", "pv-bwrap", "srt-bwrap", "wineserver", "explorer.exe",
		"services.exe", "winedevice.exe", "plugplay.exe", "rpcss.exe", "svchost.exe", "tabtip.exe", "steam.exe", "bash", "sh", "python3", "proton", "steam-runtime-launcher-service",
		"steam-runtime-supervisor", "wine64-preloader", "wine-preloader", "gamescope", "gamemoded"
	};

	private readonly object _syncRoot = new object();

	private Process? _process;

	private GameMetadata? _game;

	private DateTimeOffset _launchedAt = DateTimeOffset.MinValue;

	private DateTimeOffset? _localPlaytimeStartedAt;

	private bool _hasPlaytimeUpdate;

	private RunningGameState _state;

	public bool HasRunningGame
	{
		get
		{
			lock (_syncRoot)
			{
				return _game != null;
			}
		}
	}

	public bool HasTrackedProcess => GetTrackedProcess() != null;

	public string RunningGameTitle
	{
		get
		{
			lock (_syncRoot)
			{
				return _game?.Title ?? string.Empty;
			}
		}
	}

	public RunningGameState State
	{
		get
		{
			lock (_syncRoot)
			{
				if (_state == RunningGameState.Tracked && GetTrackedProcess() == null)
				{
					_state = RunningGameState.ProcessNotDetected;
				}
				return _state;
			}
		}
	}

	public GameMetadata? CurrentGame
	{
		get
		{
			lock (_syncRoot)
			{
				return _game;
			}
		}
	}

	public event EventHandler? StateChanged;

	public bool ConsumePlaytimeUpdate()
	{
		lock (_syncRoot)
		{
			if (!_hasPlaytimeUpdate)
			{
				return false;
			}
			_hasPlaytimeUpdate = false;
			return true;
		}
	}

	public void BeginLaunch(GameMetadata game, DateTimeOffset launchedAt)
	{
		ClearInternal();
		lock (_syncRoot)
		{
			_game = game;
			_launchedAt = launchedAt;
			_state = RunningGameState.Launching;
		}
		Log($"launch started | title={game.Title} | launchType={game.LaunchType} | launchTime={launchedAt:O} | steamAppId={game.SteamAppId} | exePath={game.ExecutablePath} | installPath={game.InstallPath}");
		this.StateChanged?.Invoke(this, EventArgs.Empty);
	}

	public void Track(GameMetadata game, Process? process)
	{
		lock (_syncRoot)
		{
			_game = game;
		}
		if (process == null || IsIgnoredProcess(process))
		{
			lock (_syncRoot)
			{
				_state = ((_game != null) ? RunningGameState.ProcessNotDetected : RunningGameState.None);
			}
			Log("process track result | title=" + game.Title + " | matchedProcess=<none>");
			this.StateChanged?.Invoke(this, EventArgs.Empty);
			return;
		}
		AttachProcess(process, RunningGameState.Tracked);
		Log($"process track result | title={game.Title} | matchedProcess={process.ProcessName} | pid={process.Id} | path={TryGetProcessPath(process)}");
		this.StateChanged?.Invoke(this, EventArgs.Empty);
	}

	public void Clear()
	{
		ClearInternal();
		this.StateChanged?.Invoke(this, EventArgs.Empty);
	}

	public async Task<RunningGameCloseResult> CloseAsync(bool forceKill, CancellationToken cancellationToken = default(CancellationToken))
	{
		GameMetadata game;
		RunningGameState state;
		lock (_syncRoot)
		{
			game = _game;
			state = _state;
		}
		if (game == null)
		{
			return new RunningGameCloseResult
			{
				Success = false,
				RequiresForceConfirmation = false,
				Message = "No game running."
			};
		}
		Process process = GetTrackedProcess();
		if (process == null)
		{
			bool allowWait = state == RunningGameState.Launching || DateTimeOffset.UtcNow - _launchedAt < TimeSpan.FromSeconds(12.0);
			Process process2 = await TryResolveTrackedProcessAsync(game, allowWait, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (process2 == null)
			{
				string text = (allowWait ? "Finding game process..." : "Game running, but process not detected");
				Log("close failed | title=" + game.Title + " | reason=" + text);
				return new RunningGameCloseResult
				{
					Success = false,
					RequiresForceConfirmation = false,
					Message = text
				};
			}
			process = process2;
			AttachProcess(process, RunningGameState.Tracked);
			this.StateChanged?.Invoke(this, EventArgs.Empty);
		}
		if (IsIgnoredProcess(process))
		{
			Log("close blocked | title=" + game.Title + " | reason=ignored process " + process.ProcessName);
			ClearInternal();
			this.StateChanged?.Invoke(this, EventArgs.Empty);
			return new RunningGameCloseResult
			{
				Success = false,
				RequiresForceConfirmation = false,
				Message = "Game running, but process not detected"
			};
		}
		try
		{
			string title = game.Title;
			bool flag = XboxMetroLauncher.Platform.LinuxWindows.RequestClose(process.Id);
			Log($"close attempt | title={title} | pid={process.Id} | process={process.ProcessName} | closeMainWindow={flag}");
			bool flag2 = flag;
			if (flag2)
			{
				flag2 = await WaitForExitAsync(process, TimeSpan.FromSeconds(3.0), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (flag2)
			{
				Log($"close success | title={title} | pid={process.Id}");
				ClearInternal();
				this.StateChanged?.Invoke(this, EventArgs.Empty);
				return new RunningGameCloseResult
				{
					Success = true,
					RequiresForceConfirmation = false,
					Message = title + " closed."
				};
			}
			if (!forceKill)
			{
				Log($"close needs force | title={title} | pid={process.Id}");
				return new RunningGameCloseResult
				{
					Success = false,
					RequiresForceConfirmation = true,
					Message = title + " did not close. Press X again to force close."
				};
			}
			process.Kill(entireProcessTree: true);
			await WaitForExitAsync(process, TimeSpan.FromSeconds(3.0), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			Log($"force close success | title={title} | pid={process.Id}");
			ClearInternal();
			this.StateChanged?.Invoke(this, EventArgs.Empty);
			return new RunningGameCloseResult
			{
				Success = true,
				RequiresForceConfirmation = false,
				Message = title + " closed."
			};
		}
		catch (Exception ex)
		{
			if (process.HasExited)
			{
				Log("close success after exception | title=" + game.Title + " | reason=" + ex.Message);
				ClearInternal();
				this.StateChanged?.Invoke(this, EventArgs.Empty);
				return new RunningGameCloseResult
				{
					Success = true,
					RequiresForceConfirmation = false,
					Message = "Game closed."
				};
			}
			Log("close failed | title=" + game.Title + " | reason=" + ex.Message);
			return new RunningGameCloseResult
			{
				Success = false,
				RequiresForceConfirmation = false,
				Message = "Unable to close the running game."
			};
		}
	}

	private Process? GetTrackedProcess()
	{
		lock (_syncRoot)
		{
			if (_process == null)
			{
				return null;
			}
			try
			{
				if (_process.HasExited)
				{
					DetachTrackedProcess();
					_state = ((_game != null) ? RunningGameState.ProcessNotDetected : RunningGameState.None);
					return null;
				}
				return _process;
			}
			catch
			{
				DetachTrackedProcess();
				_state = ((_game != null) ? RunningGameState.ProcessNotDetected : RunningGameState.None);
				return null;
			}
		}
	}

	private void AttachProcess(Process process, RunningGameState state)
	{
		lock (_syncRoot)
		{
			DetachTrackedProcess();
			_process = process;
			_state = state;
			_localPlaytimeStartedAt = (ShouldTrackLocalPlaytime(_game) ? new DateTimeOffset?(_launchedAt) : ((DateTimeOffset?)null));
		}
		try
		{
			process.EnableRaisingEvents = true;
			process.Exited += Process_OnExited;
		}
		catch
		{
		}
	}

	private async Task<Process?> TryResolveTrackedProcessAsync(GameMetadata game, bool allowWait, CancellationToken cancellationToken)
	{
		int rounds = (allowWait ? 8 : 2);
		TimeSpan delay = TimeSpan.FromMilliseconds(500.0);
		Process bestCandidate = null;
		int bestScore = int.MinValue;
		for (int round = 0; round < rounds; round++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Process[] processes = Process.GetProcesses();
			try
			{
				int foregroundProcessId = TryGetForegroundProcessId();
				Process[] array = processes;
				foreach (Process process in array)
				{
					try
					{
						if (process.HasExited || IsIgnoredProcess(process))
						{
							continue;
						}
						string text = TryGetProcessPath(process);
						int num = ScoreCandidate(game, process, text, foregroundProcessId);
						if (num > 0)
						{
							Log($"process match attempt | title={game.Title} | candidate={process.ProcessName} | pid={process.Id} | score={num} | path={text}");
							if (num > bestScore)
							{
								bestCandidate?.Dispose();
								bestCandidate = Process.GetProcessById(process.Id);
								bestScore = num;
							}
						}
					}
					catch
					{
					}
				}
			}
			finally
			{
				Process[] array = processes;
				for (int i = 0; i < array.Length; i++)
				{
					array[i].Dispose();
				}
			}
			if (bestCandidate != null && bestScore >= 120)
			{
				return bestCandidate;
			}
			if (round < rounds - 1)
			{
				await Task.Delay(delay, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		return bestCandidate;
	}

	private int ScoreCandidate(GameMetadata game, Process process, string? processPath, int foregroundProcessId)
	{
		if (!StartedAfterLaunch(process))
		{
			return 0;
		}
		int num = 0;
		string value = NormalizeFolderPath(game.InstallPath);
		string text = NormalizeFilePath(game.ExecutablePath);
		if (!string.IsNullOrWhiteSpace(processPath))
		{
			num += 10;
		}
		if (!string.IsNullOrWhiteSpace(text) && string.Equals(NormalizeFilePath(processPath), text, StringComparison.OrdinalIgnoreCase))
		{
			num += 180;
		}
		if (!string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(processPath))
		{
			string? text2 = NormalizeFilePath(processPath);
			if (text2 != null && text2.StartsWith(value, StringComparison.OrdinalIgnoreCase))
			{
				num += 130;
			}
		}
		if (process.Id == foregroundProcessId)
		{
			num += 120;
		}
		try
		{
			if (XboxMetroLauncher.Platform.LinuxWindows.HasWindow(process.Id))
			{
				num += 60;
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
		return num;
	}

	private bool StartedAfterLaunch(Process process)
	{
		try
		{
			return process.StartTime.ToUniversalTime() >= _launchedAt.UtcDateTime.AddSeconds(-2.0);
		}
		catch
		{
			return false;
		}
	}

	private void Process_OnExited(object? sender, EventArgs e)
	{
		lock (_syncRoot)
		{
			AddLocalPlaytimeIfNeeded(DateTimeOffset.UtcNow);
			DetachTrackedProcess();
			_game = null;
			_state = RunningGameState.None;
			_launchedAt = DateTimeOffset.MinValue;
		}
		this.StateChanged?.Invoke(this, EventArgs.Empty);
	}

	private void ClearInternal()
	{
		lock (_syncRoot)
		{
			AddLocalPlaytimeIfNeeded(DateTimeOffset.UtcNow);
			DetachTrackedProcess();
			_game = null;
			_state = RunningGameState.None;
			_launchedAt = DateTimeOffset.MinValue;
		}
	}

	private void DetachTrackedProcess()
	{
		Process process = _process;
		_process = null;
		if (process != null)
		{
			try
			{
				process.Exited -= Process_OnExited;
			}
			catch
			{
			}
			process.Dispose();
		}
	}

	private void AddLocalPlaytimeIfNeeded(DateTimeOffset endedAt)
	{
		if (ShouldTrackLocalPlaytime(_game))
		{
			DateTimeOffset? localPlaytimeStartedAt = _localPlaytimeStartedAt;
			if (localPlaytimeStartedAt.HasValue)
			{
				DateTimeOffset valueOrDefault = localPlaytimeStartedAt.GetValueOrDefault();
				TimeSpan timeSpan = endedAt - valueOrDefault;
				_localPlaytimeStartedAt = null;
				if (!(timeSpan <= TimeSpan.FromSeconds(1.0)))
				{
					_game.Playtime += timeSpan;
					if (_game.Playtime < TimeSpan.Zero)
					{
						_game.Playtime = TimeSpan.Zero;
					}
					_hasPlaytimeUpdate = true;
					Log($"local playtime updated | title={_game.Title} | added={timeSpan} | total={_game.Playtime}");
				}
				return;
			}
		}
		_localPlaytimeStartedAt = null;
	}

	private static bool ShouldTrackLocalPlaytime(GameMetadata? game)
	{
		if (game != null)
		{
			return !string.Equals(game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase);
		}
		return false;
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

	private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
	{
		using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(timeout);
		try
		{
			await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(continueOnCapturedContext: false);
			return true;
		}
		catch (OperationCanceledException)
		{
			return process.HasExited;
		}
	}

	private static int TryGetForegroundProcessId()
	{
		return XboxMetroLauncher.Platform.LinuxWindows.GetForegroundProcessId();
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

	private static string? NormalizeFilePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		try
		{
			return Path.GetFullPath(path);
		}
		catch
		{
			return null;
		}
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
