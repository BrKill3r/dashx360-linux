using System;
using System.IO;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Themes;

internal static class AppPathResolver
{
	/// <summary>
	/// Paths imported from the Windows build use backslashes and drive letters. Relative ones are converted to '/', and absolute
	/// Windows paths that point inside a DashX360 "Assets" folder are remapped to this installation.
	/// </summary>
	public static string NormalizeWindowsPath(string path)
	{
		if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
		{
			string unix = path.Replace('\\', '/');
			int assets = unix.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
			if (assets >= 0)
			{
				return unix[(assets + 1)..];
			}
			return unix;
		}
		if (path.Contains('\\') && !File.Exists(path))
		{
			return path.Replace('\\', '/');
		}
		return path;
	}

	private const string OldMetroRoot = "C:\\Metro";

	public static string Resolve(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return path;
		}
		path = NormalizeWindowsPath(path);
		if (!Path.IsPathRooted(path))
		{
			return AppPaths.ResolvePath(path);
		}
		string text = "/nonexistent-legacy-metro-root";
		string fullPath = Path.GetFullPath(path);
		if (fullPath.StartsWith(text + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			string relativePath = Path.GetRelativePath(text, fullPath);
			foreach (string item in AppPaths.CandidateRoots())
			{
				string text2 = Path.Combine(item, relativePath);
				if (File.Exists(text2))
				{
					return text2;
				}
			}
		}
		return fullPath;
	}
}
