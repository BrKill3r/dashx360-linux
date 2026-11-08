using System.Collections.Generic;
using System.Linq;

namespace XboxMetroLauncher.ViewModels;

public sealed class MusicBrowserResultItemViewModel : ObservableObject
{
	private bool _isSelected;

	public string Title { get; }

	public string Subtitle { get; }

	public string Kind { get; }

	public string Path { get; }

	public IReadOnlyList<string> TrackPaths { get; }

	public int TrackCount => TrackPaths.Count;

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

	public MusicBrowserResultItemViewModel(string title, string subtitle, string kind, string path, IEnumerable<string> trackPaths)
	{
		Title = title;
		Subtitle = subtitle;
		Kind = kind;
		Path = path;
		TrackPaths = trackPaths.ToList();
	}
}
