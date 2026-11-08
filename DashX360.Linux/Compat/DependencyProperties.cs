// WPF DependencyProperty registration API implemented as C# 14 static extension members on AvaloniaProperty
// (DependencyProperty is a global alias of AvaloniaProperty).
using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Data;

namespace XboxMetroLauncher.Compat;

public delegate void PropertyChangedCallback(AvaloniaObject d, DependencyPropertyChangedEventArgs e);

public delegate object CoerceValueCallback(AvaloniaObject d, object baseValue);

[Flags]
public enum FrameworkPropertyMetadataOptions
{
	None = 0,
	AffectsMeasure = 1,
	AffectsArrange = 2,
	AffectsParentMeasure = 4,
	AffectsParentArrange = 8,
	AffectsRender = 16,
	Inherits = 32,
	OverridesInheritanceBehavior = 64,
	NotDataBindable = 128,
	BindsTwoWayByDefault = 256,
	Journal = 1024,
	SubPropertiesDoNotAffectRender = 2048
}

public class PropertyMetadata
{
	public PropertyMetadata()
	{
	}

	public PropertyMetadata(object? defaultValue)
	{
		DefaultValue = defaultValue;
		HasDefault = true;
	}

	public PropertyMetadata(PropertyChangedCallback? propertyChangedCallback)
	{
		PropertyChangedCallback = propertyChangedCallback;
	}

	public PropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback)
	{
		DefaultValue = defaultValue;
		HasDefault = true;
		PropertyChangedCallback = propertyChangedCallback;
	}

	public PropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback)
	{
		DefaultValue = defaultValue;
		HasDefault = true;
		PropertyChangedCallback = propertyChangedCallback;
		CoerceValueCallback = coerceValueCallback;
	}

	public object? DefaultValue { get; set; }

	internal bool HasDefault { get; set; }

	public PropertyChangedCallback? PropertyChangedCallback { get; set; }

	public CoerceValueCallback? CoerceValueCallback { get; set; }
}

public class FrameworkPropertyMetadata : PropertyMetadata
{
	public FrameworkPropertyMetadata()
	{
	}

	public FrameworkPropertyMetadata(object? defaultValue) : base(defaultValue)
	{
	}

	public FrameworkPropertyMetadata(PropertyChangedCallback? propertyChangedCallback) : base(propertyChangedCallback)
	{
	}

	public FrameworkPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback) : base(defaultValue, propertyChangedCallback)
	{
	}

	public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags) : base(defaultValue)
	{
		Options = flags;
	}

	public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags, PropertyChangedCallback? propertyChangedCallback) : base(defaultValue, propertyChangedCallback)
	{
		Options = flags;
	}

	public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback) : base(defaultValue, propertyChangedCallback, coerceValueCallback)
	{
		Options = flags;
	}

	public FrameworkPropertyMetadataOptions Options { get; set; }
}

public sealed class DependencyPropertyChangedEventArgs : EventArgs
{
	public DependencyPropertyChangedEventArgs(AvaloniaProperty property, object? oldValue, object? newValue)
	{
		Property = property;
		OldValue = oldValue;
		NewValue = newValue;
	}

	public AvaloniaProperty Property { get; }

	public object? OldValue { get; }

	public object? NewValue { get; }
}

public static class DependencyPropertyRegistration
{
	extension(AvaloniaProperty)
	{
		public static AvaloniaProperty Register(string name, Type propertyType, Type ownerType)
		{
			return RegisterCore(name, propertyType, ownerType, null, attached: false);
		}

		public static AvaloniaProperty Register(string name, Type propertyType, Type ownerType, PropertyMetadata? typeMetadata)
		{
			return RegisterCore(name, propertyType, ownerType, typeMetadata, attached: false);
		}

		public static AvaloniaProperty RegisterAttached(string name, Type propertyType, Type ownerType)
		{
			return RegisterCore(name, propertyType, ownerType, null, attached: true);
		}

		public static AvaloniaProperty RegisterAttached(string name, Type propertyType, Type ownerType, PropertyMetadata? defaultMetadata)
		{
			return RegisterCore(name, propertyType, ownerType, defaultMetadata, attached: true);
		}
	}

	private static readonly MethodInfo RegisterMethod = typeof(AvaloniaProperty).GetMethods(BindingFlags.Public | BindingFlags.Static)
		.First(m => m.Name == "Register" && m.GetGenericArguments().Length == 2 && m.GetParameters().Length >= 2 && m.GetParameters()[1].Name == "defaultValue");

	private static readonly MethodInfo RegisterAttachedMethod = typeof(AvaloniaProperty).GetMethods(BindingFlags.Public | BindingFlags.Static)
		.First(m => m.Name == "RegisterAttached" && m.GetGenericArguments().Length == 2 && m.GetParameters().Length >= 2 && m.GetParameters()[1].ParameterType == typeof(Type));

	private static AvaloniaProperty RegisterCore(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata, bool attached)
	{
		object? defaultValue = metadata != null && metadata.HasDefault ? metadata.DefaultValue : (propertyType.IsValueType ? Activator.CreateInstance(propertyType) : null);
		if (defaultValue != null && !propertyType.IsInstanceOfType(defaultValue))
		{
			defaultValue = ValueConversion.ConvertTo(defaultValue, propertyType);
		}
		FrameworkPropertyMetadataOptions options = (metadata as FrameworkPropertyMetadata)?.Options ?? FrameworkPropertyMetadataOptions.None;
		bool inherits = options.HasFlag(FrameworkPropertyMetadataOptions.Inherits);
		BindingMode bindingMode = options.HasFlag(FrameworkPropertyMetadataOptions.BindsTwoWayByDefault) ? BindingMode.TwoWay : BindingMode.OneWay;
		AvaloniaProperty property;
		if (attached)
		{
			MethodInfo method = RegisterAttachedMethod.MakeGenericMethod(typeof(AvaloniaObject), propertyType);
			object?[] args = BuildArgs(method, name, ownerType, defaultValue, inherits, bindingMode);
			property = (AvaloniaProperty)method.Invoke(null, args)!;
		}
		else
		{
			MethodInfo method = RegisterMethod.MakeGenericMethod(ownerType, propertyType);
			object?[] args = BuildArgs(method, name, null, defaultValue, inherits, bindingMode);
			property = (AvaloniaProperty)method.Invoke(null, args)!;
		}
		PropertyChangedCallback? callback = metadata?.PropertyChangedCallback;
		bool affectsRender = options.HasFlag(FrameworkPropertyMetadataOptions.AffectsRender);
		bool affectsMeasure = options.HasFlag(FrameworkPropertyMetadataOptions.AffectsMeasure) || options.HasFlag(FrameworkPropertyMetadataOptions.AffectsParentMeasure);
		bool affectsArrange = options.HasFlag(FrameworkPropertyMetadataOptions.AffectsArrange) || options.HasFlag(FrameworkPropertyMetadataOptions.AffectsParentArrange);
		if (callback != null || affectsRender || affectsMeasure || affectsArrange)
		{
			property.Changed.Subscribe(new ActionObserver<AvaloniaPropertyChangedEventArgs>(e =>
			{
				if (!attached && !ownerType.IsInstanceOfType(e.Sender))
				{
					return;
				}
				if (e.Sender is Avalonia.Layout.Layoutable layoutable)
				{
					if (affectsMeasure)
					{
						layoutable.InvalidateMeasure();
					}
					if (affectsArrange)
					{
						layoutable.InvalidateArrange();
					}
				}
				if (affectsRender && e.Sender is Visual visual)
				{
					visual.InvalidateVisual();
				}
				callback?.Invoke(e.Sender, new DependencyPropertyChangedEventArgs(e.Property, e.OldValue, e.NewValue));
			}));
		}
		return property;
	}

	private static object?[] BuildArgs(MethodInfo method, string name, Type? ownerType, object? defaultValue, bool inherits, BindingMode bindingMode)
	{
		ParameterInfo[] parameters = method.GetParameters();
		object?[] args = new object?[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			ParameterInfo parameter = parameters[i];
			args[i] = parameter.Name switch
			{
				"name" => name,
				"ownerType" => ownerType,
				"defaultValue" => defaultValue,
				"inherits" => inherits,
				"defaultBindingMode" => bindingMode,
				_ => parameter.HasDefaultValue ? parameter.DefaultValue : null
			};
		}
		return args;
	}
}
