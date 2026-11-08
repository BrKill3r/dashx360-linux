using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace XboxMetroLauncher.Services;

/// <summary>Native (portal / GTK) file dialogs through Avalonia's StorageProvider, exposed synchronously like the WPF dialogs.</summary>
public sealed class AvaloniaFilePickerService : IFilePickerService
{
	private static IStorageProvider? Provider => (SyncWait.ActiveWindow ?? SyncWait.MainWindow)?.StorageProvider;

	public string? PickExecutable()
	{
		return PickFile("Choose game executable, script or launcher", new[]
		{
			new FilePickerFileType("Games and launchers") { Patterns = new[] { "*.desktop", "*.sh", "*.AppImage", "*.appimage", "*.x86_64", "*.exe", "*.lnk", "*.url", "*" } },
			new FilePickerFileType("Windows executables (Wine)") { Patterns = new[] { "*.exe" } },
			FilePickerFileTypes.All
		}, null);
	}

	public string? PickFolder()
	{
		IStorageProvider? provider = Provider;
		if (provider == null)
		{
			return null;
		}
		IReadOnlyList<IStorageFolder> folders = SyncWait.Wait(provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a game library folder", AllowMultiple = false }));
		return folders.FirstOrDefault()?.TryGetLocalPath();
	}

	public string? PickImage(string? initialDirectory = null)
	{
		return PickFile("Choose artwork", new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" } }, FilePickerFileTypes.All }, initialDirectory);
	}

	public string? PickJsonFile(string title = "Choose backup file")
	{
		return PickFile(title, new[] { new FilePickerFileType("JSON files") { Patterns = new[] { "*.json" } }, FilePickerFileTypes.All }, null);
	}

	public string? PickSaveJsonFile(string suggestedFileName = "DashX360_Backup.json", string title = "Save dashboard backup")
	{
		IStorageProvider? provider = Provider;
		if (provider == null)
		{
			return null;
		}
		IStorageFile? file = SyncWait.Wait(provider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = title,
			SuggestedFileName = suggestedFileName,
			DefaultExtension = "json",
			ShowOverwritePrompt = true,
			FileTypeChoices = new[] { new FilePickerFileType("JSON files") { Patterns = new[] { "*.json" } } }
		}));
		return file?.TryGetLocalPath();
	}

	private static string? PickFile(string title, IReadOnlyList<FilePickerFileType> types, string? initialDirectory)
	{
		IStorageProvider? provider = Provider;
		if (provider == null)
		{
			return null;
		}
		IStorageFolder? start = null;
		if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
		{
			start = SyncWait.Wait(provider.TryGetFolderFromPathAsync(initialDirectory));
		}
		IReadOnlyList<IStorageFile> files = SyncWait.Wait(provider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = title,
			AllowMultiple = false,
			FileTypeFilter = types,
			SuggestedStartLocation = start
		}));
		return files.FirstOrDefault()?.TryGetLocalPath();
	}
}
