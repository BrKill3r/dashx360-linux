// Resolves WPF property names / property paths to Avalonia properties and converts XAML values.
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace XboxMetroLauncher.Compat;

public static class WpfPropertyMap
{
	/// <summary>WPF names (optionally owner-qualified) that have a different Avalonia counterpart.</summary>
	private static readonly Dictionary<string, Func<AvaloniaProperty?>> Aliases = new Dictionary<string, Func<AvaloniaProperty?>>(StringComparer.Ordinal)
	{
		["Visibility"] = () => Wpf.VisibilityProperty,
		["IsMouseOver"] = () => InputElement.IsPointerOverProperty,
		["IsKeyboardFocused"] = () => InputElement.IsFocusedProperty,
		["IsKeyboardFocusWithin"] = () => InputElement.IsKeyboardFocusWithinProperty,
		["IsFocused"] = () => InputElement.IsFocusedProperty,
		["IsStylusOver"] = () => InputElement.IsPointerOverProperty,
		["ZIndex"] = () => Visual.ZIndexProperty,
		["Panel.ZIndex"] = () => Visual.ZIndexProperty,
		["ToolTip"] = () => ToolTip.TipProperty,
		["TextElement.Foreground"] = () => TextElement.ForegroundProperty,
		["TextElement.FontSize"] = () => TextElement.FontSizeProperty,
		["TextElement.FontWeight"] = () => TextElement.FontWeightProperty,
		["TextElement.FontFamily"] = () => TextElement.FontFamilyProperty,
		["TextBlock.Foreground"] = () => TextElement.ForegroundProperty,
		["TextBlock.FontSize"] = () => TextElement.FontSizeProperty,
		["TextBlock.FontWeight"] = () => TextElement.FontWeightProperty,
		["TextBlock.FontFamily"] = () => TextElement.FontFamilyProperty,
		["TextBlock.TextAlignment"] = () => TextBlock.TextAlignmentProperty,
		["Control.Foreground"] = () => TemplatedControl.ForegroundProperty,
		["Control.Background"] = () => TemplatedControl.BackgroundProperty,
		["Control.BorderBrush"] = () => TemplatedControl.BorderBrushProperty,
		["Control.BorderThickness"] = () => TemplatedControl.BorderThicknessProperty,
		["Control.Padding"] = () => TemplatedControl.PaddingProperty,
		["Control.FontSize"] = () => TemplatedControl.FontSizeProperty,
		["Control.FontWeight"] = () => TemplatedControl.FontWeightProperty,
		["Control.FontFamily"] = () => TemplatedControl.FontFamilyProperty,
		["Control.Template"] = () => TemplatedControl.TemplateProperty,
		["Control.HorizontalContentAlignment"] = () => ContentControl.HorizontalContentAlignmentProperty,
		["Control.VerticalContentAlignment"] = () => ContentControl.VerticalContentAlignmentProperty,
		["Canvas.Left"] = () => Canvas.LeftProperty,
		["Canvas.Top"] = () => Canvas.TopProperty,
		["Canvas.Right"] = () => Canvas.RightProperty,
		["Canvas.Bottom"] = () => Canvas.BottomProperty,
		["Grid.Row"] = () => Grid.RowProperty,
		["Grid.Column"] = () => Grid.ColumnProperty,
		["Grid.RowSpan"] = () => Grid.RowSpanProperty,
		["Grid.ColumnSpan"] = () => Grid.ColumnSpanProperty,
		["ScrollViewer.HorizontalScrollBarVisibility"] = () => ScrollViewer.HorizontalScrollBarVisibilityProperty,
		["ScrollViewer.VerticalScrollBarVisibility"] = () => ScrollViewer.VerticalScrollBarVisibilityProperty,
		
	};

	/// <summary>Owner prefixes that only exist in WPF; the bare property name is looked up on the target instead.</summary>
	private static readonly HashSet<string> TransparentOwners = new HashSet<string>(StringComparer.Ordinal)
	{
		"UIElement", "FrameworkElement", "Control", "ContentControl", "ButtonBase", "Selector", "Shape", "Panel", "Border", "Visual", "Animatable", "Freezable",
		"TextBlock", "ToggleButton", "ListBoxItem", "ComboBoxItem", "Button", "Image", "Grid", "Canvas", "StackPanel", "Rectangle", "Ellipse", "Path", "Window",
		"ItemsControl", "ListBox", "ComboBox", "ScrollViewer", "TextBox", "TextBoxBase", "RangeBase", "Slider", "ProgressBar", "CheckBox", "ContentPresenter",
		"Brush", "SolidColorBrush", "GradientStop", "LinearGradientBrush", "RadialGradientBrush", "GradientBrush", "Transform", "ScaleTransform", "TranslateTransform",
		"RotateTransform", "SkewTransform", "TransformGroup", "DropShadowEffect", "Effect", "Viewbox", "UserControl", "Decorator", "Popup", "MediaElement", "Line",
		"Geometry", "RectangleGeometry", "EllipseGeometry", "ImageBrush", "TileBrush", "Pen", "Run", "Inline", "TextElement"
	};

	/// <summary>Names that have no Avalonia equivalent and should be ignored silently.</summary>
	private static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.Ordinal)
	{
		"FocusVisualStyle", "SnapsToDevicePixels", "TextFormattingMode", "TextRenderingMode", "TextHintingMode", "BitmapEffect", "OverridesDefaultStyle",
		"CacheMode", "IsTabStop", "CaretBrush", "SelectionBrush", "RecognizesAccessKey", "ForceCursor", "AllowsTransparency", "CornerRadiusTarget"
	};

	public static bool IsIgnored(string name)
	{
		int dot = name.LastIndexOf('.');
		string bare = dot >= 0 ? name[(dot + 1)..] : name;
		return Ignored.Contains(bare);
	}

	public static AvaloniaProperty? Resolve(Type targetType, string wpfName)
	{
		string name = wpfName.Trim().Trim('(', ')');
		if (Aliases.TryGetValue(name, out Func<AvaloniaProperty?>? alias))
		{
			return alias();
		}
		string owner = string.Empty;
		string bare = name;
		int dot = name.LastIndexOf('.');
		if (dot >= 0)
		{
			owner = name[..dot];
			bare = name[(dot + 1)..];
		}
		if (Ignored.Contains(bare))
		{
			return null;
		}
		if (Aliases.TryGetValue(bare, out alias))
		{
			AvaloniaProperty? aliased = alias();
			if (aliased != null && (aliased.IsAttached || aliased.OwnerType.IsAssignableFrom(targetType)))
			{
				return aliased;
			}
		}
		AvaloniaProperty? property = AvaloniaPropertyRegistry.Instance.FindRegistered(targetType, bare);
		if (property != null)
		{
			return property;
		}
		if (bare == "IsPressed")
		{
			return AvaloniaPropertyRegistry.Instance.FindRegistered(targetType, "IsPressed");
		}
		if (owner.Length > 0 && !TransparentOwners.Contains(owner))
		{
			Type? ownerType = ResolveOwnerType(owner);
			if (ownerType != null)
			{
				foreach (AvaloniaProperty attached in AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(ownerType))
				{
					if (attached.Name == bare)
					{
						return attached;
					}
				}
				property = AvaloniaPropertyRegistry.Instance.FindRegistered(ownerType, bare);
				if (property != null && property.IsAttached)
				{
					return property;
				}
			}
		}
		// Attached properties declared by the application (e.g. controls:TileSelectionChrome.IsActive).
		if (owner.Length > 0)
		{
			Type? ownerType = ResolveOwnerType(owner);
			if (ownerType != null)
			{
				foreach (AvaloniaProperty attached in AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(ownerType))
				{
					if (attached.Name == bare)
					{
						return attached;
					}
				}
			}
		}
		return null;
	}

	private static readonly Dictionary<string, Type?> OwnerTypeCache = new Dictionary<string, Type?>(StringComparer.Ordinal);

	public static Type? ResolveType(string name) => ResolveOwnerType(name);

	private static Type? ResolveOwnerType(string owner)
	{
		string bare = owner.Contains(':') ? owner[(owner.IndexOf(':') + 1)..] : owner;
		if (OwnerTypeCache.TryGetValue(bare, out Type? cached))
		{
			return cached;
		}
		Type? found = null;
		string[] namespaces =
		{
			"Avalonia.Controls.", "Avalonia.Controls.Primitives.", "Avalonia.Media.", "Avalonia.Input.", "Avalonia.Layout.", "Avalonia.Controls.Documents.", "Avalonia.",
			"XboxMetroLauncher.Controls.", "XboxMetroLauncher.Compat.", "XboxMetroLauncher.Themes.", "XboxMetroLauncher."
		};
		foreach (System.Reflection.Assembly assembly in new[] { typeof(Control).Assembly, typeof(AvaloniaObject).Assembly, typeof(WpfPropertyMap).Assembly })
		{
			foreach (string ns in namespaces)
			{
				found = assembly.GetType(ns + bare, throwOnError: false);
				if (found != null)
				{
					break;
				}
			}
			if (found != null)
			{
				break;
			}
		}
		if (found != null)
		{
			System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(found.TypeHandle);
		}
		OwnerTypeCache[bare] = found;
		return found;
	}
}

public static class PropertyPathResolver
{
	private static readonly Regex IndexRegex = new Regex(@"^(?<name>.*?)\[(?<index>\d+)\]$", RegexOptions.Compiled);

	public static bool TryResolve(AvaloniaObject root, PropertyPath path, out AvaloniaObject? owner, out AvaloniaProperty? property)
	{
		owner = null;
		property = null;
		if (path.PathParameters.Length > 0 && path.Path.Trim() == "(0)" && path.PathParameters[0] is AvaloniaProperty direct)
		{
			owner = root;
			property = direct;
			return true;
		}
		List<string> segments = Split(path.Path);
		object? current = root;
		for (int i = 0; i < segments.Count; i++)
		{
			string segment = segments[i];
			int? index = null;
			Match match = IndexRegex.Match(segment);
			if (match.Success)
			{
				segment = match.Groups["name"].Value;
				index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
			}
			segment = segment.Trim();
			if (segment.StartsWith('(') && segment.EndsWith(')'))
			{
				segment = segment[1..^1];
			}
			if (segment.Length > 0 && int.TryParse(segment, out int parameterIndex) && parameterIndex < path.PathParameters.Length && path.PathParameters[parameterIndex] is AvaloniaProperty parameterProperty)
			{
				if (current is not AvaloniaObject parameterOwner)
				{
					return false;
				}
				if (i == segments.Count - 1 && index == null)
				{
					owner = parameterOwner;
					property = parameterProperty;
					return true;
				}
				current = parameterOwner.GetValue(parameterProperty);
			}
			else if (segment.Length > 0)
			{
				if (current is not AvaloniaObject avaloniaObject)
				{
					return false;
				}
				AvaloniaProperty? resolved = WpfPropertyMap.Resolve(avaloniaObject.GetType(), segment);
				if (resolved == null)
				{
					// Plain CLR property (e.g. TransformGroup.Children).
					System.Reflection.PropertyInfo? clr = avaloniaObject.GetType().GetProperty(segment.Contains('.') ? segment[(segment.LastIndexOf('.') + 1)..] : segment);
					if (clr == null)
					{
						return false;
					}
					current = clr.GetValue(avaloniaObject);
				}
				else if (i == segments.Count - 1 && index == null)
				{
					owner = avaloniaObject;
					property = resolved;
					return true;
				}
				else
				{
					current = avaloniaObject.GetValue(resolved);
				}
			}
			if (index.HasValue)
			{
				if (current is IList list && index.Value < list.Count)
				{
					current = list[index.Value];
				}
				else
				{
					return false;
				}
			}
		}
		return false;
	}

	private static List<string> Split(string path)
	{
		List<string> segments = new List<string>();
		int depth = 0;
		int start = 0;
		for (int i = 0; i < path.Length; i++)
		{
			char c = path[i];
			if (c == '(')
			{
				depth++;
			}
			else if (c == ')')
			{
				depth--;
			}
			else if (c == '.' && depth == 0)
			{
				segments.Add(path[start..i]);
				start = i + 1;
			}
		}
		segments.Add(path[start..]);
		return segments;
	}
}

public static class NameLookup
{
	/// <summary>Finds a named element near <paramref name="scope"/> (namescope first, then visual/logical descendants, then ancestors' scopes).</summary>
	public static AvaloniaObject? Find(StyledElement? scope, string name)
	{
		if (scope == null || string.IsNullOrEmpty(name))
		{
			return null;
		}
		if (scope.Name == name)
		{
			return scope;
		}
		INameScope? nameScope = NameScope.GetNameScope(scope) ?? scope.FindNameScope();
		if (nameScope?.Find(name) is AvaloniaObject found)
		{
			return found;
		}
		AvaloniaObject? descendant = FindDescendant(scope, name, null);
		if (descendant != null)
		{
			return descendant;
		}
		StyledElement? parent = scope.Parent as StyledElement;
		while (parent != null)
		{
			INameScope? parentScope = NameScope.GetNameScope(parent);
			if (parentScope?.Find(name) is AvaloniaObject parentFound)
			{
				return parentFound;
			}
			parent = parent.Parent as StyledElement;
		}
		return null;
	}

	/// <summary>Finds a template part by name, preferring elements created by the same template.</summary>
	public static AvaloniaObject? FindTemplatePart(StyledElement root, AvaloniaObject? templatedParent, string name)
	{
		if (root.Name == name)
		{
			return root;
		}
		INameScope? nameScope = NameScope.GetNameScope(root);
		if (nameScope?.Find(name) is AvaloniaObject scoped)
		{
			return scoped;
		}
		return FindDescendant(root, name, templatedParent) ?? FindDescendant(root, name, null);
	}

	private static AvaloniaObject? FindDescendant(StyledElement root, string name, AvaloniaObject? templatedParent)
	{
		Queue<StyledElement> queue = new Queue<StyledElement>();
		HashSet<StyledElement> seen = new HashSet<StyledElement>();
		queue.Enqueue(root);
		while (queue.Count > 0)
		{
			StyledElement current = queue.Dequeue();
			if (!seen.Add(current))
			{
				continue;
			}
			if (!ReferenceEquals(current, root) && current.Name == name && (templatedParent == null || ReferenceEquals(current.TemplatedParent, templatedParent)))
			{
				return current;
			}
			if (current is Visual visual)
			{
				foreach (Visual child in visual.GetVisualChildren())
				{
					if (child is StyledElement styled)
					{
						queue.Enqueue(styled);
					}
				}
			}
			if (current is ILogical logical)
			{
				foreach (ILogical child in logical.LogicalChildren)
				{
					if (child is StyledElement styled)
					{
						queue.Enqueue(styled);
					}
				}
			}
		}
		return null;
	}
}

public static class ValueConversion
{
	public static object? ConvertTo(object? value, Type targetType)
	{
		if (value == null)
		{
			return null;
		}
		if (targetType.IsInstanceOfType(value))
		{
			return value;
		}
		Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
		if (value is Visibility visibility && type == typeof(bool))
		{
			return visibility == Visibility.Visible;
		}
		if (value is bool boolean && type == typeof(Visibility))
		{
			return boolean ? Visibility.Visible : Visibility.Collapsed;
		}
		if (value is Color color && typeof(IBrush).IsAssignableFrom(type))
		{
			return new SolidColorBrush(color);
		}
		if (value is not string text)
		{
			if (value is IConvertible && (type.IsPrimitive || type == typeof(decimal)))
			{
				return System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
			}
			return value;
		}
		text = text.Trim();
		if (text == "{x:Null}")
		{
			return null;
		}
		if (type == typeof(string) || type == typeof(object))
		{
			return text;
		}
		if (type == typeof(double))
		{
			if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase) || text.Equals("NaN", StringComparison.OrdinalIgnoreCase))
			{
				return double.NaN;
			}
			if (text.Equals("Infinity", StringComparison.OrdinalIgnoreCase))
			{
				return double.PositiveInfinity;
			}
			return double.Parse(text, CultureInfo.InvariantCulture);
		}
		if (type == typeof(float))
		{
			return float.Parse(text, CultureInfo.InvariantCulture);
		}
		if (type == typeof(int))
		{
			return int.Parse(text, CultureInfo.InvariantCulture);
		}
		if (type == typeof(bool))
		{
			return bool.Parse(text);
		}
		if (type.IsEnum)
		{
			string enumText = MapEnumName(type, text);
			return Enum.Parse(type, enumText, ignoreCase: true);
		}
		if (typeof(IBrush).IsAssignableFrom(type))
		{
			return Brush.Parse(text);
		}
		if (type == typeof(Color))
		{
			return Color.Parse(text);
		}
		if (type == typeof(Thickness))
		{
			return Thickness.Parse(text);
		}
		if (type == typeof(CornerRadius))
		{
			return CornerRadius.Parse(text);
		}
		if (type == typeof(FontFamily))
		{
			return new FontFamily(text);
		}
		if (type == typeof(FontWeight))
		{
			return Enum.Parse<FontWeight>(text, ignoreCase: true);
		}
		if (type == typeof(GridLength))
		{
			return GridLength.Parse(text);
		}
		if (type == typeof(RelativePoint))
		{
			return WpfGeometry.ParseRelativePoint(text);
		}
		if (type == typeof(Point))
		{
			return Point.Parse(text);
		}
		if (type == typeof(Size))
		{
			return Size.Parse(text);
		}
		if (type == typeof(Rect))
		{
			return Rect.Parse(text);
		}
		if (type == typeof(Cursor))
		{
			return new Cursor(text.Equals("Hand", StringComparison.OrdinalIgnoreCase) ? StandardCursorType.Hand : Enum.Parse<StandardCursorType>(text, ignoreCase: true));
		}
		if (typeof(Geometry).IsAssignableFrom(type))
		{
			return Geometry.Parse(text);
		}
		if (type == typeof(ITransform) || typeof(Transform).IsAssignableFrom(type))
		{
			return null;
		}
		if (type == typeof(IImage) || typeof(IImage).IsAssignableFrom(type))
		{
			return ImageLoader.Load(text);
		}
		TypeConverter converter = TypeDescriptor.GetConverter(type);
		if (converter.CanConvertFrom(typeof(string)))
		{
			return converter.ConvertFromInvariantString(text);
		}
		System.Reflection.MethodInfo? parse = type.GetMethod("Parse", new[] { typeof(string) });
		if (parse != null && parse.IsStatic)
		{
			return parse.Invoke(null, new object[] { text });
		}
		return text;
	}

	private static string MapEnumName(Type enumType, string text)
	{
		if (enumType == typeof(StandardCursorType) && text == "Hand")
		{
			return "Hand";
		}
		if (enumType == typeof(BitmapInterpolationMode))
		{
			return text switch
			{
				"HighQuality" or "Fant" => "HighQuality",
				"NearestNeighbor" => "None",
				"LowQuality" or "Linear" => "LowQuality",
				_ => text
			};
		}
		if (enumType == typeof(TextTrimming))
		{
			return text;
		}
		return text;
	}

	/// <summary>WPF-style loose equality used by triggers (Value="True" vs bool, enum names, numbers).</summary>
	public static bool LooseEquals(object? actual, object? expected)
	{
		if (expected is string expectedText)
		{
			expectedText = expectedText.Trim();
			if (expectedText == "{x:Null}")
			{
				return actual == null;
			}
			if (actual == null)
			{
				return false;
			}
			if (actual is string actualText)
			{
				return string.Equals(actualText, expectedText, StringComparison.Ordinal);
			}
			if (actual is bool b)
			{
				return bool.TryParse(expectedText, out bool parsed) && parsed == b;
			}
			if (actual is Visibility visibility)
			{
				return Enum.TryParse(expectedText, true, out Visibility parsedVisibility) && parsedVisibility == visibility;
			}
			if (actual is Enum)
			{
				return string.Equals(actual.ToString(), expectedText, StringComparison.OrdinalIgnoreCase);
			}
			if (actual is IConvertible && double.TryParse(expectedText, NumberStyles.Float, CultureInfo.InvariantCulture, out double expectedNumber))
			{
				try
				{
					return Math.Abs(System.Convert.ToDouble(actual, CultureInfo.InvariantCulture) - expectedNumber) < 1e-9;
				}
				catch
				{
					return false;
				}
			}
			return string.Equals(System.Convert.ToString(actual, CultureInfo.InvariantCulture), expectedText, StringComparison.Ordinal);
		}
		if (actual == null || expected == null)
		{
			return actual == null && expected == null;
		}
		if (actual is Visibility v && expected is bool eb)
		{
			return (v == Visibility.Visible) == eb;
		}
		return actual.Equals(expected);
	}
}

public static class WpfGeometry
{
	/// <summary>WPF RenderTransformOrigin "0.5,0.5" is relative; Avalonia's RelativePoint parser expects "50%,50%".</summary>
	public static RelativePoint ParseRelativePoint(string text)
	{
		if (text.Contains('%'))
		{
			return RelativePoint.Parse(text);
		}
		string[] parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		return new RelativePoint(double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture), RelativeUnit.Relative);
	}
}
