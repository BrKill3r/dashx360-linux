using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace XboxMetroLauncher.ViewModels;

public sealed class GameDetailsTabViewModel : ObservableObject
{
	private bool _isSelected;

	public string Key { get; }

	public string Label { get; }

	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			if (SetProperty(ref _isSelected, value, "IsSelected"))
			{
				OnPropertyChanged("Foreground");
				OnPropertyChanged("FontWeight");
			}
		}
	}

	public IBrush Foreground
	{
		get
		{
			if (!IsSelected)
			{
				return new SolidColorBrush(Color.FromArgb(136, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			}
			return Brushes.White;
		}
	}

	public FontWeight FontWeight
	{
		get
		{
			if (!IsSelected)
			{
				return FontWeights.Light;
			}
			return FontWeights.SemiBold;
		}
	}

	public GameDetailsTabViewModel(string key, string label)
	{
		Key = key;
		Label = label;
	}
}
