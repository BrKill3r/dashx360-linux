using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher;

public partial class App : Application
{
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		Dispatcher.UIThread.UnhandledException += (_, args) =>
		{
			LogException(args.Exception, "Dispatcher");
			args.Handled = true;
		};
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			LogException(args.ExceptionObject as Exception, "AppDomain");
		};
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			LogException(args.Exception, "TaskScheduler");
			args.SetObserved();
		};
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
			MainWindow mainWindow = new MainWindow();
			desktop.MainWindow = mainWindow;
			Keyboard.ActiveTopLevel = mainWindow;
			AnimationTicker.FrameSource = mainWindow;
			KeyStateTracker.Attach(mainWindow);
			mainWindow.Show();
		}
		base.OnFrameworkInitializationCompleted();
	}

	internal static void LogException(Exception? exception, string source)
	{
		if (exception == null)
		{
			return;
		}
		try
		{
			File.AppendAllText(
				Path.Combine(AppPaths.LogsFolder, "crash.log"),
				$"[{DateTimeOffset.Now:u}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
		}
		catch
		{
		}
		if (Environment.GetEnvironmentVariable("DASHX360_DEBUG") == "1")
		{
			Console.Error.WriteLine($"[{source}] {exception}");
		}
	}
}
