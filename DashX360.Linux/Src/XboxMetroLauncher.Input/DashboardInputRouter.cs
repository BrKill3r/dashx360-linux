using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace XboxMetroLauncher.Input;

public static class DashboardInputRouter
{
	public static bool TryMapKey(KeyEventArgs args, out DashboardInputAction action)
	{
		action = DashboardInputAction.MoveLeft;
		if (IsTextEditingKey(args))
		{
			return false;
		}

		switch (args.Key)
		{
			case Key.Left:
			case Key.A:
				action = DashboardInputAction.MoveLeft;
				return true;
			case Key.Right:
			case Key.D:
				action = DashboardInputAction.MoveRight;
				return true;
			case Key.Up:
			case Key.W:
				action = DashboardInputAction.MoveUp;
				return true;
			case Key.Down:
			case Key.S:
				action = DashboardInputAction.MoveDown;
				return true;
			case Key.Enter:
			case Key.Space:
				action = DashboardInputAction.Activate;
				return true;
			case Key.Escape:
			case Key.Back:
			case Key.B:
				action = DashboardInputAction.Back;
				return true;
			case Key.X:
				action = DashboardInputAction.Details;
				return true;
			case Key.Y:
			case Key.F:
				action = DashboardInputAction.Search;
				return true;
			case Key.Q:
			case Key.PageUp:
				action = DashboardInputAction.PreviousTab;
				return true;
			case Key.E:
			case Key.PageDown:
				action = DashboardInputAction.NextTab;
				return true;
			case Key.F10:
			case Key.Apps:
				action = DashboardInputAction.Options;
				return true;
			default:
				return false;
		}
	}

	public static bool MoveFocus(DashboardInputAction action)
	{
		FocusNavigationDirection? direction = action switch
		{
			DashboardInputAction.MoveLeft => FocusNavigationDirection.Left,
			DashboardInputAction.MoveRight => FocusNavigationDirection.Right,
			DashboardInputAction.MoveUp => FocusNavigationDirection.Up,
			DashboardInputAction.MoveDown => FocusNavigationDirection.Down,
			_ => null,
		};
		if (!direction.HasValue || Keyboard.FocusedElement is not InputElement element)
		{
			return false;
		}

		try
		{
			return element.MoveFocus(new TraversalRequest(direction.Value));
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static bool ActivateFocusedElement()
	{
		if (Keyboard.FocusedElement is Button button)
		{
			if (button.Command != null)
			{
				if (!button.Command.CanExecute(button.CommandParameter))
				{
					return false;
				}
				try
				{
					button.Command.Execute(button.CommandParameter);
					return true;
				}
				catch
				{
					return false;
				}
			}
			try
			{
				button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
				return true;
			}
			catch
			{
				return false;
			}
		}
		if (Keyboard.FocusedElement is CheckBox checkBox)
		{
			checkBox.IsChecked = checkBox.IsChecked != true;
			return true;
		}
		if (Keyboard.FocusedElement is ComboBox comboBox)
		{
			comboBox.IsDropDownOpen = !comboBox.IsDropDownOpen;
			return true;
		}
		if (Keyboard.FocusedElement is Slider slider)
		{
			slider.Value = Math.Clamp(slider.Value + slider.SmallChange, slider.Minimum, slider.Maximum);
			return true;
		}
		if (Keyboard.FocusedElement is TextBox)
		{
			return true;
		}
		return false;
	}

	public static bool TryAdjustFocusedSetting(DashboardInputAction action)
	{
		if (Keyboard.FocusedElement is Slider slider && (action == DashboardInputAction.MoveLeft || action == DashboardInputAction.MoveRight))
		{
			double delta = action == DashboardInputAction.MoveRight ? slider.SmallChange : -slider.SmallChange;
			slider.Value = Math.Clamp(slider.Value + delta, slider.Minimum, slider.Maximum);
			return true;
		}
		if (Keyboard.FocusedElement is ComboBox comboBox && (action == DashboardInputAction.MoveLeft || action == DashboardInputAction.MoveRight))
		{
			if (comboBox.Items.Count == 0)
			{
				return false;
			}
			int delta = action == DashboardInputAction.MoveRight ? 1 : -1;
			int next = Math.Clamp(comboBox.SelectedIndex + delta, 0, comboBox.Items.Count - 1);
			if (next == comboBox.SelectedIndex)
			{
				return true;
			}
			comboBox.SelectedIndex = next;
			return true;
		}
		return false;
	}

	private static bool IsTextEditingKey(KeyEventArgs args)
	{
		if (Keyboard.FocusedElement is not TextBox)
		{
			return false;
		}

		return args.Key is not (Key.Enter or Key.Escape or Key.Up or Key.Down);
	}
}
