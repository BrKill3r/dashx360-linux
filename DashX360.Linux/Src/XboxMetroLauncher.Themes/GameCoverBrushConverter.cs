using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using XboxMetroLauncher.Services;

namespace XboxMetroLauncher.Themes;

public sealed class GameCoverBrushConverter : IMultiValueConverter
{
	private const double CoverBoxRatio = 47.0 / 66.0;

	public object? Convert(IList<object?> values, Type targetType, object parameter, CultureInfo culture)
	{
		BitmapSource bitmapSource = LoadPreparedImage((values.Count == 0) ? null : values[0]?.ToString());
		if (bitmapSource == null)
		{
			return new SolidColorBrush(Color.FromRgb(153, 153, 153));
		}
		ImageBrush imageBrush = new ImageBrush(bitmapSource)
		{
			Stretch = ResolveStretch((values.Count > 1) ? values[1]?.ToString() : null),
			AlignmentX = AlignmentX.Center,
			AlignmentY = AlignmentY.Center,
			TileMode = TileMode.None
		};
		ApplyManualCrop(imageBrush, values);
		return imageBrush;
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		return targetTypes.Select((Type _) => Binding.DoNothing).ToArray();
	}

	private static Stretch ResolveStretch(string? mode)
	{
		if (string.Equals(mode, "Fill", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Cover", StringComparison.OrdinalIgnoreCase))
		{
			return Stretch.UniformToFill;
		}
		return Stretch.Uniform;
	}

	internal static BitmapSource? LoadImage(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		try
		{
			path = AppPathResolver.Resolve(path);
			if (!File.Exists(path))
			{
				return null;
			}
			return ImageCacheService.GetDecodedImage(path, 320);
		}
		catch
		{
			return null;
		}
	}

	internal static BitmapSource? LoadPreparedImage(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		try
		{
			path = AppPathResolver.Resolve(path);
			if (!File.Exists(path))
			{
				return null;
			}
			return LoadImage(path);
		}
		catch
		{
			return null;
		}
	}

	internal static BitmapSource? TrimOuterBackground(BitmapSource? source)
	{
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		if (source == null || source.PixelWidth < 12 || source.PixelHeight < 12)
		{
			return source;
		}
		try
		{
			int pixelWidth = source.PixelWidth;
			int pixelHeight = source.PixelHeight;
			byte[] pixels = BitmapOps.ReadBgra(source, out int num);
			(byte, byte, byte, byte) pixel = GetPixel(pixels, num, pixelWidth - 1, 0);
			int minimumHits = Math.Max(3, (int)Math.Round((double)pixelHeight * 0.02));
			int minimumHits2 = Math.Max(3, (int)Math.Round((double)pixelWidth * 0.02));
			int num2 = FindLeft(pixels, num, pixelWidth, pixelHeight, pixel, 24, minimumHits);
			int num3 = FindRight(pixels, num, pixelWidth, pixelHeight, pixel, 24, minimumHits);
			int num4 = FindTop(pixels, num, pixelWidth, pixelHeight, pixel, 24, minimumHits2);
			int num5 = FindBottom(pixels, num, pixelWidth, pixelHeight, pixel, 24, minimumHits2);
			if (num2 >= num3 || num4 >= num5)
			{
				return source;
			}
			int num6 = num3 - num2 + 1;
			int num7 = num5 - num4 + 1;
			if (num6 >= pixelWidth - 2 && num7 >= pixelHeight - 2)
			{
				return source;
			}
			return BitmapOps.Crop(source, new Int32Rect(num2, num4, num6, num7));
		}
		catch
		{
			return source;
		}
	}

	internal static bool NeedsInset(BitmapSource image)
	{
		if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
		{
			return false;
		}
		double num = (double)image.PixelWidth / (double)image.PixelHeight;
		if (!(num > 0.8687878787878788))
		{
			return num < 0.5554545454545455;
		}
		return true;
	}

	private static void ApplyManualCrop(ImageBrush brush, IList<object?> values)
	{
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		double num = ReadDouble(values, 2, 1.0);
		if (!(num <= 1.001))
		{
			num = Math.Clamp(num, 1.0, 1.8);
			double num2 = Math.Clamp(ReadDouble(values, 3, 0.0), -1.0, 1.0);
			double num3 = Math.Clamp(ReadDouble(values, 4, 0.0), -1.0, 1.0);
			double num4 = 1.0 / num;
			double num5 = 1.0 / num;
			double num6 = (1.0 - num4) / 2.0;
			double num7 = (1.0 - num5) / 2.0;
			double num8 = Math.Clamp(0.5 - num4 / 2.0 + num2 * num6, 0.0, 1.0 - num4);
			double num9 = Math.Clamp(0.5 - num5 / 2.0 + num3 * num7, 0.0, 1.0 - num5);
			brush.SourceRect = new RelativeRect(num8, num9, num4, num5, RelativeUnit.Relative);
		}
	}

	private static double ReadDouble(IList<object?> values, int index, double fallback)
	{
		if (values.Count <= index || values[index] == null)
		{
			return fallback;
		}
		object obj = values[index];
		if (obj is double)
		{
			return (double)obj;
		}
		if (!double.TryParse(values[index].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return fallback;
		}
		return result;
	}

	private static (byte B, byte G, byte R, byte A) GetPixel(byte[] pixels, int stride, int x, int y)
	{
		int num = y * stride + x * 4;
		return (B: pixels[num], G: pixels[num + 1], R: pixels[num + 2], A: pixels[num + 3]);
	}

	private static bool IsDifferent((byte B, byte G, byte R, byte A) pixel, (byte B, byte G, byte R, byte A) background, int threshold)
	{
		if (Math.Abs(pixel.R - background.R) <= threshold && Math.Abs(pixel.G - background.G) <= threshold && Math.Abs(pixel.B - background.B) <= threshold)
		{
			return Math.Abs(pixel.A - background.A) > threshold;
		}
		return true;
	}

	private static int FindLeft(byte[] pixels, int stride, int width, int height, (byte B, byte G, byte R, byte A) background, int threshold, int minimumHits)
	{
		for (int i = 0; i < width; i++)
		{
			int num = 0;
			for (int j = 0; j < height; j++)
			{
				if (IsDifferent(GetPixel(pixels, stride, i, j), background, threshold) && ++num >= minimumHits)
				{
					return i;
				}
			}
		}
		return 0;
	}

	private static int FindRight(byte[] pixels, int stride, int width, int height, (byte B, byte G, byte R, byte A) background, int threshold, int minimumHits)
	{
		for (int num = width - 1; num >= 0; num--)
		{
			int num2 = 0;
			for (int i = 0; i < height; i++)
			{
				if (IsDifferent(GetPixel(pixels, stride, num, i), background, threshold) && ++num2 >= minimumHits)
				{
					return num;
				}
			}
		}
		return width - 1;
	}

	private static int FindTop(byte[] pixels, int stride, int width, int height, (byte B, byte G, byte R, byte A) background, int threshold, int minimumHits)
	{
		for (int i = 0; i < height; i++)
		{
			int num = 0;
			for (int j = 0; j < width; j++)
			{
				if (IsDifferent(GetPixel(pixels, stride, j, i), background, threshold) && ++num >= minimumHits)
				{
					return i;
				}
			}
		}
		return 0;
	}

	private static int FindBottom(byte[] pixels, int stride, int width, int height, (byte B, byte G, byte R, byte A) background, int threshold, int minimumHits)
	{
		for (int num = height - 1; num >= 0; num--)
		{
			int num2 = 0;
			for (int i = 0; i < width; i++)
			{
				if (IsDifferent(GetPixel(pixels, stride, i, num), background, threshold) && ++num2 >= minimumHits)
				{
					return num;
				}
			}
		}
		return height - 1;
	}
}
