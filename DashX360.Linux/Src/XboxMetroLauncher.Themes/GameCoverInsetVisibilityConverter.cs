using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace XboxMetroLauncher.Themes;

public sealed class GameCoverInsetVisibilityConverter : IMultiValueConverter
{
	public object? Convert(IList<object?> values, Type targetType, object parameter, CultureInfo culture)
	{
		string a = ((values.Count <= 1) ? "Auto" : values[1]?.ToString());
		BitmapSource bitmapSource = GameCoverBrushConverter.LoadPreparedImage((values.Count == 0) ? null : values[0]?.ToString());
		if (bitmapSource == null)
		{
			return Visibility.Collapsed;
		}
		if (string.Equals(a, "Fit", StringComparison.OrdinalIgnoreCase))
		{
			return Visibility.Visible;
		}
		return (!GameCoverBrushConverter.NeedsInset(bitmapSource)) ? Visibility.Collapsed : Visibility.Visible;
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		return targetTypes.Select((Type _) => Binding.DoNothing).ToArray();
	}
}
