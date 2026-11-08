using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Themes;

public sealed class DashboardTilePreviewImageSourceConverter : IMultiValueConverter
{
	public object? Convert(IList<object?> values, Type targetType, object parameter, CultureInfo culture)
	{
		string text = ((values.Count == 0) ? string.Empty : values[0]?.ToString());
		string text2 = ((values.Count <= 4) ? string.Empty : values[4]?.ToString());
		string value = (string.IsNullOrWhiteSpace(text) ? text2 : text);
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		object obj = new StringToImageSourceConverter().Convert(value, targetType, "1280", culture);
		if (!(obj is BitmapSource bitmapSource))
		{
			return obj;
		}
		DashboardTileCustomization dashboardTileCustomization = new DashboardTileCustomization
		{
			ImagePath = (text ?? string.Empty),
			Zoom = ToDouble(values, 1, 1.0),
			OffsetX = ToDouble(values, 2, 0.0),
			OffsetY = ToDouble(values, 3, 0.0)
		};
		if (!(dashboardTileCustomization.Zoom <= 1.001))
		{
			return DashboardTileImageSourceConverter.CropBitmap(bitmapSource, dashboardTileCustomization);
		}
		return bitmapSource;
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		return targetTypes.Select((Type _) => Binding.DoNothing).ToArray();
	}

	private static double ToDouble(IList<object?> values, int index, double fallback)
	{
		if (values.Count > index)
		{
			object obj = values[index];
			if (obj is double)
			{
				return (double)obj;
			}
		}
		return fallback;
	}
}
