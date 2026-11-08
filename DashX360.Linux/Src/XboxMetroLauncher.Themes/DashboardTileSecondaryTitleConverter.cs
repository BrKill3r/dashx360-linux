using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.ViewModels;

namespace XboxMetroLauncher.Themes;

public sealed class DashboardTileSecondaryTitleConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var (text, text2, _) = DashboardTileImageSourceConverter.ParseParameter(parameter?.ToString());
		if (value is DashboardTileCustomizationViewModel dashboardTileCustomizationViewModel)
		{
			return dashboardTileCustomizationViewModel.SecondaryTitle;
		}
		if (value is DashboardViewModel dashboardViewModel)
		{
			return dashboardViewModel.GetDashboardTileSecondaryTitle(text, text2);
		}
		if (value is AppSettings appSettings && !string.IsNullOrWhiteSpace(text) && appSettings.DashboardTileCustomizations.TryGetValue(text, out DashboardTileCustomization value2) && !string.IsNullOrWhiteSpace(value2.SecondaryTitleOverride))
		{
			return value2.SecondaryTitleOverride;
		}
		return text2;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
