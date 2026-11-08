namespace XboxMetroLauncher.ViewModels;

public sealed class GuideAchievementGameItem : ObservableObject
{
	private bool _isSelected;

	private int _unlockedCount;

	private int _totalCount;

	private string _statusText = "Steam achievements";

	public required string Title { get; init; }

	public required string SteamAppId { get; init; }

	public string CoverArtPath { get; init; } = string.Empty;

	public int UnlockedCount
	{
		get
		{
			return _unlockedCount;
		}
		set
		{
			if (SetProperty(ref _unlockedCount, value, "UnlockedCount"))
			{
				OnPropertyChanged("CountText");
			}
		}
	}

	public int TotalCount
	{
		get
		{
			return _totalCount;
		}
		set
		{
			if (SetProperty(ref _totalCount, value, "TotalCount"))
			{
				OnPropertyChanged("CountText");
			}
		}
	}

	public string StatusText
	{
		get
		{
			return _statusText;
		}
		set
		{
			SetProperty(ref _statusText, value, "StatusText");
		}
	}

	public string CountText
	{
		get
		{
			if (TotalCount <= 0)
			{
				return StatusText;
			}
			return $"{UnlockedCount} of {TotalCount} Achievements";
		}
	}

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
}
