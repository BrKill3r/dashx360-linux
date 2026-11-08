namespace XboxMetroLauncher.ViewModels;

public sealed class MusicBrowserMenuItemViewModel : ObservableObject
{
	private bool _isSelected;

	public string Key { get; }

	public string Title { get; }

	public string Icon { get; }

	public string IconPath { get; }

	public bool HasIconPath => !string.IsNullOrWhiteSpace(IconPath);

	public string Description { get; }

	public bool IsEnabled { get; }

	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			SetProperty(ref _isSelected, value, "IsSelected");
		}
	}

	public MusicBrowserMenuItemViewModel(string key, string title, string icon, string description, bool isEnabled = true, string iconPath = "")
	{
		Key = key;
		Title = title;
		Icon = icon;
		IconPath = iconPath;
		Description = description;
		IsEnabled = isEnabled;
	}
}
