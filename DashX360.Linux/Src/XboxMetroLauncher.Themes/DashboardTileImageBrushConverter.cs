using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Themes;

public sealed class DashboardTileImageBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		BitmapSource bitmapSource = GameCoverBrushConverter.LoadImage(DashboardTileImageSourceConverter.ResolveTilePath(value, parameter?.ToString()));
		if (bitmapSource == null)
		{
			return Brushes.Transparent;
		}
		DashboardTileCustomization customization = DashboardTileImageSourceConverter.GetCustomization(value, DashboardTileImageSourceConverter.ParseParameter(parameter?.ToString()).Key);
		ImageBrush imageBrush = new ImageBrush(bitmapSource)
		{
			Stretch = Stretch.UniformToFill,
			AlignmentX = AlignmentX.Center,
			AlignmentY = AlignmentY.Center,
			TileMode = TileMode.None
		};
		ApplyTileCrop(imageBrush, customization);
		if (false)
		{

		}
		return imageBrush;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}

	private static void ApplyTileCrop(ImageBrush brush, DashboardTileCustomization? customization)
	{
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		if (customization != null)
		{
			double num = Math.Clamp(customization.Zoom, 1.0, 2.5);
			double num2 = Math.Clamp(customization.OffsetX, -1.0, 1.0);
			double num3 = Math.Clamp(customization.OffsetY, -1.0, 1.0);
			double num4 = 1.0 / num;
			double num5 = 1.0 / num;
			double num6 = (1.0 - num4) / 2.0;
			double num7 = (1.0 - num5) / 2.0;
			double num8 = Math.Clamp(0.5 - num4 / 2.0 + num2 * num6, 0.0, 1.0 - num4);
			double num9 = Math.Clamp(0.5 - num5 / 2.0 + num3 * num7, 0.0, 1.0 - num5);
			brush.SourceRect = new RelativeRect(num8, num9, num4, num5, RelativeUnit.Relative);
		}
	}
}
