// Linux application launching helpers (xdg-open / gio / steam / wine) replacing Windows shell execution.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace XboxMetroLauncher.Platform;

public static class LinuxApps
{
	public static string? FindExecutable(string name)
	{
		if (name.Contains('/'))
		{
			return File.Exists(name) ? name : null;
		}
		string path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
		foreach (string dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries).Concat(new[] { "/usr/games", "/snap/bin", "/var/lib/flatpak/exports/bin", Path.Combine(Home, ".local/share/flatpak/exports/bin") }))
		{
			string candidate = Path.Combine(dir, name);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
		return null;
	}

	public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

	private static readonly Dictionary<string, string[]> WindowsProgramEquivalents = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["notepad.exe"] = new[] { "gnome-text-editor", "gedit", "kate", "mousepad", "xed" },
		["wmplayer.exe"] = new[] { "totem", "rhythmbox", "vlc", "celluloid", "mpv" },
		["explorer.exe"] = new[] { "nautilus", "dolphin", "thunar", "nemo" },
		["calc.exe"] = new[] { "gnome-calculator", "kcalc", "galculator" },
		["mspaint.exe"] = new[] { "pinta", "kolourpaint", "gimp" },
		["msedge.exe"] = new[] { "firefox", "google-chrome", "chromium", "chromium-browser" },
		["iexplore.exe"] = new[] { "firefox", "google-chrome", "chromium", "chromium-browser" },
		["steam.exe"] = new[] { "steam" },
	};

	/// <summary>Linux equivalent of Process.Start(new ProcessStartInfo { FileName, Arguments, UseShellExecute = true }).</summary>
	public static Process? ShellExecute(string fileName, string? arguments = null, string? workingDirectory = null)
	{
		string file = fileName.Trim();
		string args = (arguments ?? string.Empty).Trim();
		if (Path.GetFileName(file).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
		{
			return OpenUri(args.Length > 0 ? args.Trim('"') : Home);
		}
		if (file.Contains("://", StringComparison.Ordinal) || file.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase) || file.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || Directory.Exists(file))
		{
			return OpenUri(file);
		}
		if (File.Exists(file) && !IsExecutable(file) && !IsLaunchableExtension(Path.GetExtension(file)))
		{
			return OpenUri(file);
		}
		return Process.Start(CreateLaunchInfo(file, args, workingDirectory));
	}

	private static bool IsLaunchableExtension(string extension) => extension.ToLowerInvariant() is ".exe" or ".bat" or ".lnk" or ".url" or ".desktop" or ".appimage" or ".sh";

	public static string ResolveMediaPlayer()
	{
		return ResolveFirst(WindowsProgramEquivalents["wmplayer.exe"]) ?? "xdg-open";
	}

	private static string? ResolveFirst(IEnumerable<string> names)
	{
		foreach (string name in names)
		{
			string? found = FindExecutable(name);
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	/// <summary>Opens a URI or file with the desktop's default handler (steam:// goes to the Steam client).</summary>
	public static Process? OpenUri(string uri)
	{
		if (uri.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
		{
			string? steam = FindExecutable("steam");
			if (steam != null)
			{
				return Start(steam, new[] { uri }, null);
			}
		}
		if (uri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
		{
			string? spotify = FindExecutable("spotify");
			if (spotify != null)
			{
				return Start(spotify, new[] { "--uri=" + uri }, null);
			}
		}
		string? opener = FindExecutable("xdg-open") ?? FindExecutable("gio");
		if (opener == null)
		{
			return null;
		}
		return opener.EndsWith("gio", StringComparison.Ordinal) ? Start(opener, new[] { "open", uri }, null) : Start(opener, new[] { uri }, null);
	}

	/// <summary>Builds the start info for a library entry (native binary, script, AppImage, .desktop, Windows .exe via Wine).</summary>
	public static ProcessStartInfo CreateLaunchInfo(string fileName, string? arguments, string? workingDirectory)
	{
		string file = fileName.Trim().Trim('"');
		string args = arguments ?? string.Empty;
		string? cwd = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
		if (Uri.TryCreate(file, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
		{
			return ShellInfo(FindExecutable("xdg-open") ?? "xdg-open", Quote(file), cwd);
		}
		string extension = Path.GetExtension(file);
		if (!file.Contains('/') && WindowsProgramEquivalents.TryGetValue(Path.GetFileName(file), out string[]? equivalents))
		{
			string? native = ResolveFirst(equivalents);
			if (native != null)
			{
				return ShellInfo(native, args, cwd);
			}
		}
		if (extension.Equals(".desktop", StringComparison.OrdinalIgnoreCase))
		{
			string? gio = FindExecutable("gio");
			if (gio != null)
			{
				return ShellInfo(gio, "launch " + Quote(file) + (args.Length > 0 ? " " + args : string.Empty), cwd);
			}
			string? exec = ReadDesktopExec(file);
			if (exec != null)
			{
				return ShellInfo("/bin/sh", "-c " + Quote(exec + " " + args), cwd);
			}
		}
		if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
		{
			string? wine = FindExecutable("wine") ?? FindExecutable("wine64");
			if (wine != null)
			{
				return ShellInfo(wine, (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? "start /unix " : string.Empty) + Quote(file) + (args.Length > 0 ? " " + args : string.Empty), cwd ?? Path.GetDirectoryName(file));
			}
		}
		if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
		{
			string? url = File.Exists(file) ? File.ReadAllLines(file).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..] : null;
			if (url != null)
			{
				return CreateLaunchInfo(url, null, cwd);
			}
		}
		if (File.Exists(file) && (IsExecutable(file) || extension.Equals(".AppImage", StringComparison.OrdinalIgnoreCase)))
		{
			if (!IsExecutable(file))
			{
				TryMakeExecutable(file);
			}
			return ShellInfo(file, args, cwd ?? Path.GetDirectoryName(file));
		}
		if (extension.Equals(".sh", StringComparison.OrdinalIgnoreCase))
		{
			return ShellInfo("/bin/sh", Quote(file) + (args.Length > 0 ? " " + args : string.Empty), cwd ?? Path.GetDirectoryName(file));
		}
		string? onPath = FindExecutable(file);
		if (onPath != null)
		{
			return ShellInfo(onPath, args, cwd);
		}
		return ShellInfo(FindExecutable("xdg-open") ?? "xdg-open", Quote(file), cwd);
	}

	public static Process? Start(string file, IEnumerable<string> args, string? cwd)
	{
		ProcessStartInfo info = new ProcessStartInfo(file) { UseShellExecute = false };
		foreach (string arg in args)
		{
			info.ArgumentList.Add(arg);
		}
		if (!string.IsNullOrWhiteSpace(cwd))
		{
			info.WorkingDirectory = cwd;
		}
		return Process.Start(info);
	}

	private static ProcessStartInfo ShellInfo(string file, string arguments, string? cwd)
	{
		ProcessStartInfo info = new ProcessStartInfo(file, arguments) { UseShellExecute = false };
		if (!string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd))
		{
			info.WorkingDirectory = cwd;
		}
		return info;
	}

	public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

	public static bool IsExecutable(string file)
	{
		try
		{
			UnixFileMode mode = File.GetUnixFileMode(file);
			return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0 && !Directory.Exists(file);
		}
		catch
		{
			return false;
		}
	}

	private static void TryMakeExecutable(string file)
	{
		try
		{
			File.SetUnixFileMode(file, File.GetUnixFileMode(file) | UnixFileMode.UserExecute);
		}
		catch
		{
		}
	}

	public static string? ReadDesktopExec(string desktopFile)
	{
		try
		{
			foreach (string line in File.ReadLines(desktopFile))
			{
				if (line.StartsWith("Exec=", StringComparison.Ordinal))
				{
					string exec = line[5..];
					foreach (string code in new[] { "%f", "%F", "%u", "%U", "%i", "%c", "%k" })
					{
						exec = exec.Replace(code, string.Empty);
					}
					return exec.Trim();
				}
			}
		}
		catch
		{
		}
		return null;
	}

	public static string? ReadDesktopValue(string desktopFile, string key)
	{
		try
		{
			bool inEntry = false;
			foreach (string line in File.ReadLines(desktopFile))
			{
				if (line.StartsWith('['))
				{
					inEntry = line.Trim() == "[Desktop Entry]";
					continue;
				}
				if (inEntry && line.StartsWith(key + "=", StringComparison.Ordinal))
				{
					return line[(key.Length + 1)..].Trim();
				}
			}
		}
		catch
		{
		}
		return null;
	}
}
