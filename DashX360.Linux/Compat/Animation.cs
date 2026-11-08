// WPF animation model (Timeline / Storyboard / BeginAnimation) implemented on top of Avalonia's
// property system. Animated values are applied with BindingPriority.Animation so that, as in WPF,
// they override local values and fall back to the base value when the animation is removed.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Threading;

namespace XboxMetroLauncher.Compat;

#region Value types

[TypeConverter(typeof(DurationTypeConverter))]
public readonly struct Duration : IEquatable<Duration>
{
	private readonly TimeSpan _timeSpan;

	private readonly int _kind; // 0 = TimeSpan, 1 = Automatic, 2 = Forever

	public Duration(TimeSpan timeSpan)
	{
		_timeSpan = timeSpan;
		_kind = 0;
	}

	private Duration(int kind)
	{
		_timeSpan = TimeSpan.Zero;
		_kind = kind;
	}

	public static Duration Automatic => new Duration(1);

	public static Duration Forever => new Duration(2);

	public bool HasTimeSpan => _kind == 0;

	public bool IsAutomatic => _kind == 1;

	public bool IsForever => _kind == 2;

	public TimeSpan TimeSpan => _kind == 0 ? _timeSpan : throw new InvalidOperationException("Duration has no TimeSpan.");

	public static implicit operator Duration(TimeSpan timeSpan) => new Duration(timeSpan);

	public static Duration Parse(string text)
	{
		string trimmed = text.Trim();
		if (trimmed.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
		{
			return Automatic;
		}
		if (trimmed.Equals("Forever", StringComparison.OrdinalIgnoreCase))
		{
			return Forever;
		}
		return new Duration(TimeSpan.Parse(trimmed, CultureInfo.InvariantCulture));
	}

	public bool Equals(Duration other) => _kind == other._kind && _timeSpan == other._timeSpan;

	public override bool Equals(object? obj) => obj is Duration d && Equals(d);

	public override int GetHashCode() => HashCode.Combine(_kind, _timeSpan);

	public static bool operator ==(Duration a, Duration b) => a.Equals(b);

	public static bool operator !=(Duration a, Duration b) => !a.Equals(b);

	public override string ToString() => _kind switch { 1 => "Automatic", 2 => "Forever", _ => _timeSpan.ToString() };
}

public sealed class DurationTypeConverter : TypeConverter
{
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || sourceType == typeof(TimeSpan);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value switch
	{
		TimeSpan ts => new Duration(ts),
		string s => Duration.Parse(s),
		_ => throw new NotSupportedException()
	};
}

[TypeConverter(typeof(RepeatBehaviorTypeConverter))]
public readonly struct RepeatBehavior
{
	private readonly double _count;

	private readonly TimeSpan _duration;

	private readonly int _kind; // 0 = count, 1 = duration, 2 = forever

	public RepeatBehavior(double count)
	{
		_count = count;
		_duration = TimeSpan.Zero;
		_kind = 0;
	}

	public RepeatBehavior(TimeSpan duration)
	{
		_count = 0;
		_duration = duration;
		_kind = 1;
	}

	private RepeatBehavior(int kind)
	{
		_count = 0;
		_duration = TimeSpan.Zero;
		_kind = kind;
	}

	public static RepeatBehavior Forever => new RepeatBehavior(2);

	public bool HasCount => _kind == 0;

	public bool HasDuration => _kind == 1;

	public bool IsForever => _kind == 2;

	public double Count => _count;

	public TimeSpan Duration => _duration;

	public static RepeatBehavior Parse(string text)
	{
		string trimmed = text.Trim();
		if (trimmed.Equals("Forever", StringComparison.OrdinalIgnoreCase))
		{
			return Forever;
		}
		if (trimmed.EndsWith('x') || trimmed.EndsWith('X'))
		{
			return new RepeatBehavior(double.Parse(trimmed[..^1], CultureInfo.InvariantCulture));
		}
		return new RepeatBehavior(TimeSpan.Parse(trimmed, CultureInfo.InvariantCulture));
	}
}

public sealed class RepeatBehaviorTypeConverter : TypeConverter
{
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => RepeatBehavior.Parse((string)value);
}

public enum FillBehavior
{
	HoldEnd,
	Stop
}

public enum HandoffBehavior
{
	SnapshotAndReplace,
	Compose
}

public enum EasingMode
{
	EaseIn,
	EaseOut,
	EaseInOut
}

#endregion

#region Easing

public interface IEasingFunction
{
	double Ease(double normalizedTime);
}

public abstract class EasingFunctionBase : IEasingFunction
{
	public EasingMode EasingMode { get; set; } = EasingMode.EaseOut;

	public double Ease(double normalizedTime)
	{
		double t = Math.Clamp(normalizedTime, 0.0, 1.0);
		return EasingMode switch
		{
			EasingMode.EaseIn => EaseInCore(t),
			EasingMode.EaseOut => 1.0 - EaseInCore(1.0 - t),
			_ => t < 0.5 ? EaseInCore(t * 2.0) * 0.5 : (1.0 - EaseInCore((1.0 - t) * 2.0)) * 0.5 + 0.5
		};
	}

	protected abstract double EaseInCore(double normalizedTime);
}

public sealed class SineEase : EasingFunctionBase
{
	protected override double EaseInCore(double t) => 1.0 - Math.Sin(Math.PI * 0.5 * (1.0 - t));
}

public sealed class QuadraticEase : EasingFunctionBase
{
	protected override double EaseInCore(double t) => t * t;
}

public sealed class CubicEase : EasingFunctionBase
{
	protected override double EaseInCore(double t) => t * t * t;
}

public sealed class QuarticEase : EasingFunctionBase
{
	protected override double EaseInCore(double t) => t * t * t * t;
}

public sealed class QuinticEase : EasingFunctionBase
{
	protected override double EaseInCore(double t) => t * t * t * t * t;
}

public sealed class PowerEase : EasingFunctionBase
{
	public double Power { get; set; } = 2.0;

	protected override double EaseInCore(double t) => Math.Pow(t, Math.Max(0.0, Power));
}

public sealed class CircleEase : EasingFunctionBase
{
	protected override double EaseInCore(double t) => 1.0 - Math.Sqrt(1.0 - t * t);
}

public sealed class ExponentialEase : EasingFunctionBase
{
	public double Exponent { get; set; } = 2.0;

	protected override double EaseInCore(double t)
	{
		double exponent = Exponent;
		if (Math.Abs(exponent) < 1e-6)
		{
			return t;
		}
		return (Math.Exp(exponent * t) - 1.0) / (Math.Exp(exponent) - 1.0);
	}
}

public sealed class BackEase : EasingFunctionBase
{
	public double Amplitude { get; set; } = 1.0;

	protected override double EaseInCore(double t)
	{
		double amplitude = Math.Max(0.0, Amplitude);
		return Math.Pow(t, 3.0) - t * amplitude * Math.Sin(Math.PI * t);
	}
}

public sealed class ElasticEase : EasingFunctionBase
{
	public int Oscillations { get; set; } = 3;

	public double Springiness { get; set; } = 3.0;

	protected override double EaseInCore(double t)
	{
		double oscillations = Math.Max(0, Oscillations);
		double springiness = Math.Max(0.0, Springiness);
		double expo = Math.Abs(springiness) < 1e-6 ? t : (Math.Exp(springiness * t) - 1.0) / (Math.Exp(springiness) - 1.0);
		return expo * Math.Sin((Math.PI * 2.0 * oscillations + Math.PI * 0.5) * t);
	}
}

public sealed class BounceEase : EasingFunctionBase
{
	public int Bounces { get; set; } = 3;

	public double Bounciness { get; set; } = 2.0;

	protected override double EaseInCore(double t)
	{
		double bounces = Math.Max(0.0, Bounces);
		double bounciness = Bounciness;
		if (bounciness < 1.0 || Math.Abs(bounciness - 1.0) < 1e-6)
		{
			bounciness = 1.001;
		}
		double pow = Math.Pow(bounciness, bounces);
		double oneMinusBounciness = 1.0 - bounciness;
		double sumOfUnits = (1.0 - pow) / oneMinusBounciness + pow * 0.5;
		double unitAtT = t * sumOfUnits;
		double bounceAtT = Math.Floor(Math.Log(-unitAtT * (1.0 - bounciness) + 1.0) / Math.Log(bounciness));
		double start = (1.0 - Math.Pow(bounciness, bounceAtT)) / oneMinusBounciness;
		double end = (1.0 - Math.Pow(bounciness, bounceAtT + 1.0)) / oneMinusBounciness;
		double divisor = 1.0 / sumOfUnits;
		double startTime = start * divisor;
		double endTime = end * divisor;
		double midTime = (startTime + endTime) * 0.5;
		double timeRelativeToPeak = t - midTime;
		double radius = midTime - startTime;
		double amplitude = Math.Pow(1.0 / bounciness, bounces - bounceAtT);
		return -amplitude / (radius * radius) * (timeRelativeToPeak - radius) * (timeRelativeToPeak + radius);
	}
}

#endregion

#region Property paths

[TypeConverter(typeof(PropertyPathTypeConverter))]
public sealed class PropertyPath
{
	public PropertyPath(string path, params object[] pathParameters)
	{
		Path = path;
		PathParameters = pathParameters ?? Array.Empty<object>();
	}

	public PropertyPath(object parameter)
	{
		if (parameter is AvaloniaProperty property)
		{
			Path = "(0)";
			PathParameters = new object[] { property };
		}
		else
		{
			Path = parameter?.ToString() ?? string.Empty;
			PathParameters = Array.Empty<object>();
		}
	}

	public string Path { get; }

	public object[] PathParameters { get; }

	public override string ToString() => Path;
}

public sealed class PropertyPathTypeConverter : TypeConverter
{
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => new PropertyPath((string)value);
}

#endregion

#region Timelines

public abstract class Timeline
{
	public TimeSpan? BeginTime { get; set; } = TimeSpan.Zero;

	public Duration Duration { get; set; } = Duration.Automatic;

	public bool AutoReverse { get; set; }

	public RepeatBehavior RepeatBehavior { get; set; } = new RepeatBehavior(1.0);

	public FillBehavior FillBehavior { get; set; } = FillBehavior.HoldEnd;

	public double SpeedRatio { get; set; } = 1.0;

	public double AccelerationRatio { get; set; }

	public double DecelerationRatio { get; set; }

	public string? Name { get; set; }

	// Storyboard attached values (WPF: Storyboard.TargetName / TargetProperty / Target).
	public string? TargetName { get; set; }

	public PropertyPath? TargetProperty { get; set; }

	public AvaloniaObject? Target { get; set; }

	public event EventHandler? Completed;

	internal void RaiseCompleted() => Completed?.Invoke(this, EventArgs.Empty);

	internal bool HasCompletedHandlers => Completed != null;

	public Timeline Clone() => (Timeline)MemberwiseClone();

	/// <summary>Natural length of one forward pass when Duration is Automatic.</summary>
	protected internal virtual TimeSpan NaturalDuration => TimeSpan.FromSeconds(1.0);

	internal TimeSpan SimpleDuration => Duration.HasTimeSpan ? Duration.TimeSpan : NaturalDuration;

	/// <summary>Total active duration (including repeats/auto reverse) or null for forever.</summary>
	internal TimeSpan? ActiveDuration
	{
		get
		{
			if (Duration.IsForever || RepeatBehavior.IsForever)
			{
				return null;
			}
			TimeSpan iteration = SimpleDuration;
			if (AutoReverse)
			{
				iteration += iteration;
			}
			TimeSpan active = RepeatBehavior.HasDuration ? RepeatBehavior.Duration : TimeSpan.FromTicks((long)(iteration.Ticks * Math.Max(0.0, RepeatBehavior.Count)));
			double speed = SpeedRatio <= 0 ? 1.0 : SpeedRatio;
			return TimeSpan.FromTicks((long)(active.Ticks / speed));
		}
	}

	/// <summary>Converts parent-local time into normalized progress [0,1] for the current iteration.</summary>
	internal double GetProgress(TimeSpan localTime, out bool finished)
	{
		finished = false;
		TimeSpan? active = ActiveDuration;
		if (active.HasValue && localTime >= active.Value)
		{
			finished = true;
			localTime = active.Value;
		}
		double speed = SpeedRatio <= 0 ? 1.0 : SpeedRatio;
		double simple = Math.Max(1e-9, SimpleDuration.TotalMilliseconds);
		double t = localTime.TotalMilliseconds * speed;
		double iterationLength = AutoReverse ? simple * 2.0 : simple;
		double position;
		if (finished)
		{
			// End-of-active-period value: forward end, or start if auto-reversed.
			double iterations = active.HasValue ? (active.Value.TotalMilliseconds * speed) / iterationLength : 1.0;
			double fraction = iterations - Math.Floor(iterations);
			if (fraction < 1e-9)
			{
				position = AutoReverse ? 0.0 : 1.0;
			}
			else
			{
				double withinIteration = fraction * iterationLength;
				position = withinIteration <= simple ? withinIteration / simple : 2.0 - withinIteration / simple;
			}
		}
		else
		{
			double withinIteration = t % iterationLength;
			position = withinIteration <= simple ? withinIteration / simple : 2.0 - withinIteration / simple;
		}
		return ApplyAccelerationDeceleration(Math.Clamp(position, 0.0, 1.0));
	}

	private double ApplyAccelerationDeceleration(double progress)
	{
		double acc = Math.Clamp(AccelerationRatio, 0.0, 1.0);
		double dec = Math.Clamp(DecelerationRatio, 0.0, 1.0 - acc);
		if (acc <= 0 && dec <= 0)
		{
			return progress;
		}
		double maxRate = 2.0 / (2.0 - acc - dec);
		if (progress < acc)
		{
			return maxRate * progress * progress / (2.0 * acc);
		}
		if (progress <= 1.0 - dec)
		{
			return maxRate * (progress - acc / 2.0);
		}
		double remaining = 1.0 - progress;
		return 1.0 - maxRate * remaining * remaining / (2.0 * dec);
	}
}

public abstract class AnimationTimeline : Timeline
{
	public IEasingFunction? EasingFunction { get; set; }

	/// <summary>Computes the animated value. origin = value when the animation started, destination = base value.</summary>
	public abstract object? GetCurrentValue(object? origin, object? destination, double progress);

	public virtual bool IsDestinationDefault => false;

	protected double Ease(double progress) => EasingFunction?.Ease(progress) ?? progress;
}

public class DoubleAnimation : AnimationTimeline
{
	public new DoubleAnimation Clone() => (DoubleAnimation)MemberwiseClone();

	public DoubleAnimation()
	{
	}

	public DoubleAnimation(double toValue, Duration duration)
	{
		To = toValue;
		Duration = duration;
	}

	public DoubleAnimation(double toValue, Duration duration, FillBehavior fillBehavior)
	{
		To = toValue;
		Duration = duration;
		FillBehavior = fillBehavior;
	}

	public DoubleAnimation(double fromValue, double toValue, Duration duration)
	{
		From = fromValue;
		To = toValue;
		Duration = duration;
	}

	public DoubleAnimation(double fromValue, double toValue, Duration duration, FillBehavior fillBehavior)
	{
		From = fromValue;
		To = toValue;
		Duration = duration;
		FillBehavior = fillBehavior;
	}

	public double? From { get; set; }

	public double? To { get; set; }

	public double? By { get; set; }

	public override bool IsDestinationDefault => !To.HasValue && !By.HasValue;

	public override object? GetCurrentValue(object? origin, object? destination, double progress)
	{
		double originValue = ToDouble(origin);
		double from = From ?? originValue;
		double to = To ?? (By.HasValue ? from + By.Value : ToDouble(destination));
		double eased = Ease(progress);
		return from + (to - from) * eased;
	}

	internal static double ToDouble(object? value) => value switch
	{
		double d => double.IsNaN(d) ? 0.0 : d,
		float f => f,
		int i => i,
		_ => 0.0
	};
}

public class ColorAnimation : AnimationTimeline
{
	public new ColorAnimation Clone() => (ColorAnimation)MemberwiseClone();

	public ColorAnimation()
	{
	}

	public ColorAnimation(Color toValue, Duration duration)
	{
		To = toValue;
		Duration = duration;
	}

	public ColorAnimation(Color fromValue, Color toValue, Duration duration)
	{
		From = fromValue;
		To = toValue;
		Duration = duration;
	}

	public Color? From { get; set; }

	public Color? To { get; set; }

	public override bool IsDestinationDefault => !To.HasValue;

	public override object? GetCurrentValue(object? origin, object? destination, double progress)
	{
		Color originColor = origin is Color c ? c : Colors.Transparent;
		Color from = From ?? originColor;
		Color to = To ?? (destination is Color d ? d : originColor);
		double t = Ease(progress);
		return Color.FromArgb(Lerp(from.A, to.A, t), Lerp(from.R, to.R, t), Lerp(from.G, to.G, t), Lerp(from.B, to.B, t));
	}

	private static byte Lerp(byte a, byte b, double t) => (byte)Math.Clamp(Math.Round(a + (b - a) * t), 0, 255);
}

public class PointAnimation : AnimationTimeline
{
	public new PointAnimation Clone() => (PointAnimation)MemberwiseClone();

	public PointAnimation()
	{
	}

	public PointAnimation(Point toValue, Duration duration)
	{
		To = toValue;
		Duration = duration;
	}

	public PointAnimation(Point fromValue, Point toValue, Duration duration)
	{
		From = fromValue;
		To = toValue;
		Duration = duration;
	}

	public Point? From { get; set; }

	public Point? To { get; set; }

	public override bool IsDestinationDefault => !To.HasValue;

	public override object? GetCurrentValue(object? origin, object? destination, double progress)
	{
		Point originPoint = origin is Point p ? p : default;
		Point from = From ?? originPoint;
		Point to = To ?? (destination is Point d ? d : originPoint);
		double t = Ease(progress);
		return new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
	}
}

public class ThicknessAnimation : AnimationTimeline
{
	public new ThicknessAnimation Clone() => (ThicknessAnimation)MemberwiseClone();

	public ThicknessAnimation()
	{
	}

	public ThicknessAnimation(Thickness toValue, Duration duration)
	{
		To = toValue;
		Duration = duration;
	}

	public ThicknessAnimation(Thickness fromValue, Thickness toValue, Duration duration)
	{
		From = fromValue;
		To = toValue;
		Duration = duration;
	}

	public Thickness? From { get; set; }

	public Thickness? To { get; set; }

	public override bool IsDestinationDefault => !To.HasValue;

	public override object? GetCurrentValue(object? origin, object? destination, double progress)
	{
		Thickness originValue = origin is Thickness th ? th : default;
		Thickness from = From ?? originValue;
		Thickness to = To ?? (destination is Thickness d ? d : originValue);
		double t = Ease(progress);
		return new Thickness(from.Left + (to.Left - from.Left) * t, from.Top + (to.Top - from.Top) * t, from.Right + (to.Right - from.Right) * t, from.Bottom + (to.Bottom - from.Bottom) * t);
	}
}

[TypeConverter(typeof(KeyTimeTypeConverter))]
public readonly struct KeyTime
{
	private readonly TimeSpan _timeSpan;

	private readonly double _percent;

	private readonly int _kind; // 0 timespan, 1 percent, 2 uniform, 3 paced

	private KeyTime(TimeSpan timeSpan, double percent, int kind)
	{
		_timeSpan = timeSpan;
		_percent = percent;
		_kind = kind;
	}

	public static KeyTime FromTimeSpan(TimeSpan timeSpan) => new KeyTime(timeSpan, 0, 0);

	public static KeyTime FromPercent(double percent) => new KeyTime(TimeSpan.Zero, percent, 1);

	public static KeyTime Uniform => new KeyTime(TimeSpan.Zero, 0, 2);

	public static KeyTime Paced => new KeyTime(TimeSpan.Zero, 0, 3);

	public static implicit operator KeyTime(TimeSpan timeSpan) => FromTimeSpan(timeSpan);

	public bool IsTimeSpan => _kind == 0;

	public bool IsPercent => _kind == 1;

	public TimeSpan TimeSpan => _timeSpan;

	public double Percent => _percent;

	public static KeyTime Parse(string text)
	{
		string trimmed = text.Trim();
		if (trimmed.Equals("Uniform", StringComparison.OrdinalIgnoreCase))
		{
			return Uniform;
		}
		if (trimmed.Equals("Paced", StringComparison.OrdinalIgnoreCase))
		{
			return Paced;
		}
		if (trimmed.EndsWith('%'))
		{
			return FromPercent(double.Parse(trimmed[..^1], CultureInfo.InvariantCulture) / 100.0);
		}
		return FromTimeSpan(TimeSpan.Parse(trimmed, CultureInfo.InvariantCulture));
	}
}

public sealed class KeyTimeTypeConverter : TypeConverter
{
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => KeyTime.Parse((string)value);
}

public abstract class DoubleKeyFrame
{
	public double Value { get; set; }

	public KeyTime KeyTime { get; set; } = KeyTime.Uniform;

	internal abstract double Interpolate(double baseValue, double progress);
}

public sealed class LinearDoubleKeyFrame : DoubleKeyFrame
{
	public LinearDoubleKeyFrame()
	{
	}

	public LinearDoubleKeyFrame(double value, KeyTime keyTime)
	{
		Value = value;
		KeyTime = keyTime;
	}

	internal override double Interpolate(double baseValue, double progress) => baseValue + (Value - baseValue) * progress;
}

public sealed class DiscreteDoubleKeyFrame : DoubleKeyFrame
{
	public DiscreteDoubleKeyFrame()
	{
	}

	public DiscreteDoubleKeyFrame(double value, KeyTime keyTime)
	{
		Value = value;
		KeyTime = keyTime;
	}

	internal override double Interpolate(double baseValue, double progress) => progress < 1.0 ? baseValue : Value;
}

public sealed class EasingDoubleKeyFrame : DoubleKeyFrame
{
	public EasingDoubleKeyFrame()
	{
	}

	public EasingDoubleKeyFrame(double value, KeyTime keyTime, IEasingFunction? easingFunction = null)
	{
		Value = value;
		KeyTime = keyTime;
		EasingFunction = easingFunction;
	}

	public IEasingFunction? EasingFunction { get; set; }

	internal override double Interpolate(double baseValue, double progress)
	{
		double eased = EasingFunction?.Ease(progress) ?? progress;
		return baseValue + (Value - baseValue) * eased;
	}
}

public sealed class SplineDoubleKeyFrame : DoubleKeyFrame
{
	public string? KeySpline { get; set; }

	internal override double Interpolate(double baseValue, double progress) => baseValue + (Value - baseValue) * progress;
}

public class DoubleAnimationUsingKeyFrames : AnimationTimeline
{
	public new DoubleAnimationUsingKeyFrames Clone() => (DoubleAnimationUsingKeyFrames)MemberwiseClone();

	[Content]
	public List<DoubleKeyFrame> KeyFrames { get; } = new List<DoubleKeyFrame>();

	protected internal override TimeSpan NaturalDuration
	{
		get
		{
			TimeSpan max = TimeSpan.Zero;
			foreach (DoubleKeyFrame frame in KeyFrames)
			{
				if (frame.KeyTime.IsTimeSpan && frame.KeyTime.TimeSpan > max)
				{
					max = frame.KeyTime.TimeSpan;
				}
			}
			return max > TimeSpan.Zero ? max : TimeSpan.FromSeconds(1.0);
		}
	}

	public override object? GetCurrentValue(object? origin, object? destination, double progress)
	{
		double baseValue = DoubleAnimation.ToDouble(origin);
		if (KeyFrames.Count == 0)
		{
			return baseValue;
		}
		double total = Math.Max(1e-9, SimpleDuration.TotalMilliseconds);
		double now = progress * total;
		double previousTime = 0.0;
		double previousValue = baseValue;
		for (int i = 0; i < KeyFrames.Count; i++)
		{
			DoubleKeyFrame frame = KeyFrames[i];
			double frameTime = ResolveKeyTime(frame.KeyTime, i, total);
			if (now <= frameTime)
			{
				double span = frameTime - previousTime;
				double local = span <= 1e-9 ? 1.0 : (now - previousTime) / span;
				return frame.Interpolate(previousValue, Math.Clamp(local, 0.0, 1.0));
			}
			previousTime = frameTime;
			previousValue = frame.Value;
		}
		return previousValue;
	}

	private double ResolveKeyTime(KeyTime keyTime, int index, double total)
	{
		if (keyTime.IsTimeSpan)
		{
			return keyTime.TimeSpan.TotalMilliseconds;
		}
		if (keyTime.IsPercent)
		{
			return keyTime.Percent * total;
		}
		return total * (index + 1) / KeyFrames.Count;
	}
}

public class DiscreteObjectKeyFrame
{
	public DiscreteObjectKeyFrame()
	{
	}

	public DiscreteObjectKeyFrame(object? value, KeyTime keyTime)
	{
		Value = value;
		KeyTime = keyTime;
	}

	public object? Value { get; set; }

	public KeyTime KeyTime { get; set; } = KeyTime.FromTimeSpan(TimeSpan.Zero);
}

public class ObjectAnimationUsingKeyFrames : AnimationTimeline
{
	public new ObjectAnimationUsingKeyFrames Clone() => (ObjectAnimationUsingKeyFrames)MemberwiseClone();

	[Content]
	public List<DiscreteObjectKeyFrame> KeyFrames { get; } = new List<DiscreteObjectKeyFrame>();

	protected internal override TimeSpan NaturalDuration
	{
		get
		{
			TimeSpan max = TimeSpan.Zero;
			foreach (DiscreteObjectKeyFrame frame in KeyFrames)
			{
				if (frame.KeyTime.IsTimeSpan && frame.KeyTime.TimeSpan > max)
				{
					max = frame.KeyTime.TimeSpan;
				}
			}
			return max;
		}
	}

	public override object? GetCurrentValue(object? origin, object? destination, double progress)
	{
		double total = SimpleDuration.TotalMilliseconds;
		double now = progress * total;
		object? value = origin;
		foreach (DiscreteObjectKeyFrame frame in KeyFrames)
		{
			double frameTime = frame.KeyTime.IsPercent ? frame.KeyTime.Percent * total : frame.KeyTime.TimeSpan.TotalMilliseconds;
			if (now + 1e-6 >= frameTime)
			{
				value = frame.Value;
			}
		}
		return value;
	}
}

public class ParallelTimeline : Timeline
{
	[Content]
	public List<Timeline> Children { get; } = new List<Timeline>();

	protected internal override TimeSpan NaturalDuration
	{
		get
		{
			TimeSpan max = TimeSpan.Zero;
			foreach (Timeline child in Children)
			{
				TimeSpan? active = child.ActiveDuration;
				if (!active.HasValue)
				{
					continue;
				}
				TimeSpan end = (child.BeginTime ?? TimeSpan.Zero) + active.Value;
				if (end > max)
				{
					max = end;
				}
			}
			return max;
		}
	}
}

#endregion

#region Runtime

/// <summary>Frame source shared by animations and CompositionTarget.Rendering.</summary>
public static class AnimationTicker
{
	private static readonly Stopwatch Clock = Stopwatch.StartNew();

	private static readonly List<Action<TimeSpan>> Callbacks = new List<Action<TimeSpan>>();

	private static DispatcherTimer? _timer;

	private static bool _frameRequested;

	public static TopLevel? FrameSource { get; set; }

	public static TimeSpan Now => Clock.Elapsed;

	public static void Subscribe(Action<TimeSpan> callback)
	{
		Callbacks.Add(callback);
		EnsureRunning();
	}

	public static void Unsubscribe(Action<TimeSpan> callback)
	{
		Callbacks.Remove(callback);
	}

	private static void EnsureRunning()
	{
		if (FrameSource != null)
		{
			if (!_frameRequested)
			{
				_frameRequested = true;
				FrameSource.RequestAnimationFrame(OnAnimationFrame);
			}
			_timer?.Stop();
			return;
		}
		if (_timer == null)
		{
			_timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
		}
		if (!_timer.IsEnabled)
		{
			_timer.Start();
		}
	}

	private static void OnAnimationFrame(TimeSpan _)
	{
		_frameRequested = false;
		Tick();
	}

	private static void Tick()
	{
		TimeSpan now = Clock.Elapsed;
		if (Callbacks.Count == 0)
		{
			_timer?.Stop();
			return;
		}
		Action<TimeSpan>[] snapshot = Callbacks.ToArray();
		foreach (Action<TimeSpan> callback in snapshot)
		{
			try
			{
				callback(now);
			}
			catch (Exception exception)
			{
				App.LogException(exception, "AnimationTicker");
			}
		}
		if (Callbacks.Count > 0)
		{
			EnsureRunning();
		}
		else
		{
			_timer?.Stop();
		}
	}
}

public sealed class RenderingEventArgs : EventArgs
{
	public RenderingEventArgs(TimeSpan renderingTime)
	{
		RenderingTime = renderingTime;
	}

	public TimeSpan RenderingTime { get; }
}

/// <summary>WPF CompositionTarget.Rendering emulation (raised once per frame while subscribed).</summary>
public static class CompositionTarget
{
	private static EventHandler? _rendering;

	private static readonly Action<TimeSpan> Callback = time => _rendering?.Invoke(null, new RenderingEventArgs(time));

	public static event EventHandler Rendering
	{
		add
		{
			bool wasEmpty = _rendering == null;
			_rendering += value;
			if (wasEmpty)
			{
				AnimationTicker.Subscribe(Callback);
			}
		}
		remove
		{
			_rendering -= value;
			if (_rendering == null)
			{
				AnimationTicker.Unsubscribe(Callback);
			}
		}
	}
}

/// <summary>Pushes animated values into a property at Animation priority.</summary>
internal sealed class AnimationSlot : IObservable<object?>
{
	private IObserver<object?>? _observer;

	private IDisposable? _binding;

	private object? _pendingValue = AvaloniaProperty.UnsetValue;

	public AnimationSlot(AvaloniaObject target, AvaloniaProperty property)
	{
		Target = target;
		Property = property;
	}

	public AvaloniaObject Target { get; }

	public AvaloniaProperty Property { get; }

	public ActiveAnimation? Current { get; set; }

	public bool IsBound => _binding != null;

	public void Push(object? value)
	{
		object? coerced = AnimationValueCoercion.Coerce(value, Property.PropertyType);
		if (_binding == null)
		{
			_pendingValue = coerced;
			_binding = Target.Bind(Property, this, BindingPriority.Animation);
		}
		else
		{
			_observer?.OnNext(coerced);
		}
	}

	public void Release()
	{
		IDisposable? binding = _binding;
		_binding = null;
		_observer = null;
		binding?.Dispose();
	}

	public IDisposable Subscribe(IObserver<object?> observer)
	{
		_observer = observer;
		if (!ReferenceEquals(_pendingValue, AvaloniaProperty.UnsetValue))
		{
			object? value = _pendingValue;
			_pendingValue = AvaloniaProperty.UnsetValue;
			observer.OnNext(value);
		}
		return new Unsubscriber(this, observer);
	}

	private sealed class Unsubscriber : IDisposable
	{
		private readonly AnimationSlot _slot;

		private readonly IObserver<object?> _observer;

		public Unsubscriber(AnimationSlot slot, IObserver<object?> observer)
		{
			_slot = slot;
			_observer = observer;
		}

		public void Dispose()
		{
			if (ReferenceEquals(_slot._observer, _observer))
			{
				_slot._observer = null;
			}
		}
	}
}

internal static class AnimationValueCoercion
{
	public static object? Coerce(object? value, Type propertyType)
	{
		if (value == null)
		{
			return null;
		}
		if (propertyType.IsInstanceOfType(value))
		{
			return value;
		}
		Type underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
		if (underlying == typeof(double) && value is IConvertible)
		{
			return Convert.ToDouble(value, CultureInfo.InvariantCulture);
		}
		if (underlying == typeof(float) && value is IConvertible)
		{
			return Convert.ToSingle(value, CultureInfo.InvariantCulture);
		}
		if (underlying == typeof(int) && value is IConvertible)
		{
			return Convert.ToInt32(value, CultureInfo.InvariantCulture);
		}
		if (underlying == typeof(bool) && value is Visibility visibility)
		{
			return visibility == Visibility.Visible;
		}
		return ValueConversion.ConvertTo(value, propertyType);
	}
}

/// <summary>A running animation (one timeline applied to one property).</summary>
internal sealed class ActiveAnimation
{
	private readonly AnimationSlot _slot;

	private readonly AnimationTimeline _timeline;

	private readonly object? _origin;

	private readonly Action<ActiveAnimation>? _onCompleted;

	private TimeSpan _startedAt;

	private TimeSpan _pausedAt;

	private bool _paused;

	private bool _finished;

	private bool _stopped;

	public ActiveAnimation(AnimationSlot slot, AnimationTimeline timeline, object? origin, Action<ActiveAnimation>? onCompleted, TimeSpan offset)
	{
		_slot = slot;
		_timeline = timeline;
		_origin = origin;
		_onCompleted = onCompleted;
		_startedAt = AnimationTicker.Now - offset;
	}

	public bool IsFinished => _finished;

	public AnimationTimeline Timeline => _timeline;

	public void Start()
	{
		Evaluate(AnimationTicker.Now);
		if (!_finished)
		{
			AnimationTicker.Subscribe(OnFrame);
		}
	}

	public void Pause()
	{
		if (_paused || _finished || _stopped)
		{
			return;
		}
		_paused = true;
		_pausedAt = AnimationTicker.Now;
		AnimationTicker.Unsubscribe(OnFrame);
	}

	public void Resume()
	{
		if (!_paused || _stopped)
		{
			return;
		}
		_paused = false;
		_startedAt += AnimationTicker.Now - _pausedAt;
		if (!_finished)
		{
			AnimationTicker.Subscribe(OnFrame);
		}
	}

	public void Seek(TimeSpan offset)
	{
		_startedAt = AnimationTicker.Now - offset;
		_finished = false;
		Evaluate(AnimationTicker.Now);
	}

	/// <summary>Stops ticking. When remove is true the animated value is released (base value returns).</summary>
	public void Stop(bool remove)
	{
		_stopped = true;
		AnimationTicker.Unsubscribe(OnFrame);
		if (remove && ReferenceEquals(_slot.Current, this))
		{
			_slot.Current = null;
			_slot.Release();
		}
	}

	private void OnFrame(TimeSpan now)
	{
		if (_stopped || _paused)
		{
			return;
		}
		Evaluate(now);
	}

	private void Evaluate(TimeSpan now)
	{
		if (!ReferenceEquals(_slot.Current, this))
		{
			Stop(remove: false);
			return;
		}
		TimeSpan local = now - _startedAt - (_timeline.BeginTime ?? TimeSpan.Zero);
		if (local < TimeSpan.Zero)
		{
			// Not started yet: hold the snapshot value (WPF SnapshotAndReplace semantics).
			_slot.Push(_origin);
			return;
		}
		double progress = _timeline.GetProgress(local, out bool finished);
		object? destination = _timeline.IsDestinationDefault ? _slot.Target.GetBaseValueSafe(_slot.Property) : null;
		object? value = _timeline.GetCurrentValue(_origin, destination, progress);
		_slot.Push(value);
		if (finished && !_finished)
		{
			_finished = true;
			AnimationTicker.Unsubscribe(OnFrame);
			if (_timeline.FillBehavior == FillBehavior.Stop)
			{
				_slot.Current = null;
				_slot.Release();
			}
			_onCompleted?.Invoke(this);
		}
	}
}

public static class AnimationExtensions
{
	private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, AnimationSlot>> Slots = new ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, AnimationSlot>>();

	internal static AnimationSlot GetSlot(AvaloniaObject target, AvaloniaProperty property)
	{
		Dictionary<AvaloniaProperty, AnimationSlot> slots = Slots.GetOrCreateValue(target);
		if (!slots.TryGetValue(property, out AnimationSlot? slot))
		{
			slot = new AnimationSlot(target, property);
			slots[property] = slot;
		}
		return slot;
	}

	internal static object? GetBaseValueSafe(this AvaloniaObject target, AvaloniaProperty property)
	{
		try
		{
			object? value = target.GetBaseValue(property);
			if (value is Optional<object?> optional)
			{
				return optional.HasValue ? optional.Value : property.GetMetadata(target.GetType()).DefaultValueObject();
			}
			return value;
		}
		catch
		{
			return target.GetValue(property);
		}
	}

	private static object? DefaultValueObject(this AvaloniaPropertyMetadata metadata)
	{
		return metadata.GetType().GetProperty("DefaultValue")?.GetValue(metadata);
	}

	/// <summary>WPF UIElement/Animatable.BeginAnimation.</summary>
	public static void BeginAnimation(this AvaloniaObject target, AvaloniaProperty property, AnimationTimeline? animation)
	{
		BeginAnimation(target, property, animation, HandoffBehavior.SnapshotAndReplace);
	}

	public static void BeginAnimation(this AvaloniaObject target, AvaloniaProperty property, AnimationTimeline? animation, HandoffBehavior handoffBehavior)
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(() => BeginAnimation(target, property, animation, handoffBehavior));
			return;
		}
		AnimationSlot slot = GetSlot(target, property);
		if (animation == null)
		{
			ActiveAnimation? current = slot.Current;
			slot.Current = null;
			current?.Stop(remove: false);
			slot.Release();
			return;
		}
		Start(slot, animation, null, TimeSpan.Zero);
	}

	internal static ActiveAnimation Start(AnimationSlot slot, AnimationTimeline animation, Action<ActiveAnimation>? onCompleted, TimeSpan offset)
	{
		object? origin = slot.Target.GetValue(slot.Property);
		ActiveAnimation? previous = slot.Current;
		ActiveAnimation active = new ActiveAnimation(slot, animation, origin, active2 =>
		{
			onCompleted?.Invoke(active2);
			if (onCompleted == null)
			{
				animation.RaiseCompleted();
			}
		}, offset);
		slot.Current = active;
		previous?.Stop(remove: false);
		active.Start();
		return active;
	}

	/// <summary>Returns the current animated value or the base value (WPF GetAnimationBaseValue).</summary>
	public static object? GetAnimationBaseValue(this AvaloniaObject target, AvaloniaProperty property)
	{
		return target.GetBaseValueSafe(property);
	}

	public static bool HasAnimatedProperties(this AvaloniaObject target)
	{
		if (!Slots.TryGetValue(target, out Dictionary<AvaloniaProperty, AnimationSlot>? slots))
		{
			return false;
		}
		foreach (AnimationSlot slot in slots.Values)
		{
			if (slot.IsBound)
			{
				return true;
			}
		}
		return false;
	}
}

/// <summary>WPF Storyboard.</summary>
public class Storyboard : ParallelTimeline
{
	private sealed class Run
	{
		public readonly List<(ActiveAnimation Animation, AnimationSlot Slot)> Animations = new List<(ActiveAnimation, AnimationSlot)>();

		public int Pending;

		public bool Stopped;
	}

	private readonly ConditionalWeakTable<object, Run> _runs = new ConditionalWeakTable<object, Run>();

	private static readonly object DefaultKey = new object();

	public static void SetTargetName(Timeline element, string? name) => element.TargetName = name;

	public static string? GetTargetName(Timeline element) => element.TargetName;

	public static void SetTargetProperty(Timeline element, PropertyPath? path) => element.TargetProperty = path;

	public static PropertyPath? GetTargetProperty(Timeline element) => element.TargetProperty;

	public static void SetTarget(Timeline element, AvaloniaObject? target) => element.Target = target;

	public static AvaloniaObject? GetTarget(Timeline element) => element.Target;

	public void Begin()
	{
		BeginCore(null, null, null);
	}

	public void Begin(StyledElement containingObject)
	{
		BeginCore(containingObject, containingObject, null);
	}

	public void Begin(StyledElement containingObject, bool isControllable)
	{
		BeginCore(containingObject, containingObject, null);
	}

	public void Begin(StyledElement containingObject, HandoffBehavior handoffBehavior)
	{
		BeginCore(containingObject, containingObject, null);
	}

	public void Begin(StyledElement containingObject, HandoffBehavior handoffBehavior, bool isControllable)
	{
		BeginCore(containingObject, containingObject, null);
	}

	public void Begin(StyledElement containingObject, StyledElement? templatedParent, bool isControllable)
	{
		BeginCore(containingObject, containingObject, null);
	}

	/// <summary>Begin with an explicit name resolver (used by the trigger engine for template parts).</summary>
	internal void BeginCore(object? key, StyledElement? defaultTarget, Func<string, AvaloniaObject?>? nameResolver)
	{
		object runKey = key ?? DefaultKey;
		StopCore(runKey, remove: false);
		Run run = new Run();
		_runs.AddOrUpdate(runKey, run);
		List<(AnimationTimeline Timeline, AvaloniaObject Target, AvaloniaProperty Property, TimeSpan Offset)> plan = new List<(AnimationTimeline, AvaloniaObject, AvaloniaProperty, TimeSpan)>();
		Collect(this, TimeSpan.Zero, defaultTarget, nameResolver, plan, this.TargetName, this.Target);
		run.Pending = plan.Count;
		if (plan.Count == 0)
		{
			RaiseCompleted();
			return;
		}
		foreach ((AnimationTimeline timeline, AvaloniaObject target, AvaloniaProperty property, TimeSpan offset) in plan)
		{
			AnimationSlot slot = AnimationExtensions.GetSlot(target, property);
			AnimationTimeline effective = timeline;
			if (offset != TimeSpan.Zero)
			{
				effective = (AnimationTimeline)timeline.Clone();
				effective.BeginTime = (timeline.BeginTime ?? TimeSpan.Zero) + offset;
			}
			ActiveAnimation active = AnimationExtensions.Start(slot, effective, completed =>
			{
				timeline.RaiseCompleted();
				if (!run.Stopped && --run.Pending == 0)
				{
					RaiseCompleted();
				}
			}, TimeSpan.Zero);
			run.Animations.Add((active, slot));
		}
	}

	private static void Collect(ParallelTimeline parent, TimeSpan offset, StyledElement? defaultTarget, Func<string, AvaloniaObject?>? nameResolver, List<(AnimationTimeline, AvaloniaObject, AvaloniaProperty, TimeSpan)> plan, string? inheritedName, AvaloniaObject? inheritedTarget)
	{
		foreach (Timeline child in parent.Children)
		{
			string? targetName = child.TargetName ?? inheritedName;
			AvaloniaObject? explicitTarget = child.Target ?? inheritedTarget;
			if (child is ParallelTimeline nested)
			{
				Collect(nested, offset + (nested.BeginTime ?? TimeSpan.Zero), defaultTarget, nameResolver, plan, targetName, explicitTarget);
				continue;
			}
			if (child is not AnimationTimeline animation || animation.TargetProperty == null)
			{
				continue;
			}
			AvaloniaObject? root = explicitTarget;
			if (root == null && !string.IsNullOrEmpty(targetName))
			{
				root = nameResolver?.Invoke(targetName) ?? NameLookup.Find(defaultTarget, targetName);
			}
			root ??= defaultTarget;
			if (root == null)
			{
				continue;
			}
			if (PropertyPathResolver.TryResolve(root, animation.TargetProperty, out AvaloniaObject? owner, out AvaloniaProperty? property) && owner != null && property != null)
			{
				plan.Add((animation, owner, property, offset));
			}
		}
	}

	public void Stop()
	{
		StopCore(DefaultKey, remove: true);
	}

	public void Stop(StyledElement containingObject)
	{
		StopCore(containingObject, remove: true);
	}

	internal void StopCore(object? key, bool remove)
	{
		object runKey = key ?? DefaultKey;
		if (!_runs.TryGetValue(runKey, out Run? run))
		{
			return;
		}
		run.Stopped = true;
		foreach ((ActiveAnimation animation, AnimationSlot _) in run.Animations)
		{
			animation.Stop(remove);
		}
		_runs.Remove(runKey);
	}

	public void Remove()
	{
		StopCore(DefaultKey, remove: true);
	}

	public void Remove(StyledElement containingObject)
	{
		StopCore(containingObject, remove: true);
	}

	public void Pause()
	{
		Pause(DefaultKey);
	}

	public void Pause(object containingObject)
	{
		if (_runs.TryGetValue(containingObject, out Run? run))
		{
			foreach ((ActiveAnimation animation, AnimationSlot _) in run.Animations)
			{
				animation.Pause();
			}
		}
	}

	public void Resume()
	{
		Resume(DefaultKey);
	}

	public void Resume(object containingObject)
	{
		if (_runs.TryGetValue(containingObject, out Run? run))
		{
			foreach ((ActiveAnimation animation, AnimationSlot _) in run.Animations)
			{
				animation.Resume();
			}
		}
	}

	public void Seek(TimeSpan offset)
	{
		Seek(DefaultKey, offset);
	}

	public void Seek(object containingObject, TimeSpan offset)
	{
		if (_runs.TryGetValue(containingObject, out Run? run))
		{
			foreach ((ActiveAnimation animation, AnimationSlot _) in run.Animations)
			{
				animation.Seek(offset);
			}
		}
	}

	public void SkipToFill()
	{
		Seek(DefaultKey, TimeSpan.FromDays(1));
	}

	public void SkipToFill(object containingObject)
	{
		Seek(containingObject, TimeSpan.FromDays(1));
	}

	public new Storyboard Clone() => (Storyboard)MemberwiseClone();
}

#endregion
