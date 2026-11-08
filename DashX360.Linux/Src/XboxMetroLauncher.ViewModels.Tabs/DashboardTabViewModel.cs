using Avalonia.Media;

namespace XboxMetroLauncher.ViewModels.Tabs;

public sealed class BladesMenuItemViewModel
{
	public string Title { get; }

	public string Key { get; }

	public string IconGlyph { get; }

	public string IconPath { get; }

	public bool HasIconPath => !string.IsNullOrWhiteSpace(IconPath);

	public string CountText { get; }

	public string DetailTitle { get; }

	public string DetailDescription { get; }

	public string DetailMetaText { get; }

	public bool IsEnabled { get; }

	public bool IsSelected { get; }

	public BladesMenuItemViewModel(string title, string iconGlyph, string detailTitle, string detailDescription, string countText = "", bool isEnabled = true, string? key = null, bool isSelected = false, string iconPath = "", string detailMetaText = "")
	{
		Title = title;
		Key = string.IsNullOrWhiteSpace(key) ? title : key;
		IconGlyph = iconGlyph;
		IconPath = iconPath;
		CountText = countText;
		DetailTitle = detailTitle;
		DetailDescription = detailDescription;
		DetailMetaText = detailMetaText;
		IsEnabled = isEnabled;
		IsSelected = isSelected;
	}

	public override string ToString()
	{
		return Title;
	}
}

public sealed class BladesBladeViewModel
{
	public string Key { get; }

	public string Title { get; }

	public Brush AccentBrush { get; }

	public IReadOnlyList<BladesMenuItemViewModel> MenuItems { get; }

	public bool IsSelected { get; }

	public BladesBladeViewModel(string key, string title, Brush accentBrush, IReadOnlyList<BladesMenuItemViewModel> menuItems, bool isSelected = false)
	{
		Key = key;
		Title = title;
		AccentBrush = accentBrush;
		MenuItems = menuItems;
		IsSelected = isSelected;
	}
}

public abstract class DashboardTabViewModel : ObservableObject
{
	private bool _isSelected;

	public DashboardViewModel Shell { get; }

	public string Name { get; }

	public string Key { get; }

	public string BladesTitle => Key switch
	{
		"bing" => "Xbox LIVE",
		"home" => "Xbox 360",
		"social" => "Social",
		"media" => "Media",
		"games" => "Games",
		"music" => "Media",
		"apps" => "System",
		"settings" => "System",
		_ => Name
	};

	public string BladesDescription => Key switch
	{
		"bing" => "Games. Tournaments. Entertainment. All the rewards. Endless possibilities.",
		"home" => "Play games, open the tray, manage pins, and jump back into recent activity.",
		"social" => "View your profile, friends, parties, messages, and Xbox LIVE activity.",
		"media" => "Watch videos and launch your media apps from one place.",
		"games" => "Browse your game library, launch games, and view achievements.",
		"music" => "Play dashboard music from the hard drive or Spotify sources.",
		"apps" => "Open dashboard apps and shortcuts.",
		"settings" => "Edit your console settings, including display, audio, dashboard, games, and data.",
		_ => "Select an option from this blade."
	};

	public IReadOnlyList<BladesMenuItemViewModel> BladesMenuItems => Key switch
	{
		"bing" => new[]
		{
			Item("Connect to Xbox LIVE", "\U0001F30D", "Xbox LIVE", "Games. Tournaments. Entertainment. All the rewards. Endless possibilities.", key: "Search Bing"),
			Item("Internet Explorer", "\U0001F30D", "Internet Explorer", "Open the computer's default browser.", key: "Internet Explorer"),
			Item("Recent Searches", "\U0001F552", "Recent Searches", "Return to recent search activity.", key: "Recent Searches")
		},
		"home" => new[]
		{
			Item(Shell.OpenTrayTitle, "\U0001F4BF", Shell.OpenTrayTitle, "Open or close the disc tray."),
			Item("My Pins", "\U0001F588", "My Pins", "Open your pinned dashboard shortcuts."),
			Item("Recent", "\U0001F552", "Recent", "Jump back into recent games and activity.")
		},
		"social" => new[]
		{
			Item("Profile", "\U0001F5E3", "Profile", "View and edit your player profile."),
			Item("Friends", "\U0001F5EA", "Friends", "See friends and their current status."),
			Item("Party", "\U0001F5E8", "Party", "Open the party menu."),
			Item("Inside Xbox", "\u2709", "Inside Xbox", "Open Xbox LIVE news and videos.")
		},
		"media" => new[]
		{
			Item("Music", "\u266A", "Music", "Choose a music source and play dashboard music.", key: "Select Music"),
			Item("Videos", "\U0001F39E", "Videos", "Open your video player.", key: "Video Player"),
			Item("Video Store", "\U0001F39E", "Video Store", "Browse video apps and marketplace content.", key: "Movies & TV"),
			Item("Media Center", "\U0001F4FA", "Media Center", "Open Windows Media Player Legacy.", key: "Windows Media Center")
		},
		"games" => new[]
		{
			Item("Games Library", "\U0001F3AE", "My Games", $"You have {Shell.Games.Count} games on your console. Select this option to play a game now.", Shell.Games.Count.ToString(), key: "My Games"),
			Item("Achievements", "\U0001F3C6", "Achievements", "View achievements and progress for your games.", key: "Achievements"),
			Item("Played Games", "\U0001F3AE", "Played Games", "View the current game's details.", key: "Game Details"),
			Item("Game Store", "\U0001F6CD", "Game Store", "Open game marketplace content.", key: "Game Marketplace")
		},
		"music" => new[]
		{
			Item("Music Player", "\u266A", "Music Player", "Open the Now Playing music screen."),
			Item("Select Music", "\u266A", "Music Sources", "Choose Hard Drive or Spotify as your music source."),
			Item("Hard Drive", "\U0001F4BF", "Hard Drive", "Listen to music stored in your dashboard music folder."),
			Item("Spotify", "\u266B", "Spotify", "Use Spotify as a dashboard music source.")
		},
		"apps" => new[]
		{
			Item("My Apps", "\u2630", "My Apps", $"You have {Shell.AppLibraryTiles.Count} apps on your console. Select this option to launch an app.", Shell.AppLibraryTiles.Count.ToString()),
			Item("Browse Apps", "\U0001F6CD", "Browse Apps", "Open dashboard apps and shortcuts."),
			Item("YouTube", "\U0001F39E", "YouTube", "Open the YouTube app.")
		},
		"settings" => new[]
		{
			Item("Console Settings", "\u2699", "Console Settings", "Edit display, controller, startup, and dashboard style options."),
			Item("Dashboard Customization", "\U0001F58C", "Dashboard Customization", "Change dashboard tiles, theme images, colors, and labels."),
			Item("Games Setup", "\U0001F3AE", "Games Setup", "Scan, add, edit, or remove games and apps."),
			Item("Audio", "\U0001F50A", "Audio", "Choose output devices, music folders, and dashboard volume."),
			Item("Data Control", "\u2630", "Data Control", "Import or export dashboard data.")
		},
		_ => new[] { Item(Name, "\U0001F3E0", Name, "Select an option from this blade.") }
	};

	public string BladesPrimaryAction => Key switch
	{
		"home" => Shell.OpenTrayTitle,
		"games" => "Play Game",
		"music" => "Play Music",
		"settings" => "Open Settings",
		_ => "Select"
	};

	public Brush BladesAccentBrush => Key switch
	{
		"bing" => WpfBrushes.LinearGradient(Color.FromRgb(244, 159, 57), Color.FromRgb(210, 104, 30), 90.0),
		"home" => WpfBrushes.LinearGradient(Color.FromRgb(88, 168, 75), Color.FromRgb(46, 126, 47), 90.0),
		"social" => WpfBrushes.LinearGradient(Color.FromRgb(88, 168, 75), Color.FromRgb(46, 126, 47), 90.0),
		"media" => WpfBrushes.LinearGradient(Color.FromRgb(77, 158, 216), Color.FromRgb(34, 114, 184), 90.0),
		"games" => WpfBrushes.LinearGradient(Color.FromRgb(47, 149, 76), Color.FromRgb(24, 103, 51), 90.0),
		"music" => WpfBrushes.LinearGradient(Color.FromRgb(77, 158, 216), Color.FromRgb(34, 114, 184), 90.0),
		"apps" => WpfBrushes.LinearGradient(Color.FromRgb(72, 134, 186), Color.FromRgb(33, 82, 143), 90.0),
		"settings" => WpfBrushes.LinearGradient(Color.FromRgb(169, 126, 222), Color.FromRgb(115, 80, 172), 90.0),
		_ => new SolidColorBrush(Color.FromRgb(231, 96, 34))
	};

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

	protected DashboardTabViewModel(DashboardViewModel shell, string name, string key)
	{
		Shell = shell;
		Name = name;
		Key = key;
	}

	private static BladesMenuItemViewModel Item(string title, string iconGlyph, string detailTitle, string detailDescription, string countText = "", bool isEnabled = true, string? key = null)
	{
		return new BladesMenuItemViewModel(title, iconGlyph, detailTitle, detailDescription, countText, isEnabled, key);
	}
}
