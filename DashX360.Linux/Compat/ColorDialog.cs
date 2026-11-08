// WinForms ColorDialog replacement using Avalonia's ColorView.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace XboxMetroLauncher.Compat;

public enum DialogResult
{
	None = 0,
	OK = 1,
	Cancel = 2
}

public sealed class ColorDialog : IDisposable
{
	public bool FullOpen { get; set; }

	public bool AllowFullOpen { get; set; } = true;

	public Color Color { get; set; } = Colors.White;

	public DialogResult ShowDialog()
	{
		DialogResult result = DialogResult.Cancel;
		Window dialog = DialogChrome.Create("Choose color", 560);
		ColorView view = new ColorView
		{
			Color = Color,
			IsAlphaEnabled = false,
			IsAlphaVisible = false,
			Height = 380
		};
		StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 18, 0, 0) };
		Button ok = DialogChrome.CreateButton("OK", true);
		ok.IsDefault = true;
		ok.Click += (_, _) =>
		{
			Color = view.Color;
			result = DialogResult.OK;
			dialog.Close();
		};
		Button cancel = DialogChrome.CreateButton("Cancel", false);
		cancel.IsCancel = true;
		cancel.Click += (_, _) => dialog.Close();
		buttons.Children.Add(ok);
		buttons.Children.Add(cancel);
		DialogChrome.SetContent(dialog, view, buttons);
		SyncWait.Wait(DialogChrome.ShowAsync(dialog));
		return result;
	}

	public void Dispose()
	{
	}
}

public static class ColorFormatting
{
	/// <summary>"#AARRGGBB" like WPF's Color.ToString().</summary>
	public static string ToWpfString(this Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
