// Synchronous WPF-style dialogs (MessageBox / input prompt / clipboard) built with Avalonia windows.
// The original code calls these synchronously, so a nested DispatcherFrame is pumped until they close.
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace XboxMetroLauncher.Compat;

public enum MessageBoxButton
{
	OK = 0,
	OKCancel = 1,
	YesNoCancel = 3,
	YesNo = 4
}

public enum MessageBoxImage
{
	None = 0,
	Error = 16,
	Hand = 16,
	Stop = 16,
	Question = 32,
	Warning = 48,
	Exclamation = 48,
	Information = 64,
	Asterisk = 64
}

public enum MessageBoxResult
{
	None = 0,
	OK = 1,
	Cancel = 2,
	Yes = 6,
	No = 7
}

public static class SyncWait
{
	/// <summary>Runs the UI message loop until the task completes (WPF-style modal behavior).</summary>
	public static T Wait<T>(Task<T> task)
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			return task.GetAwaiter().GetResult();
		}
		if (!task.IsCompleted)
		{
			DispatcherFrame frame = new DispatcherFrame();
			task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
			Dispatcher.UIThread.PushFrame(frame);
		}
		return task.GetAwaiter().GetResult();
	}

	public static void Wait(Task task)
	{
		Wait(task.ContinueWith(t =>
		{
			t.GetAwaiter().GetResult();
			return true;
		}, TaskScheduler.Default));
	}

	public static Window? MainWindow => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

	public static Window? ActiveWindow
	{
		get
		{
			if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				foreach (Window window in desktop.Windows)
				{
					if (window.IsActive && window.IsVisible)
					{
						return window;
					}
				}
				return desktop.MainWindow;
			}
			return null;
		}
	}
}

public static class MessageBox
{
	public static MessageBoxResult Show(string messageBoxText) => Show(messageBoxText, "DashX360", MessageBoxButton.OK, MessageBoxImage.None);

	public static MessageBoxResult Show(string messageBoxText, string caption) => Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

	public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) => Show(messageBoxText, caption, button, MessageBoxImage.None);

	public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) => Show(messageBoxText, caption, button, icon);

	public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			return Dispatcher.UIThread.Invoke(() => Show(messageBoxText, caption, button, icon));
		}
		return SyncWait.Wait(ShowAsync(messageBoxText, caption, button, icon));
	}

	public static Task<MessageBoxResult> ShowAsync(string text, string caption, MessageBoxButton button, MessageBoxImage icon)
	{
		MessageBoxResult result = button == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;
		Window dialog = DialogChrome.Create(caption, 520);
		StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 18, 0, 0) };
		void AddButton(string label, MessageBoxResult value, bool isDefault = false, bool isCancel = false)
		{
			Button b = DialogChrome.CreateButton(label, isDefault);
			b.IsDefault = isDefault;
			b.IsCancel = isCancel;
			b.Click += (_, _) =>
			{
				result = value;
				dialog.Close();
			};
			buttons.Children.Add(b);
		}
		switch (button)
		{
		case MessageBoxButton.OK:
			AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true);
			break;
		case MessageBoxButton.OKCancel:
			AddButton("OK", MessageBoxResult.OK, isDefault: true);
			AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
			break;
		case MessageBoxButton.YesNo:
			AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
			AddButton("No", MessageBoxResult.No, isCancel: true);
			break;
		case MessageBoxButton.YesNoCancel:
			AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
			AddButton("No", MessageBoxResult.No);
			AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
			break;
		}
		string glyph = icon switch
		{
			MessageBoxImage.Error => "✖",
			MessageBoxImage.Warning => "⚠",
			MessageBoxImage.Question => "?",
			MessageBoxImage.Information => "ℹ",
			_ => string.Empty
		};
		Grid body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
		if (glyph.Length > 0)
		{
			body.Children.Add(new TextBlock { Text = glyph, FontSize = 30, Foreground = DialogChrome.Accent, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top });
		}
		TextBlock message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 16, Foreground = Brushes.White, MaxWidth = 440 };
		Grid.SetColumn(message, 1);
		body.Children.Add(message);
		DialogChrome.SetContent(dialog, body, buttons);
		return DialogChrome.ShowAsync(dialog).ContinueWith(_ => result, TaskScheduler.FromCurrentSynchronizationContext());
	}
}

public static class InputBox
{
	public static string? Show(string caption, string prompt, string initialText = "")
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			return Dispatcher.UIThread.Invoke(() => Show(caption, prompt, initialText));
		}
		string? result = null;
		Window dialog = DialogChrome.Create(caption, 480);
		TextBox box = new TextBox { Text = initialText, FontSize = 16, Margin = new Thickness(0, 10, 0, 0) };
		StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 18, 0, 0) };
		Button ok = DialogChrome.CreateButton("OK", true);
		ok.IsDefault = true;
		ok.Click += (_, _) =>
		{
			result = box.Text ?? string.Empty;
			dialog.Close();
		};
		Button cancel = DialogChrome.CreateButton("Cancel", false);
		cancel.IsCancel = true;
		cancel.Click += (_, _) => dialog.Close();
		buttons.Children.Add(ok);
		buttons.Children.Add(cancel);
		StackPanel body = new StackPanel();
		body.Children.Add(new TextBlock { Text = prompt, FontSize = 16, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
		body.Children.Add(box);
		DialogChrome.SetContent(dialog, body, buttons);
		dialog.Opened += (_, _) => box.Focus();
		SyncWait.Wait(DialogChrome.ShowAsync(dialog));
		return result;
	}
}

internal static class DialogChrome
{
	public static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#FF6BBF1A"));

	public static Window Create(string title, double width)
	{
		return new Window
		{
			Title = title,
			Width = width,
			SizeToContent = SizeToContent.Height,
			CanResize = false,
			ShowInTaskbar = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = new SolidColorBrush(Color.Parse("#FF1E2122")),
			FontFamily = new FontFamily("avares://DashX360/Fonts#Selawik"),
			Topmost = true
		};
	}

	public static Button CreateButton(string label, bool primary)
	{
		return new Button
		{
			Content = label,
			MinWidth = 90,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			Background = primary ? Accent : new SolidColorBrush(Color.Parse("#FF3A3F41")),
			Foreground = Brushes.White
		};
	}

	public static void SetContent(Window dialog, Control body, Control buttons)
	{
		StackPanel root = new StackPanel { Margin = new Thickness(24) };
		root.Children.Add(new TextBlock { Text = dialog.Title, FontSize = 22, FontWeight = FontWeight.Light, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 14) });
		root.Children.Add(body);
		root.Children.Add(buttons);
		dialog.Content = root;
		dialog.KeyDown += (_, e) =>
		{
			if (e.Key == Key.Escape)
			{
				dialog.Close();
			}
		};
	}

	public static Task ShowAsync(Window dialog)
	{
		Window? owner = SyncWait.ActiveWindow;
		if (owner != null && owner.IsVisible)
		{
			return dialog.ShowDialog(owner);
		}
		TaskCompletionSource tcs = new TaskCompletionSource();
		dialog.Closed += (_, _) => tcs.TrySetResult();
		dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		dialog.Show();
		return tcs.Task;
	}
}

/// <summary>WPF System.Windows.Clipboard (synchronous).</summary>
public static class Clipboard
{
	private static Avalonia.Input.Platform.IClipboard? Native => SyncWait.ActiveWindow?.Clipboard ?? SyncWait.MainWindow?.Clipboard;

	public static bool ContainsText()
	{
		string? text = GetText();
		return !string.IsNullOrEmpty(text);
	}

	public static string GetText()
	{
		Avalonia.Input.Platform.IClipboard? clipboard = Native;
		if (clipboard == null)
		{
			return string.Empty;
		}
		try
		{
			return SyncWait.Wait(clipboard.GetTextAsync()) ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	public static void SetText(string text)
	{
		Avalonia.Input.Platform.IClipboard? clipboard = Native;
		if (clipboard != null)
		{
			SyncWait.Wait(clipboard.SetTextAsync(text));
		}
	}
}
