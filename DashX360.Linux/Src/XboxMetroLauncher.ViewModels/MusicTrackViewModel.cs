using System.IO;

namespace XboxMetroLauncher.ViewModels;

public sealed class MusicTrackViewModel : ObservableObject
{
	private bool _isPlaying;

	private bool _isSelected;

	public string Path { get; }

	public string Title { get; }

	public bool IsPlaying
	{
		get
		{
			return _isPlaying;
		}
		set
		{
			SetProperty(ref _isPlaying, value, "IsPlaying");
		}
	}

	public MusicTrackViewModel(string path)
	{
		Path = path;
		Title = System.IO.Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
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

	public MusicTrackViewModel(string title, string path)
	{
		Title = title;
		Path = path;
	}
}
