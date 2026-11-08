using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace XboxMetroLauncher.ViewModels;

public sealed class DashboardTileCustomizationViewModel : ObservableObject
{
	private bool _isSelected;

	private string _imagePath = string.Empty;

	private string _titleOverride = string.Empty;

	private string _secondaryTitleOverride = string.Empty;

	private string _launchExecutablePath = string.Empty;

	private string _launchWebAddress = string.Empty;

	private double _zoom = 1.0;

	private double _offsetX;

	private double _offsetY;

	public string Key { get; }

	public string DefaultTitle { get; }

	public string Title
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(_titleOverride))
			{
				return _titleOverride;
			}
			return DefaultTitle;
		}
		set
		{
			string value2 = (string.Equals(value?.Trim(), DefaultTitle, StringComparison.Ordinal) ? string.Empty : (value?.Trim() ?? string.Empty));
			if (SetProperty(ref _titleOverride, value2, "Title"))
			{
				OnPropertyChanged("Title");
				OnPropertyChanged("HasCustomTitle");
				OnPropertyChanged("HasCustomization");
			}
		}
	}

	public string TitleOverride
	{
		get
		{
			return _titleOverride;
		}
		set
		{
			if (SetProperty(ref _titleOverride, value?.Trim() ?? string.Empty, "TitleOverride"))
			{
				OnPropertyChanged("Title");
				OnPropertyChanged("HasCustomTitle");
				OnPropertyChanged("HasCustomization");
			}
		}
	}

	public string DefaultSecondaryTitle { get; }

	public string SecondaryTitle
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(_secondaryTitleOverride))
			{
				return _secondaryTitleOverride;
			}
			return DefaultSecondaryTitle;
		}
		set
		{
			string value2 = (string.Equals(value?.Trim(), DefaultSecondaryTitle, StringComparison.Ordinal) ? string.Empty : (value?.Trim() ?? string.Empty));
			if (SetProperty(ref _secondaryTitleOverride, value2, "SecondaryTitle"))
			{
				OnPropertyChanged("SecondaryTitle");
				OnPropertyChanged("HasCustomSecondaryTitle");
				OnPropertyChanged("HasCustomization");
			}
		}
	}

	public string SecondaryTitleOverride
	{
		get
		{
			return _secondaryTitleOverride;
		}
		set
		{
			if (SetProperty(ref _secondaryTitleOverride, value?.Trim() ?? string.Empty, "SecondaryTitleOverride"))
			{
				OnPropertyChanged("SecondaryTitle");
				OnPropertyChanged("HasCustomSecondaryTitle");
				OnPropertyChanged("HasCustomization");
			}
		}
	}

	public bool AllowsSecondaryTitleCustomization => !string.IsNullOrWhiteSpace(DefaultSecondaryTitle);

	public string TabKey { get; }

	public double Left { get; }

	public double Top { get; }

	public double Width { get; }

	public double Height { get; }

	public bool UsesDashboardColor { get; }

	public bool AllowsImageCustomization { get; }

	public bool AllowsLaunchCustomization { get; }

	public string DefaultImagePath { get; }

	public string PlaceholderColor { get; }

	public string TitleColor { get; }

	public string CustomizationHint
	{
		get
		{
			if (!AllowsImageCustomization)
			{
				return "This tile uses the global tile color.";
			}
			return "Choose an image and adjust how it is cropped.";
		}
	}

	public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath);

	public bool HasCustomTitle => !string.IsNullOrWhiteSpace(TitleOverride);

	public bool HasCustomSecondaryTitle => !string.IsNullOrWhiteSpace(SecondaryTitleOverride);

	public bool HasCustomLaunchExecutable => !string.IsNullOrWhiteSpace(LaunchExecutablePath);

	public bool HasCustomLaunchWebAddress => !string.IsNullOrWhiteSpace(LaunchWebAddress);

	public bool HasCustomization
	{
		get
		{
			if (!HasImage && !HasCustomSecondaryTitle && !HasCustomLaunchExecutable && !HasCustomLaunchWebAddress)
			{
				return HasCustomTitle;
			}
			return true;
		}
	}

	public IBrush PlaceholderBrush
	{
		get
		{
			if (!UsesDashboardColor)
			{
				return CreateBrush(PlaceholderColor, Color.FromRgb(32, 38, 40));
			}
			return Brushes.Transparent;
		}
	}

	public IBrush TitleBrush => CreateBrush(TitleColor, Colors.White);

	public Thickness TitleMargin => new Thickness(8.0, 0.0, 8.0, (Height > 170.0) ? 12 : 7);

	public double TitleFontSize => (Height > 170.0) ? 15 : 13;

	public string ImagePath
	{
		get
		{
			return _imagePath;
		}
		set
		{
			if (SetProperty(ref _imagePath, value, "ImagePath"))
			{
				OnPropertyChanged("HasImage");
				OnPropertyChanged("HasCustomization");
			}
		}
	}

	public string LaunchExecutablePath
	{
		get
		{
			return _launchExecutablePath;
		}
		set
		{
			if (SetProperty(ref _launchExecutablePath, value?.Trim() ?? string.Empty, "LaunchExecutablePath"))
			{
				OnPropertyChanged("HasCustomLaunchExecutable");
				OnPropertyChanged("HasCustomization");
			}
		}
	}

	public string LaunchWebAddress
	{
		get
		{
			return _launchWebAddress;
		}
		set
		{
			if (SetProperty(ref _launchWebAddress, value?.Trim() ?? string.Empty, "LaunchWebAddress"))
			{
				OnPropertyChanged("HasCustomLaunchWebAddress");
				OnPropertyChanged("HasCustomization");
			}
		}
	}

	public double Zoom
	{
		get
		{
			return _zoom;
		}
		set
		{
			SetProperty(ref _zoom, Math.Clamp(value, 1.0, 2.5), "Zoom");
		}
	}

	public double OffsetX
	{
		get
		{
			return _offsetX;
		}
		set
		{
			SetProperty(ref _offsetX, Math.Clamp(value, -1.0, 1.0), "OffsetX");
		}
	}

	public double OffsetY
	{
		get
		{
			return _offsetY;
		}
		set
		{
			SetProperty(ref _offsetY, Math.Clamp(value, -1.0, 1.0), "OffsetY");
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

	public DashboardTileCustomizationViewModel(string key, string title, string tabKey, double left, double top, double width, double height, bool usesDashboardColor, bool allowsImageCustomization, string defaultImagePath = "", string placeholderColor = "#FF202628", string titleColor = "#FFFFFFFF", string defaultSecondaryTitle = "", bool allowsLaunchCustomization = true)
	{
		Key = key;
		DefaultTitle = title;
		DefaultSecondaryTitle = defaultSecondaryTitle;
		TabKey = tabKey;
		Left = left;
		Top = top;
		Width = width;
		Height = height;
		UsesDashboardColor = usesDashboardColor;
		AllowsImageCustomization = allowsImageCustomization;
		AllowsLaunchCustomization = allowsLaunchCustomization;
		DefaultImagePath = defaultImagePath;
		PlaceholderColor = placeholderColor;
		TitleColor = titleColor;
	}

	public void ResetImage()
	{
		ImagePath = string.Empty;
		Zoom = 1.0;
		OffsetX = 0.0;
		OffsetY = 0.0;
	}

	public void ResetTitle()
	{
		TitleOverride = string.Empty;
		SecondaryTitleOverride = string.Empty;
	}

	public void ResetLaunchTargets()
	{
		LaunchExecutablePath = string.Empty;
		LaunchWebAddress = string.Empty;
	}

	private static IBrush CreateBrush(string color, Color fallback)
	{
		try
		{
			SolidColorBrush solidColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
			if (false)
			{

			}
			return solidColorBrush;
		}
		catch
		{
			SolidColorBrush solidColorBrush2 = new SolidColorBrush(fallback);

			return solidColorBrush2;
		}
	}
}
