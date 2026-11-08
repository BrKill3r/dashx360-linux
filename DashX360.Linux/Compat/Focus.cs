// WPF directional focus navigation (UIElement.MoveFocus(new TraversalRequest(direction))).
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace XboxMetroLauncher.Compat;

public enum FocusNavigationDirection
{
	Next,
	Previous,
	First,
	Last,
	Left,
	Right,
	Up,
	Down
}

public sealed class TraversalRequest
{
	public TraversalRequest(FocusNavigationDirection focusNavigationDirection)
	{
		FocusNavigationDirection = focusNavigationDirection;
	}

	public FocusNavigationDirection FocusNavigationDirection { get; }

	public bool Wrapped { get; set; }
}

public static class FocusNavigation
{
	public static bool MoveFocus(this InputElement element, TraversalRequest request)
	{
		IInputElement? next = PredictFocus(element, request.FocusNavigationDirection);
		if (next is InputElement target && !ReferenceEquals(target, element))
		{
			return target.Focus(NavigationMethod.Directional);
		}
		return false;
	}

	public static IInputElement? PredictFocus(this InputElement element, FocusNavigationDirection direction)
	{
		switch (direction)
		{
		case FocusNavigationDirection.Next:
			return KeyboardNavigationHandler.GetNext(element, NavigationDirection.Next);
		case FocusNavigationDirection.Previous:
			return KeyboardNavigationHandler.GetNext(element, NavigationDirection.Previous);
		case FocusNavigationDirection.First:
			return KeyboardNavigationHandler.GetNext(element, NavigationDirection.First);
		case FocusNavigationDirection.Last:
			return KeyboardNavigationHandler.GetNext(element, NavigationDirection.Last);
		}
		Visual? root = element.GetVisualRoot() as Visual;
		if (root == null)
		{
			return null;
		}
		Rect? originRect = BoundsIn(element, root);
		if (originRect == null)
		{
			return null;
		}
		Rect origin = originRect.Value;
		InputElement? best = null;
		double bestScore = double.MaxValue;
		foreach (InputElement candidate in EnumerateFocusable(root))
		{
			if (ReferenceEquals(candidate, element) || IsAncestorOf(candidate, element) || IsAncestorOf(element, candidate))
			{
				continue;
			}
			Rect? rect = BoundsIn(candidate, root);
			if (rect == null)
			{
				continue;
			}
			double score = Score(origin, rect.Value, direction);
			if (score < bestScore)
			{
				bestScore = score;
				best = candidate;
			}
		}
		return best;
	}

	private static bool IsAncestorOf(Visual ancestor, Visual descendant)
	{
		return descendant.GetVisualAncestors().Contains(ancestor);
	}

	private static IEnumerable<InputElement> EnumerateFocusable(Visual root)
	{
		Stack<Visual> stack = new Stack<Visual>();
		stack.Push(root);
		while (stack.Count > 0)
		{
			Visual current = stack.Pop();
			if (!current.IsVisible)
			{
				continue;
			}
			if (current is InputElement input && !input.IsEffectivelyEnabled)
			{
				continue;
			}
			if (current is InputElement focusable && focusable.Focusable && !ReferenceEquals(current, root) && KeyboardNavigation.GetIsTabStop(focusable))
			{
				yield return focusable;
			}
			foreach (Visual child in current.GetVisualChildren())
			{
				stack.Push(child);
			}
		}
	}

	private static Rect? BoundsIn(Visual visual, Visual root)
	{
		if (visual.Bounds.Width <= 0 && visual.Bounds.Height <= 0)
		{
			return null;
		}
		if (visual.Opacity <= 0.01)
		{
			return null;
		}
		Matrix? transform = visual.TransformToVisual(root);
		if (transform == null)
		{
			return null;
		}
		return new Rect(visual.Bounds.Size).TransformToAABB(transform.Value);
	}

	/// <summary>Lower is better; double.MaxValue means "not in that direction".</summary>
	private static double Score(Rect origin, Rect candidate, FocusNavigationDirection direction)
	{
		Point oc = origin.Center;
		Point cc = candidate.Center;
		double primary;
		double secondary;
		double overlap;
		switch (direction)
		{
		case FocusNavigationDirection.Left:
			if (candidate.Right > origin.Left + 1 && cc.X >= oc.X)
			{
				return double.MaxValue;
			}
			primary = Math.Max(0, origin.Left - candidate.Right);
			secondary = Math.Abs(cc.Y - oc.Y);
			overlap = Overlap(origin.Top, origin.Bottom, candidate.Top, candidate.Bottom);
			break;
		case FocusNavigationDirection.Right:
			if (candidate.Left < origin.Right - 1 && cc.X <= oc.X)
			{
				return double.MaxValue;
			}
			primary = Math.Max(0, candidate.Left - origin.Right);
			secondary = Math.Abs(cc.Y - oc.Y);
			overlap = Overlap(origin.Top, origin.Bottom, candidate.Top, candidate.Bottom);
			break;
		case FocusNavigationDirection.Up:
			if (candidate.Bottom > origin.Top + 1 && cc.Y >= oc.Y)
			{
				return double.MaxValue;
			}
			primary = Math.Max(0, origin.Top - candidate.Bottom);
			secondary = Math.Abs(cc.X - oc.X);
			overlap = Overlap(origin.Left, origin.Right, candidate.Left, candidate.Right);
			break;
		default:
			if (candidate.Top < origin.Bottom - 1 && cc.Y <= oc.Y)
			{
				return double.MaxValue;
			}
			primary = Math.Max(0, candidate.Top - origin.Bottom);
			secondary = Math.Abs(cc.X - oc.X);
			overlap = Overlap(origin.Left, origin.Right, candidate.Left, candidate.Right);
			break;
		}
		// Elements sharing the same row/column are strongly preferred (WPF-like).
		double alignmentPenalty = overlap > 0 ? 0 : 1000;
		return alignmentPenalty + primary * 2 + secondary;
	}

	private static double Overlap(double a1, double a2, double b1, double b2) => Math.Max(0, Math.Min(a2, b2) - Math.Max(a1, b1));
}
