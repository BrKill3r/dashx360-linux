using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace XboxMetroLauncher.Themes;

public sealed class GameCoverStretchConverter : IMultiValueConverter
{
	public object? Convert(IList<object?> values, Type targetType, object parameter, CultureInfo culture)
	{
		string path = ((values.Count == 0) ? string.Empty : values[0]?.ToString());
		return ((values.Count <= 1) ? "Auto" : values[1]?.ToString()) switch
		{
			"Fit" => Stretch.Uniform, 
			"Stretch" => Stretch.UniformToFill, 
			"Cover" => Stretch.UniformToFill, 
			"Fill" => Stretch.UniformToFill, 
			_ => ChooseAutoStretch(path), 
		};
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		return targetTypes.Select((Type _) => Binding.DoNothing).ToArray();
	}

	private static Stretch ChooseAutoStretch(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return Stretch.UniformToFill;
		}
		try
		{
			path = AppPathResolver.Resolve(path);
			if (!File.Exists(path))
			{
				return Stretch.UniformToFill;
			}
			if (path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Contains($"{Path.DirectorySeparatorChar}Assets{Path.DirectorySeparatorChar}GameArt{Path.DirectorySeparatorChar}Steam{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
			{
				return Stretch.UniformToFill;
			}
			Avalonia.PixelSize? bitmapFrame = ImageLoader.ReadSize(path);
			if (bitmapFrame == null)
			{
				return Stretch.UniformToFill;
			}
			int pixelWidth = bitmapFrame.Value.Width;
			int pixelHeight = bitmapFrame.Value.Height;
			if (pixelWidth <= 0 || pixelHeight <= 0)
			{
				return Stretch.UniformToFill;
			}
			double num = (double)pixelWidth / (double)pixelHeight;
			bool flag = ((num < 0.52 || num > 2.4) ? true : false);
			return flag ? Stretch.Uniform : Stretch.UniformToFill;
		}
		catch
		{
			return Stretch.UniformToFill;
		}
	}
}
