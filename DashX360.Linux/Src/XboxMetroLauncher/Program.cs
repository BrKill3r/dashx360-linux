using System;
using Avalonia;
using Avalonia.Media;

namespace XboxMetroLauncher;

internal static class Program
{
	[STAThread]
	public static int Main(string[] args)
	{
		return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	public static AppBuilder BuildAvaloniaApp()
	{
		if (Environment.GetEnvironmentVariable("DASHX360_DEBUG") == "1")
		{
			return BuildCore().LogToTextWriter(Console.Error, Avalonia.Logging.LogEventLevel.Warning);
		}
		return BuildCore().LogToTrace();
	}

	private static AppBuilder BuildCore()
	{
		return AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.With(new FontManagerOptions
			{
				DefaultFamilyName = "avares://DashX360/Fonts#Selawik",
				FontFallbacks = new[]
				{
					new FontFallback { FontFamily = new FontFamily("avares://DashX360/Fonts#Noto Sans Symbols 2") },
					new FontFallback { FontFamily = new FontFamily("Noto Sans") },
					new FontFallback { FontFamily = new FontFamily("DejaVu Sans") },
					new FontFallback { FontFamily = new FontFamily("Noto Color Emoji") }
				}
			})
			.With(new X11PlatformOptions
			{
				// Required for the transparent Guide overlay and smooth video on most GPUs.
				RenderingMode = new[] { X11RenderingMode.Glx, X11RenderingMode.Egl, X11RenderingMode.Software },
				WmClass = "DashX360"
			});
	}
}
