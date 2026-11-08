using Avalonia.Interactivity;
using Avalonia.Media;
// WPF -> Avalonia compatibility layer: core primitives.
// Keeps the ported DashX360 code close to its WPF original while running on Avalonia.
using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace XboxMetroLauncher.Compat;

/// <summary>WPF Visibility enum. Hidden is treated as "invisible, but still hit-test free" (Avalonia has no reserved-space mode).</summary>
[TypeConverter(typeof(VisibilityTypeConverter))]
public enum Visibility
{
	Visible = 0,
	Hidden = 1,
	Collapsed = 2
}

public sealed class VisibilityTypeConverter : TypeConverter
{
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || sourceType == typeof(bool) || base.CanConvertFrom(context, sourceType);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
	{
		return value switch
		{
			bool b => b ? Visibility.Visible : Visibility.Collapsed,
			string s => Enum.Parse<Visibility>(s.Trim(), ignoreCase: true),
			_ => base.ConvertFrom(context, culture, value)
		};
	}
}

/// <summary>Attached properties that emulate WPF-only element properties.</summary>
public static class Wpf
{
	public static readonly AttachedProperty<Visibility> VisibilityProperty =
		AvaloniaProperty.RegisterAttached<Control, Visibility>("Visibility", typeof(Wpf), Visibility.Visible);

	/// <summary>Remembers whether a Hidden element should keep its layout slot (rendered with zero opacity).</summary>
	private static readonly AttachedProperty<IDisposable?> HiddenOpacityTokenProperty =
		AvaloniaProperty.RegisterAttached<Control, IDisposable?>("HiddenOpacityToken", typeof(Wpf));

	static Wpf()
	{
		VisibilityProperty.Changed.AddClassHandler<Control>(OnVisibilityChanged);
	}

	private static void OnVisibilityChanged(Control control, AvaloniaPropertyChangedEventArgs e)
	{
		Visibility visibility = e.NewValue is Visibility v ? v : Visibility.Visible;
		control.GetValue(HiddenOpacityTokenProperty)?.Dispose();
		control.SetValue(HiddenOpacityTokenProperty, null);
		switch (visibility)
		{
		case Visibility.Visible:
			control.IsVisible = true;
			break;
		case Visibility.Collapsed:
			control.IsVisible = false;
			break;
		case Visibility.Hidden:
			// WPF Hidden keeps the layout slot but does not render nor hit-test.
			control.IsVisible = true;
			IDisposable? opacity = control.SetValue(Visual.OpacityProperty, 0.0, BindingPriority.Animation);
			IDisposable? hit = control.SetValue(InputElement.IsHitTestVisibleProperty, false, BindingPriority.Animation);
			control.SetValue(HiddenOpacityTokenProperty, new CompositeDisposable(opacity, hit));
			break;
		}
	}

	public static Visibility GetVisibility(Control control) => control.GetValue(VisibilityProperty);

	public static void SetVisibility(Control control, Visibility value) => control.SetValue(VisibilityProperty, value);

	public static readonly AttachedProperty<object?> ToolTipProperty =
		AvaloniaProperty.RegisterAttached<Control, object?>("ToolTip", typeof(Wpf));

	internal sealed class CompositeDisposable : IDisposable
	{
		private IDisposable?[]? _items;

		public CompositeDisposable(params IDisposable?[] items)
		{
			_items = items;
		}

		public void Dispose()
		{
			IDisposable?[]? items = Interlocked.Exchange(ref _items, null);
			if (items == null)
			{
				return;
			}
			foreach (IDisposable? item in items)
			{
				item?.Dispose();
			}
		}
	}
}

/// <summary>C# 14 extension members that give Avalonia controls the WPF surface the code-behind expects.</summary>
public static class WpfControlExtensions
{
	extension(Control control)
	{
		public Visibility Visibility
		{
			get => control.GetValue(Wpf.VisibilityProperty);
			set => control.SetValue(Wpf.VisibilityProperty, value);
		}

		public double ActualWidth => control.Bounds.Width;

		public double ActualHeight => control.Bounds.Height;

		public Size RenderSize => control.Bounds.Size;

		public bool IsKeyboardFocused => control.IsFocused;

		public bool IsMouseOver => control.IsPointerOver;

		public Dispatcher Dispatcher => Dispatcher.UIThread;

		public void SetResourceReference(AvaloniaProperty property, object key)
		{
			control.Bind(property, control.GetResourceObservable(key));
		}

		public object? TryFindResource(object key)
		{
			return control.TryFindResource(key, out object? value) ? value : null;
		}

		public object FindResource(object key)
		{
			if (control.TryFindResource(key, out object? value) && value != null)
			{
				return value;
			}
			if (Avalonia.Application.Current != null && Avalonia.Application.Current.TryFindResource(key, out value) && value != null)
			{
				return value;
			}
			throw new KeyNotFoundException("Resource not found: " + key);
		}

		public void UpdateLayout()
		{
			control.UpdateLayout();
		}
	}

	extension(AvaloniaObject obj)
	{
		public Dispatcher Dispatcher => Dispatcher.UIThread;

		public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

		/// <summary>WPF Freezable.Freeze(); Avalonia objects do not need freezing.</summary>
		public void Freeze()
		{
		}

		public bool CanFreeze => false;

		public bool IsFrozen => false;
	}
}

/// <summary>WPF style Dispatcher helpers.</summary>
public static class DispatcherExtensions
{
	public static DispatcherOperation BeginInvoke(this Dispatcher dispatcher, Delegate method, params object?[]? args)
	{
		return dispatcher.InvokeAsync(() => { method.DynamicInvoke(args is { Length: > 0 } ? args : null); }, DispatcherPriority.Normal);
	}

	public static DispatcherOperation BeginInvoke(this Dispatcher dispatcher, Delegate method, DispatcherPriority priority, params object?[]? args)
	{
		return dispatcher.InvokeAsync(() => { method.DynamicInvoke(args is { Length: > 0 } ? args : null); }, priority);
	}

	public static DispatcherOperation BeginInvoke(this Dispatcher dispatcher, DispatcherPriority priority, Delegate method)
	{
		return dispatcher.InvokeAsync(() => { method.DynamicInvoke(null); }, priority);
	}

	public static DispatcherOperation BeginInvoke(this Dispatcher dispatcher, Action action, DispatcherPriority priority = default)
	{
		return dispatcher.InvokeAsync(action, priority == default ? DispatcherPriority.Normal : priority);
	}

	public static void Invoke(this Dispatcher dispatcher, Delegate method, params object?[]? args)
	{
		dispatcher.Invoke(() => { method.DynamicInvoke(args is { Length: > 0 } ? args : null); });
	}

	public static bool HasShutdownStarted(this Dispatcher dispatcher) => false;
}

/// <summary>WPF Keyboard static facade (focused element tracking via the active TopLevel).</summary>
public static class Keyboard
{
	public static readonly RoutedEvent<GotFocusEventArgs> GotKeyboardFocusEvent = InputElement.GotFocusEvent;

	public static readonly RoutedEvent<RoutedEventArgs> LostKeyboardFocusEvent = InputElement.LostFocusEvent;

	public static TopLevel? ActiveTopLevel { get; set; }

	public static IInputElement? FocusedElement
	{
		get
		{
			TopLevel? topLevel = ResolveTopLevel();
			return topLevel?.FocusManager?.GetFocusedElement();
		}
	}

	public static KeyModifiers Modifiers => KeyStateTracker.Modifiers;

	public static IInputElement? Focus(IInputElement? element)
	{
		if (element is InputElement inputElement)
		{
			inputElement.Focus(NavigationMethod.Directional);
		}
		return FocusedElement;
	}

	public static void ClearFocus()
	{
		ResolveTopLevel()?.FocusManager?.ClearFocus();
	}

	public static bool IsKeyDown(Key key) => KeyStateTracker.IsDown(key);

	public static bool IsKeyUp(Key key) => !KeyStateTracker.IsDown(key);

	private static TopLevel? ResolveTopLevel()
	{
		if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
		{
			foreach (Window window in desktop.Windows)
			{
				if (window.IsActive)
				{
					return window;
				}
			}
		}
		return ActiveTopLevel;
	}
}

/// <summary>Tracks key state (Avalonia has no global key state query).</summary>
public static class KeyStateTracker
{
	private static readonly HashSet<Key> Down = new HashSet<Key>();

	public static KeyModifiers Modifiers { get; private set; }

	public static void Attach(TopLevel topLevel)
	{
		topLevel.AddHandler(InputElement.KeyDownEvent, (_, e) =>
		{
			lock (Down)
			{
				Down.Add(e.Key);
			}
			Modifiers = e.KeyModifiers;
		}, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
		topLevel.AddHandler(InputElement.KeyUpEvent, (_, e) =>
		{
			lock (Down)
			{
				Down.Remove(e.Key);
			}
			Modifiers = e.KeyModifiers;
		}, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
		(topLevel as WindowBase)!.Deactivated += (_, _) =>
		{
			lock (Down)
			{
				Down.Clear();
			}
			Modifiers = KeyModifiers.None;
		};
	}

	public static bool IsDown(Key key)
	{
		lock (Down)
		{
			return Down.Contains(key);
		}
	}
}

public static class VisualTreeHelper
{
	public static int GetChildrenCount(AvaloniaObject reference)
	{
		return reference is Visual visual ? visual.GetVisualChildren().Count() : 0;
	}

	public static Visual GetChild(AvaloniaObject reference, int childIndex)
	{
		return ((Visual)reference).GetVisualChildren().ElementAt(childIndex);
	}

	public static Visual? GetParent(AvaloniaObject reference)
	{
		return (reference as Visual)?.GetVisualParent();
	}
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		bool flag = value is bool b && b;
		return Visibility.Visible.ToTarget(flag, targetType);
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value is Visibility v ? v == Visibility.Visible : value is bool b && b;
	}
}

public static class VisibilityConversion
{
	/// <summary>Returns either a Visibility or a bool depending on what the binding target wants.</summary>
	public static object ToTarget(this Visibility _, bool visible, Type targetType)
	{
		if (targetType == typeof(bool) || targetType == typeof(bool?))
		{
			return visible;
		}
		return visible ? Visibility.Visible : Visibility.Collapsed;
	}

	public static object ToTarget(this Visibility visibility, Type targetType)
	{
		if (targetType == typeof(bool) || targetType == typeof(bool?))
		{
			return visibility == Visibility.Visible;
		}
		return visibility;
	}
}

/// <summary>WPF Application members used by the port (MainWindow, Shutdown, Windows).</summary>
public static class WpfApplicationExtensions
{
	extension(Avalonia.Application application)
	{
		public Window? MainWindow
		{
			get => (application.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
			set
			{
				if (application.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
				{
					desktop.MainWindow = value;
				}
			}
		}

		public IReadOnlyList<Window> Windows => (application.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows ?? (IReadOnlyList<Window>)Array.Empty<Window>();

		public Dispatcher Dispatcher => Dispatcher.UIThread;

		public void Shutdown()
		{
			(application.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
		}
	}
}

public static class WpfStaticExtensions
{
	extension(Avalonia.Data.Binding)
	{
		/// <summary>WPF Binding.DoNothing.</summary>
		public static object DoNothing => Avalonia.Data.BindingOperations.DoNothing;
	}

	extension(Panel)
	{
		public static void SetZIndex(Control element, int value) => element.ZIndex = value;

		public static int GetZIndex(Control element) => element.ZIndex;
	}

	extension(Avalonia.Media.RenderOptions)
	{
		public static void SetBitmapScalingMode(Visual element, BitmapScalingMode mode) => Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(element, mode switch
		{
			BitmapScalingMode.NearestNeighbor => Avalonia.Media.Imaging.BitmapInterpolationMode.None,
			BitmapScalingMode.LowQuality or BitmapScalingMode.Linear => Avalonia.Media.Imaging.BitmapInterpolationMode.LowQuality,
			BitmapScalingMode.Unspecified => Avalonia.Media.Imaging.BitmapInterpolationMode.Unspecified,
			_ => Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality
		});
	}

	extension(Avalonia.Interactivity.RoutedEventArgs e)
	{
		public object? OriginalSource => e.Source;
	}

	extension(ScrollViewer viewer)
	{
		public void ScrollToHorizontalOffset(double offset) => viewer.Offset = new Vector(Math.Max(0, offset), viewer.Offset.Y);

		public void ScrollToVerticalOffset(double offset) => viewer.Offset = new Vector(viewer.Offset.X, Math.Max(0, offset));

		public double HorizontalOffset => viewer.Offset.X;

		public double VerticalOffset => viewer.Offset.Y;

		public double ViewportWidth => viewer.Viewport.Width;

		public double ViewportHeight => viewer.Viewport.Height;

		public double ExtentWidth => viewer.Extent.Width;

		public double ExtentHeight => viewer.Extent.Height;

		public double ScrollableWidth => Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width);

		public double ScrollableHeight => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

		public void ScrollToHome() => viewer.Offset = default;

		public void ScrollToLeftEnd() => viewer.Offset = new Vector(0, viewer.Offset.Y);
	}

	extension(Visual visual)
	{
		public bool SnapsToDevicePixels
		{
			get => true;
			set { }
		}

		public GeneralTransform TransformToAncestor(Visual ancestor) => new GeneralTransform(visual.TransformToVisual(ancestor) ?? Matrix.Identity);

		public GeneralTransform TransformToVisualW(Visual target) => new GeneralTransform(visual.TransformToVisual(target) ?? Matrix.Identity);

		public Point PointToScreenW(Point point)
		{
			PixelPoint pixel = visual.PointToScreen(point);
			return new Point(pixel.X, pixel.Y);
		}

		public void InvalidateVisualW() => visual.InvalidateVisual();
	}
}

public sealed class GeneralTransform
{
	public GeneralTransform(Matrix matrix)
	{
		Value = matrix;
	}

	public Matrix Value { get; }

	public Point Transform(Point point) => point.Transform(Value);

	public Rect TransformBounds(Rect rect) => rect.TransformToAABB(Value);

	public bool TryTransform(Point inPoint, out Point result)
	{
		result = inPoint.Transform(Value);
		return true;
	}
}

public enum BitmapScalingMode
{
	Unspecified = 0,
	LowQuality = 1,
	HighQuality = 2,
	Linear = 1,
	Fant = 2,
	NearestNeighbor = 3
}

public static class FontWeights
{
	public static readonly Avalonia.Media.FontWeight Thin = Avalonia.Media.FontWeight.Thin;
	public static readonly Avalonia.Media.FontWeight ExtraLight = Avalonia.Media.FontWeight.ExtraLight;
	public static readonly Avalonia.Media.FontWeight Light = Avalonia.Media.FontWeight.Light;
	public static readonly Avalonia.Media.FontWeight SemiLight = Avalonia.Media.FontWeight.SemiLight;
	public static readonly Avalonia.Media.FontWeight Normal = Avalonia.Media.FontWeight.Normal;
	public static readonly Avalonia.Media.FontWeight Regular = Avalonia.Media.FontWeight.Regular;
	public static readonly Avalonia.Media.FontWeight Medium = Avalonia.Media.FontWeight.Medium;
	public static readonly Avalonia.Media.FontWeight SemiBold = Avalonia.Media.FontWeight.SemiBold;
	public static readonly Avalonia.Media.FontWeight Bold = Avalonia.Media.FontWeight.Bold;
	public static readonly Avalonia.Media.FontWeight ExtraBold = Avalonia.Media.FontWeight.ExtraBold;
	public static readonly Avalonia.Media.FontWeight Black = Avalonia.Media.FontWeight.Black;
}

public static class Cursors
{
	public static readonly Cursor None = new Cursor(StandardCursorType.None);
	public static readonly Cursor Arrow = new Cursor(StandardCursorType.Arrow);
	public static readonly Cursor Hand = new Cursor(StandardCursorType.Hand);
	public static readonly Cursor Wait = new Cursor(StandardCursorType.Wait);
	public static readonly Cursor IBeam = new Cursor(StandardCursorType.Ibeam);
}

public static class ColorConverter
{
	public static object ConvertFromString(string value) => Color.Parse(value);
}

public enum CaptureMode
{
	None,
	Element,
	SubTree
}

/// <summary>WPF Mouse static facade.</summary>
public static class Mouse
{
	private static Cursor? _override;

	internal static Point LastPosition;

	internal static Avalonia.Input.IPointer? LastPointer;

	internal static Visual? LastPositionRoot;

	public static Avalonia.Input.IInputElement? Captured { get; private set; }

	public static Cursor? OverrideCursor
	{
		get => _override;
		set
		{
			_override = value;
			foreach (Window window in Avalonia.Application.Current?.Windows ?? Array.Empty<Window>())
			{
				window.Cursor = value;
			}
		}
	}

	public static Point GetPosition(Visual relativeTo)
	{
		if (LastPositionRoot == null)
		{
			return default;
		}
		Matrix? transform = LastPositionRoot.TransformToVisual(relativeTo);
		return transform.HasValue ? LastPosition.Transform(transform.Value) : LastPosition;
	}

	public static bool Capture(Avalonia.Input.IInputElement? element, CaptureMode mode = CaptureMode.Element)
	{
		Captured = element;
		LastPointer?.Capture(element);
		return true;
	}

	/// <summary>Called by windows from their pointer handlers to keep GetPosition() working.</summary>
	public static void Track(Avalonia.Input.PointerEventArgs e, Visual root)
	{
		LastPointer = e.Pointer;
		LastPositionRoot = root;
		LastPosition = e.GetPosition(root);
	}
}

public delegate void DependencyPropertyChangedEventHandler(object sender, DependencyPropertyChangedEventArgs e);

/// <summary>WPF UIElement.IsVisibleChanged emulation.</summary>
public static class IsVisibleChangedExtensions
{
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Dictionary<Delegate, EventHandler<AvaloniaPropertyChangedEventArgs>>> Handlers = new();

	public static void AddIsVisibleChanged(this Control control, DependencyPropertyChangedEventHandler handler)
	{
		EventHandler<AvaloniaPropertyChangedEventArgs> wrapper = (sender, e) =>
		{
			if (e.Property == Visual.IsVisibleProperty)
			{
				handler(control, new DependencyPropertyChangedEventArgs(e.Property, e.OldValue, e.NewValue));
			}
		};
		Handlers.GetOrCreateValue(control)[handler] = wrapper;
		control.PropertyChanged += wrapper;
	}


	public static void RemoveIsVisibleChanged(this Control control, DependencyPropertyChangedEventHandler handler)
	{
		if (Handlers.TryGetValue(control, out Dictionary<Delegate, EventHandler<AvaloniaPropertyChangedEventArgs>>? map) && map.Remove(handler, out EventHandler<AvaloniaPropertyChangedEventArgs>? wrapper))
		{
			control.PropertyChanged -= wrapper;
		}
	}
}

/// <summary>WPF brush constructors that Avalonia does not have.</summary>
public static class WpfBrushes
{
	public static LinearGradientBrush LinearGradient(GradientStops stops)
	{
		return new LinearGradientBrush { GradientStops = stops, StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
	}

	public static LinearGradientBrush LinearGradient(GradientStops stops, double angle)
	{
		LinearGradientBrush brush = LinearGradient(stops);
		ApplyAngle(brush, angle);
		return brush;
	}

	public static LinearGradientBrush LinearGradient(GradientStops stops, Point start, Point end)
	{
		return new LinearGradientBrush { GradientStops = stops, StartPoint = new RelativePoint(start, RelativeUnit.Relative), EndPoint = new RelativePoint(end, RelativeUnit.Relative) };
	}

	public static LinearGradientBrush LinearGradient(Color startColor, Color endColor, double angle)
	{
		return LinearGradient(new GradientStops { new GradientStop(startColor, 0), new GradientStop(endColor, 1) }, angle);
	}

	public static LinearGradientBrush LinearGradient(Color startColor, Color endColor, Point start, Point end)
	{
		return LinearGradient(new GradientStops { new GradientStop(startColor, 0), new GradientStop(endColor, 1) }, start, end);
	}

	public static RadialGradientBrush RadialGradient(GradientStops stops)
	{
		return new RadialGradientBrush { GradientStops = stops };
	}

	public static RadialGradientBrush RadialGradient(Color startColor, Color endColor)
	{
		return new RadialGradientBrush { GradientStops = new GradientStops { new GradientStop(startColor, 0), new GradientStop(endColor, 1) } };
	}

	private static void ApplyAngle(LinearGradientBrush brush, double angle)
	{
		double radians = angle * Math.PI / 180.0;
		Point end = new Point(Math.Cos(radians), Math.Sin(radians));
		brush.StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative);
		brush.EndPoint = new RelativePoint(end, RelativeUnit.Relative);
	}
}

public readonly record struct DpiScale(double DpiScaleX, double DpiScaleY)
{
	public double PixelsPerDip => DpiScaleY;
}

public static class VisualDpiExtensions
{
	public static DpiScale GetDpiScale(this Visual visual)
	{
		double scaling = TopLevel.GetTopLevel(visual)?.RenderScaling ?? 1.0;
		return new DpiScale(scaling, scaling);
	}
}

public static class StreamGeometryContextExtensions
{
	public static void BezierTo(this StreamGeometryContext context, Point point1, Point point2, Point point3, bool isStroked, bool isSmoothJoin) => context.CubicBezierTo(point1, point2, point3);

	public static void LineTo(this StreamGeometryContext context, Point point, bool isStroked, bool isSmoothJoin) => context.LineTo(point);

	public static void QuadraticBezierTo(this StreamGeometryContext context, Point point1, Point point2, bool isStroked, bool isSmoothJoin) => context.QuadraticBezierTo(point1, point2);

	public static void ArcTo(this StreamGeometryContext context, Point point, Size size, double rotationAngle, bool isLargeArc, SweepDirection sweepDirection, bool isStroked, bool isSmoothJoin) => context.ArcTo(point, size, rotationAngle, isLargeArc, sweepDirection);
}

public static class WpfGeometryExtensions
{
	extension(Rect)
	{
		/// <summary>WPF Rect.Empty sentinel.</summary>
		public static Rect Empty => default;
	}

	extension(Rect rect)
	{
		public bool IsEmpty => rect.Width <= 0 && rect.Height <= 0;
	}

	extension(Vector)
	{
		/// <summary>WPF Vector.Multiply(Vector, Vector) is the dot product.</summary>
		public static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y;
	}
}
