using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.ViewModels;

namespace XboxMetroLauncher.Themes;

public sealed class DashboardTileImageSourceConverter : IValueConverter
{
	public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		string parameter2 = parameter?.ToString();
		string value2 = ResolveTilePath(value, parameter2);
		if (string.IsNullOrWhiteSpace(value2))
		{
			return null;
		}
		DashboardTileCustomization customization = GetCustomization(value, ParseParameter(parameter2).Key);
		string parameter3 = ((!string.IsNullOrWhiteSpace(customization?.ImagePath)) ? ResolveCustomImageDecodeWidth(parameter2) : ResolveDecodeWidth(parameter2));
		object obj = new StringToImageSourceConverter().Convert(value2, targetType, parameter3, culture);
		if (customization != null && !(customization.Zoom <= 1.001) && obj is BitmapSource source)
		{
			return CropBitmap(source, customization);
		}
		return obj;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}

	internal static string ResolveTilePath(object value, string? parameter)
	{
		(string Key, string Fallback, string DecodeWidth) tuple = ParseParameter(parameter);
		string item = tuple.Key;
		string item2 = tuple.Fallback;
		DashboardTileCustomization dashboardTileCustomization;
		if (!(value is DashboardViewModel dashboardViewModel))
		{
			if (!(value is AppSettings appSettings))
			{
				if (value is DashboardTileCustomizationViewModel dashboardTileCustomizationViewModel)
				{
					if (string.IsNullOrWhiteSpace(item))
					{
						dashboardTileCustomization = new DashboardTileCustomization
						{
							ImagePath = dashboardTileCustomizationViewModel.ImagePath,
							Zoom = dashboardTileCustomizationViewModel.Zoom,
							OffsetX = dashboardTileCustomizationViewModel.OffsetX,
							OffsetY = dashboardTileCustomizationViewModel.OffsetY
						};
					}
					else
					{
						DashboardTileCustomizationViewModel dashboardTileCustomizationViewModel2 = dashboardTileCustomizationViewModel;
						dashboardTileCustomization = (string.Equals(dashboardTileCustomizationViewModel2.Key, item, StringComparison.OrdinalIgnoreCase) ? new DashboardTileCustomization
						{
							ImagePath = dashboardTileCustomizationViewModel2.ImagePath,
							Zoom = dashboardTileCustomizationViewModel2.Zoom,
							OffsetX = dashboardTileCustomizationViewModel2.OffsetX,
							OffsetY = dashboardTileCustomizationViewModel2.OffsetY
						} : null);
					}
				}
				else
				{
					dashboardTileCustomization = null;
				}
			}
			else
			{
				dashboardTileCustomization = ((!string.IsNullOrWhiteSpace(item) && appSettings.DashboardTileCustomizations.TryGetValue(item, out DashboardTileCustomization value2)) ? value2 : null);
			}
		}
		else
		{
			dashboardTileCustomization = dashboardViewModel.GetDashboardTileCustomization(item);
		}
		DashboardTileCustomization dashboardTileCustomization2 = dashboardTileCustomization;
		if (!string.IsNullOrWhiteSpace(dashboardTileCustomization2?.ImagePath))
		{
			return dashboardTileCustomization2.ImagePath;
		}
		if (!(value is DashboardTileCustomizationViewModel dashboardTileCustomizationViewModel3) || !string.IsNullOrWhiteSpace(item2))
		{
			return item2;
		}
		return dashboardTileCustomizationViewModel3.DefaultImagePath;
	}

	internal static (string Key, string Fallback, string DecodeWidth) ParseParameter(string? parameter)
	{
		string[] array = (parameter ?? string.Empty).Split('|');
		return (Key: (array.Length != 0) ? array[0] : string.Empty, Fallback: (array.Length > 1) ? array[1] : string.Empty, DecodeWidth: (array.Length > 2) ? array[2] : string.Empty);
	}

	internal static DashboardTileCustomization? GetCustomization(object value, string key)
	{
		if (!(value is DashboardViewModel dashboardViewModel))
		{
			if (!(value is AppSettings appSettings))
			{
				if (value is DashboardTileCustomizationViewModel dashboardTileCustomizationViewModel)
				{
					return new DashboardTileCustomization
					{
						ImagePath = dashboardTileCustomizationViewModel.ImagePath,
						Zoom = dashboardTileCustomizationViewModel.Zoom,
						OffsetX = dashboardTileCustomizationViewModel.OffsetX,
						OffsetY = dashboardTileCustomizationViewModel.OffsetY
					};
				}
				return null;
			}
			DashboardTileCustomization value2;
			return (!string.IsNullOrWhiteSpace(key) && appSettings.DashboardTileCustomizations.TryGetValue(key, out value2)) ? value2 : null;
		}
		return dashboardViewModel.GetDashboardTileCustomization(key);
	}

	private static string ResolveDecodeWidth(string? parameter)
	{
		string item = ParseParameter(parameter).DecodeWidth;
		if (!string.IsNullOrWhiteSpace(item))
		{
			return item;
		}
		return "512";
	}

	private static string ResolveCustomImageDecodeWidth(string? parameter)
	{
		if (int.TryParse(ResolveDecodeWidth(parameter), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return Math.Max(result, 768).ToString(CultureInfo.InvariantCulture);
		}
		return "768";
	}

	internal static BitmapSource CropBitmap(BitmapSource source, DashboardTileCustomization customization)
	{
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			double num = Math.Clamp(customization.Zoom, 1.0, 2.5);
			int num2 = Math.Max(1, (int)Math.Round((double)source.PixelWidth / num));
			int num3 = Math.Max(1, (int)Math.Round((double)source.PixelHeight / num));
			int num4 = Math.Max(0, source.PixelWidth - num2);
			int num5 = Math.Max(0, source.PixelHeight - num3);
			int num6 = Math.Clamp((int)Math.Round((double)num4 / 2.0 + customization.OffsetX * (double)num4 / 2.0), 0, num4);
			int num7 = Math.Clamp((int)Math.Round((double)num5 / 2.0 + customization.OffsetY * (double)num5 / 2.0), 0, num5);
			return BitmapOps.Crop(source, new Int32Rect(num6, num7, num2, num3));
		}
		catch
		{
			return source;
		}
	}
}
