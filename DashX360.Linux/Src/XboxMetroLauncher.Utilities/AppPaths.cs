using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace XboxMetroLauncher.Utilities;

internal static class AppPaths
{
	private static readonly string[] LegacyUserDataFolders = new string[2]
	{
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DashX360"),
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DashX360Data")
	};

	/// <summary>$XDG_DATA_HOME/DashX360 (default ~/.local/share/DashX360).</summary>
	public static string XdgDataFolder
	{
		get
		{
			string? data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
			if (string.IsNullOrWhiteSpace(data))
			{
				data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
			}
			return Path.Combine(data, "DashX360");
		}
	}

	/// <summary>Portable mode keeps data next to the binary (UserData folder or a "portable" marker file present and writable).</summary>
	private static bool IsPortable
	{
		get
		{
			string folder = AppFolder;
			return (Directory.Exists(Path.Combine(folder, "UserData")) || File.Exists(Path.Combine(folder, "portable"))) && IsWritable(folder);
		}
	}

	private static bool IsWritable(string folder)
	{
		try
		{
			string probe = Path.Combine(folder, ".write-test-" + Environment.ProcessId);
			File.WriteAllText(probe, string.Empty);
			File.Delete(probe);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Folder containing DashX360.dll and its Assets (works for the apphost and for "dotnet DashX360.dll").</summary>
	public static string AppFolder => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

	/// <summary>Writable Assets root for generated files (custom art, Steam art cache): next to the app when writable, else XDG data.</summary>
	public static string WritableAssetsFolder => IsWritable(AppFolder) ? Path.Combine(AppFolder, "Assets") : EnsureFolder(Path.Combine(XdgDataFolder, "Assets"));

	public static string UserDataFolder => EnsureFolder(ResolveUserDataFolder());

	public static string LogsFolder => EnsureFolder(IsPortable ? Path.Combine(AppFolder, "Logs") : Path.Combine(XdgDataFolder, "Logs"));

	public static bool IsPartyLinkTestInstance => HasCommandLineSwitch("--party-link-test") || string.Equals(Environment.GetEnvironmentVariable("DASHX360_PARTY_LINK_TEST"), "1", StringComparison.OrdinalIgnoreCase);

	public static IReadOnlyList<string> LegacyDataRoots()
	{
		return LegacyUserDataFolders.Where((string path) => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct<string>(StringComparer.Ordinal)
			.ToList();
	}

	public static IReadOnlyList<string> CandidateRoots()
	{
		List<string> list = new List<string>();
		AddRoot(list, AppFolder);
		AddRoot(list, Environment.CurrentDirectory);
		AddRoot(list, AppContext.BaseDirectory);
		return list;
	}

	public static string FindFolder(string relativePath, Func<string, bool>? predicate = null)
	{
		foreach (string item in CandidateRoots())
		{
			string text = Path.Combine(item, relativePath);
			if (Directory.Exists(text) && (predicate == null || predicate(text)))
			{
				return text;
			}
		}
		return Path.Combine(AppFolder, relativePath);
	}

	public static string FindFile(string relativePath)
	{
		foreach (string item in CandidateRoots())
		{
			string text = Path.Combine(item, relativePath);
			if (File.Exists(text))
			{
				return text;
			}
		}
		return Path.Combine(AppFolder, relativePath);
	}

	public static string ResolvePath(string path)
	{
		if (!Path.IsPathRooted(path))
		{
			return Path.Combine(AppFolder, path);
		}
		return path;
	}

	private static void AddRoot(List<string> roots, string? path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
			if (!roots.Any((string root) => string.Equals(root, fullPath, StringComparison.Ordinal)))
			{
				roots.Add(fullPath);
			}
		}
	}

	private static string ResolveUserDataFolder()
	{
		string text = Environment.GetEnvironmentVariable("DASHX360_USER_DATA_FOLDER");
		if (!string.IsNullOrWhiteSpace(text))
		{
			return Path.GetFullPath(text);
		}
		string text2 = GetCommandLineOption("--userdata") ?? GetCommandLineOption("--user-data");
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return Path.GetFullPath(text2);
		}
		return IsPortable ? Path.Combine(AppFolder, "UserData") : Path.Combine(XdgDataFolder, "UserData");
	}

	private static string? GetCommandLineOption(string optionName)
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		for (int i = 0; i < commandLineArgs.Length - 1; i++)
		{
			if (string.Equals(commandLineArgs[i], optionName, StringComparison.OrdinalIgnoreCase))
			{
				return commandLineArgs[i + 1];
			}
		}
		return null;
	}

	private static bool HasCommandLineSwitch(string switchName)
	{
		return Environment.GetCommandLineArgs().Any((string argument) => string.Equals(argument, switchName, StringComparison.OrdinalIgnoreCase));
	}

	private static string EnsureFolder(string path)
	{
		Directory.CreateDirectory(path);
		return path;
	}
}
