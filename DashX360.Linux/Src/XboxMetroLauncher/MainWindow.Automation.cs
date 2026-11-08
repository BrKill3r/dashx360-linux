using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using XboxMetroLauncher.Input;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher;

/// <summary>
/// Developer automation (Linux port): DASHX360_TEST_ACTIONS="1500:NextTab,400:MoveRight,..." injects dashboard input
/// actions, DASHX360_SCREENSHOT=/path/shot (with DASHX360_SCREENSHOT_TIMES="3000,6000") renders the window to PNGs.
/// </summary>
public partial class MainWindow
{
	private static readonly bool AssumeActiveForAutomation = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DASHX360_TEST_ACTIONS"));

	/// <summary>Window.IsActive, except during headless automation where no window manager grants activation.</summary>
	private bool IsActiveForInput => IsActive || AssumeActiveForAutomation;

	internal void StartDeveloperAutomation()
	{
		string? actions = Environment.GetEnvironmentVariable("DASHX360_TEST_ACTIONS");
		if (!string.IsNullOrWhiteSpace(actions))
		{
			// Headless X servers have no window manager: grant focus ourselves so keyboard focus behaves like a desktop.
			DispatcherTimer.Run(() =>
			{
				foreach (Window window in Application.Current!.Windows)
				{
					if (window.IsVisible && window.TryGetPlatformHandle() is { } handle && (window.IsActive == false) && (window == this || window.Topmost))
					{
						XboxMetroLauncher.Platform.LinuxWindows.ForceInputFocus(handle.Handle);
						break;
					}
				}
				return true;
			}, TimeSpan.FromMilliseconds(500));
		}
		if (!string.IsNullOrWhiteSpace(actions))
		{
			TimeSpan at = TimeSpan.Zero;
			foreach (string item in actions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				string[] parts = item.Split(':');
				at += TimeSpan.FromMilliseconds(double.Parse(parts[0], CultureInfo.InvariantCulture));
				string name = parts.Length > 1 ? parts[1] : string.Empty;
				DispatcherTimer.RunOnce(() =>
				{
					if (name.StartsWith("cmd=", StringComparison.OrdinalIgnoreCase))
					{
						System.Reflection.PropertyInfo? property = _viewModel.GetType().GetProperty(name[4..]);
						if (property?.GetValue(_viewModel) is System.Windows.Input.ICommand command && command.CanExecute(null))
						{
							command.Execute(null);
							Console.WriteLine("[automation] executed " + name[4..]);
						}
						else
						{
							Console.WriteLine("[automation] command not available " + name[4..]);
						}
					}
					else if (name.StartsWith("shot", StringComparison.OrdinalIgnoreCase))
					{
						SaveDeveloperScreenshot(name);
					}
					else if (Enum.TryParse(name, true, out DashboardInputAction action))
					{
						HandleControllerInputAction(action);
					}
				}, at);
			}
		}
		string? shot = Environment.GetEnvironmentVariable("DASHX360_SCREENSHOT");
		if (!string.IsNullOrWhiteSpace(shot))
		{
			string times = Environment.GetEnvironmentVariable("DASHX360_SCREENSHOT_TIMES") ?? "4000";
			int index = 0;
			foreach (string time in times.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				int current = index++;
				DispatcherTimer.RunOnce(() => SaveDeveloperScreenshot(shot + "-" + current.ToString("00", CultureInfo.InvariantCulture)), TimeSpan.FromMilliseconds(double.Parse(time, CultureInfo.InvariantCulture)));
			}
		}
	}

	private void SaveDeveloperScreenshot(string name)
	{
		try
		{
			string? folder = Environment.GetEnvironmentVariable("DASHX360_SCREENSHOT_DIR");
			string path = Path.IsPathRooted(name) ? name : Path.Combine(folder ?? AppPaths.LogsFolder, name);
			if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				path += ".png";
			}
			int index = 0;
			foreach (Window window in Application.Current!.Windows)
			{
				Console.WriteLine($"[automation] window {window.GetType().Name} visible={window.IsVisible} effective={window.IsVisible} opacity={window.Opacity} bounds={window.Bounds}");
				if (!window.IsVisible)
				{
					continue;
				}
				string file = index == 0 && ReferenceEquals(window, this) ? path : path[..^4] + "-" + window.GetType().Name + ".png";
				PixelSize size = new PixelSize(Math.Max(1, (int)window.Bounds.Width), Math.Max(1, (int)window.Bounds.Height));
				using RenderTargetBitmap bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
				bitmap.Render(window);
				bitmap.Save(file);
				Console.WriteLine("[automation] screenshot " + file);
				index++;
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.SaveDeveloperScreenshot");
		}
	}
}
