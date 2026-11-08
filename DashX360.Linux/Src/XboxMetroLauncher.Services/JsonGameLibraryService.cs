using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class JsonGameLibraryService : IGameLibraryService
{
	private const string LibraryFileName = "library.json";

	private readonly IJsonStore _store;

	private readonly string _libraryPath;

	public JsonGameLibraryService(IJsonStore store)
	{
		_store = store;
		_libraryPath = Path.Combine(AppPaths.UserDataFolder, "library.json");
	}

	public async Task<GameLibrary> LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		GameLibrary gameLibrary = await ReadLibraryFileAsync(_libraryPath, cancellationToken);
		if (gameLibrary != null)
		{
			await SaveAsync(gameLibrary, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return gameLibrary;
		}
		string path = AppPaths.FindFile(Path.Combine("Data", "library.seed.json"));
		if (File.Exists(path))
		{
			GameLibrary seeded = await ReadLibraryFileAsync(path, cancellationToken);
			if (seeded != null)
			{
				await SaveAsync(seeded, cancellationToken);
				return seeded;
			}
		}
		return new GameLibrary();
	}

	public Task SaveAsync(GameLibrary library, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _store.WriteAsync("library.json", library, cancellationToken);
	}

	private static async Task<GameLibrary?> ReadLibraryFileAsync(string path, CancellationToken cancellationToken)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		await using FileStream stream = File.OpenRead(path);
		using JsonDocument document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip
		}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		JsonElement root = document.RootElement;
		if (root.ValueKind == JsonValueKind.Array)
		{
			return new GameLibrary
			{
				Games = root.Deserialize<List<GameMetadata>>() ?? new List<GameMetadata>()
			};
		}
		if (root.ValueKind == JsonValueKind.Object)
		{
			return root.Deserialize<GameLibrary>();
		}
		return null;
	}

	public Task<IReadOnlyList<GameMetadata>> ScanFolderAsync(string folderPath, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!Directory.Exists(folderPath))
		{
			return Task.FromResult((IReadOnlyList<GameMetadata>)Array.Empty<GameMetadata>());
		}
		HashSet<string> ignoredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UnityCrashHandler64", "UnityCrashHandler32", "CrashReportClient", "unins000", "uninstall", "crashpad_handler", "chrome-sandbox", "UnrealCEFSubProcess", "steam_appid" };
		// Linux: Windows .exe (run through Wine/Proton), AppImages, shell launchers, .desktop entries and native ELF executables.
		List<GameMetadata> games = new List<GameMetadata>();
		foreach (string path in SafeEnumerateFiles(folderPath))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string name = Path.GetFileNameWithoutExtension(path);
			string extension = Path.GetExtension(path).ToLowerInvariant();
			if (ignoredNames.Contains(name) || name.StartsWith("lib", StringComparison.Ordinal))
			{
				continue;
			}
			bool candidate = extension switch
			{
				".exe" or ".appimage" or ".desktop" or ".sh" or ".x86_64" or ".x86" => true,
				"" => XboxMetroLauncher.Platform.LinuxApps.IsExecutable(path) && IsElfExecutable(path),
				_ => false
			};
			if (!candidate)
			{
				continue;
			}
			string title = extension == ".desktop" ? (XboxMetroLauncher.Platform.LinuxApps.ReadDesktopValue(path, "Name") ?? CleanTitle(name)) : CleanTitle(name);
			games.Add(new GameMetadata
			{
				Title = title,
				LaunchType = "Exe",
				ExecutablePath = path,
				WorkingDirectory = (Path.GetDirectoryName(path) ?? folderPath),
				Platform = extension == ".exe" ? "PC (Wine)" : "Linux",
				Genre = "Imported"
			});
		}
		return Task.FromResult((IReadOnlyList<GameMetadata>)games.OrderBy(game => game.Title).ToList());
	}

	private static IEnumerable<string> SafeEnumerateFiles(string folderPath)
	{
		EnumerationOptions options = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			IgnoreInaccessible = true,
			MaxRecursionDepth = 6,
			AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
		};
		return Directory.EnumerateFiles(folderPath, "*", options);
	}

	private static bool IsElfExecutable(string path)
	{
		try
		{
			using FileStream stream = File.OpenRead(path);
			Span<byte> header = stackalloc byte[18];
			if (stream.Read(header) < 18)
			{
				return false;
			}
			// ELF magic + e_type == ET_EXEC (2) or ET_DYN (3, PIE executables).
			return header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F' && (header[16] == 2 || header[16] == 3);
		}
		catch
		{
			return false;
		}
	}

	private static string CleanTitle(string value)
	{
		return value.Replace("_", " ").Replace("-", " ").Trim();
	}
}
