// WPF Trigger / DataTrigger / MultiTrigger / MultiDataTrigger emulation for Avalonia.
//
// Triggers are attached through one of the Wpf.*Triggers attached properties:
//  - TemplateTriggers  : set on a ControlTemplate root. Context = templated parent, TargetName = template part.
//  - ThemeTriggers     : set from a ControlTheme (WPF keyed Style) through a <Template> setter. Context = styled element.
//  - StyleTriggers     : set from a selector Style (WPF implicit Style). Context = styled element.
//  - DataTemplateTriggers : set on a DataTemplate root. Context = root, TargetName = element inside the data template.
//
// Setter values are applied with BindingPriority.StyleTrigger (over style/template values, under local values),
// and a per-host arbiter makes the last active trigger win, exactly like WPF.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;
using Avalonia.Threading;

namespace XboxMetroLauncher.Compat;

/// <summary>
/// Holds the triggers of a template/style. It derives from Control only so it can be produced by an Avalonia
/// &lt;Template&gt; inside a style Setter (one instance per styled element); it is never added to the visual tree.
/// </summary>
public sealed class TriggerCollection : Control
{
	[Content]
	public List<TriggerBase> Triggers { get; } = new List<TriggerBase>();

	public int Count => Triggers.Count;

	public TriggerBase this[int index] => Triggers[index];
}

public sealed class Setter
{
	public string? Property { get; set; }

	public string? TargetName { get; set; }

	[Content]
	[AssignBinding]
	public object? Value { get; set; }
}

public abstract class TriggerAction
{
}

public sealed class BeginStoryboard : TriggerAction
{
	public string? Name { get; set; }

	public HandoffBehavior HandoffBehavior { get; set; }

	[Content]
	public Storyboard? Storyboard { get; set; }
}

public sealed class StopStoryboard : TriggerAction
{
	public string? BeginStoryboardName { get; set; }
}

public sealed class RemoveStoryboard : TriggerAction
{
	public string? BeginStoryboardName { get; set; }
}

public sealed class PauseStoryboard : TriggerAction
{
	public string? BeginStoryboardName { get; set; }
}

public sealed class ResumeStoryboard : TriggerAction
{
	public string? BeginStoryboardName { get; set; }
}

public abstract class TriggerBase
{
	[Content]
	public List<Setter> Setters { get; } = new List<Setter>();

	public List<TriggerAction> EnterActions { get; } = new List<TriggerAction>();

	public List<TriggerAction> ExitActions { get; } = new List<TriggerAction>();
}

public sealed class Trigger : TriggerBase
{
	public string? Property { get; set; }

	public string? SourceName { get; set; }

	public object? Value { get; set; }
}

public sealed class DataTrigger : TriggerBase
{
	[AssignBinding]
	public IBinding? Binding { get; set; }

	public object? Value { get; set; }
}

public sealed class Condition
{
	public string? Property { get; set; }

	public string? SourceName { get; set; }

	[AssignBinding]
	public IBinding? Binding { get; set; }

	public object? Value { get; set; }
}

public sealed class MultiTrigger : TriggerBase
{
	public List<Condition> Conditions { get; } = new List<Condition>();
}

public sealed class MultiDataTrigger : TriggerBase
{
	public List<Condition> Conditions { get; } = new List<Condition>();
}

/// <summary>Markup extension used by converted setters for {DynamicResource Key}.</summary>
public sealed class DynRes : MarkupExtension
{
	public DynRes()
	{
	}

	public DynRes(object key)
	{
		ResourceKey = key;
	}

	public object? ResourceKey { get; set; }

	public override object ProvideValue(IServiceProvider serviceProvider) => new DynamicResourceReference(ResourceKey ?? string.Empty);
}

public sealed class DynamicResourceReference
{
	public DynamicResourceReference(object key)
	{
		Key = key;
	}

	public object Key { get; }
}

public static partial class WpfTriggers
{
	public static readonly AttachedProperty<TriggerCollection?> TemplateTriggersProperty =
		AvaloniaProperty.RegisterAttached<StyledElement, TriggerCollection?>("TemplateTriggers", typeof(WpfTriggers));

	public static readonly AttachedProperty<TriggerCollection?> ThemeTriggersProperty =
		AvaloniaProperty.RegisterAttached<StyledElement, TriggerCollection?>("ThemeTriggers", typeof(WpfTriggers));

	public static readonly AttachedProperty<TriggerCollection?> ThemeTriggers2Property =
		AvaloniaProperty.RegisterAttached<StyledElement, TriggerCollection?>("ThemeTriggers2", typeof(WpfTriggers));

	public static readonly AttachedProperty<TriggerCollection?> ThemeTriggers3Property =
		AvaloniaProperty.RegisterAttached<StyledElement, TriggerCollection?>("ThemeTriggers3", typeof(WpfTriggers));

	public static readonly AttachedProperty<TriggerCollection?> StyleTriggersProperty =
		AvaloniaProperty.RegisterAttached<StyledElement, TriggerCollection?>("StyleTriggers", typeof(WpfTriggers));

	public static readonly AttachedProperty<TriggerCollection?> DataTemplateTriggersProperty =
		AvaloniaProperty.RegisterAttached<StyledElement, TriggerCollection?>("DataTemplateTriggers", typeof(WpfTriggers));

	private static readonly AttachedProperty<Dictionary<AvaloniaProperty, TriggerHost>?> HostsProperty =
		AvaloniaProperty.RegisterAttached<StyledElement, Dictionary<AvaloniaProperty, TriggerHost>?>("TriggerHosts", typeof(WpfTriggers));

	static WpfTriggers()
	{
		TemplateTriggersProperty.Changed.AddClassHandler<StyledElement>((e, a) => OnTriggersChanged(e, a, TriggerScope.Template));
		ThemeTriggersProperty.Changed.AddClassHandler<StyledElement>((e, a) => OnTriggersChanged(e, a, TriggerScope.Element));
		ThemeTriggers2Property.Changed.AddClassHandler<StyledElement>((e, a) => OnTriggersChanged(e, a, TriggerScope.Element));
		ThemeTriggers3Property.Changed.AddClassHandler<StyledElement>((e, a) => OnTriggersChanged(e, a, TriggerScope.Element));
		StyleTriggersProperty.Changed.AddClassHandler<StyledElement>((e, a) => OnTriggersChanged(e, a, TriggerScope.Element));
		DataTemplateTriggersProperty.Changed.AddClassHandler<StyledElement>((e, a) => OnTriggersChanged(e, a, TriggerScope.DataTemplate));
	}

	public static void SetTemplateTriggers(StyledElement element, TriggerCollection? value) => element.SetValue(TemplateTriggersProperty, value);

	public static TriggerCollection? GetTemplateTriggers(StyledElement element) => element.GetValue(TemplateTriggersProperty);

	public static void SetThemeTriggers(StyledElement element, TriggerCollection? value) => element.SetValue(ThemeTriggersProperty, value);

	public static TriggerCollection? GetThemeTriggers(StyledElement element) => element.GetValue(ThemeTriggersProperty);

	public static void SetThemeTriggers2(StyledElement element, TriggerCollection? value) => element.SetValue(ThemeTriggers2Property, value);

	public static TriggerCollection? GetThemeTriggers2(StyledElement element) => element.GetValue(ThemeTriggers2Property);

	public static void SetThemeTriggers3(StyledElement element, TriggerCollection? value) => element.SetValue(ThemeTriggers3Property, value);

	public static TriggerCollection? GetThemeTriggers3(StyledElement element) => element.GetValue(ThemeTriggers3Property);

	public static void SetStyleTriggers(StyledElement element, TriggerCollection? value) => element.SetValue(StyleTriggersProperty, value);

	public static TriggerCollection? GetStyleTriggers(StyledElement element) => element.GetValue(StyleTriggersProperty);

	public static void SetDataTemplateTriggers(StyledElement element, TriggerCollection? value) => element.SetValue(DataTemplateTriggersProperty, value);

	public static TriggerCollection? GetDataTemplateTriggers(StyledElement element) => element.GetValue(DataTemplateTriggersProperty);

	internal enum TriggerScope
	{
		Template,
		Element,
		DataTemplate
	}

	private static void OnTriggersChanged(StyledElement element, AvaloniaPropertyChangedEventArgs args, TriggerScope scope)
	{
		if (TriggerHost.DebugEnabled)
		{
			Console.Error.WriteLine($"[trigger] set {args.Property.Name} on {element.GetType().Name}#{element.Name} count={(args.NewValue as TriggerCollection)?.Count}");
		}
		Dictionary<AvaloniaProperty, TriggerHost> hosts = element.GetValue(HostsProperty) ?? new Dictionary<AvaloniaProperty, TriggerHost>();
		element.SetValue(HostsProperty, hosts);
		if (hosts.TryGetValue(args.Property, out TriggerHost? existing))
		{
			existing.Dispose();
			hosts.Remove(args.Property);
		}
		// XAML assigns the collection before populating it, so the host reads the triggers when it activates.
		if (args.NewValue is TriggerCollection collection)
		{
			TriggerHost host = new TriggerHost(element, collection, scope);
			hosts[args.Property] = host;
			host.Initialize();
		}
	}
}

/// <summary>Arbitrates values set by several triggers on the same property (highest order wins).</summary>
internal sealed class SetterArbiter
{
	private sealed class Entry
	{
		public readonly SortedDictionary<int, object?> Values = new SortedDictionary<int, object?>();

		public IDisposable? Applied;

		public object? AppliedValue = AvaloniaProperty.UnsetValue;
	}

	private readonly Dictionary<(AvaloniaObject, AvaloniaProperty), Entry> _entries = new Dictionary<(AvaloniaObject, AvaloniaProperty), Entry>();

	private readonly StyledElement _resourceAnchor;

	public SetterArbiter(StyledElement resourceAnchor)
	{
		_resourceAnchor = resourceAnchor;
	}

	public void Set(AvaloniaObject target, AvaloniaProperty property, int order, object? value)
	{
		(AvaloniaObject, AvaloniaProperty) key = (target, property);
		if (!_entries.TryGetValue(key, out Entry? entry))
		{
			entry = new Entry();
			_entries[key] = entry;
		}
		entry.Values[order] = value;
		Reapply(target, property, entry);
	}

	public void Clear(AvaloniaObject target, AvaloniaProperty property, int order)
	{
		(AvaloniaObject, AvaloniaProperty) key = (target, property);
		if (!_entries.TryGetValue(key, out Entry? entry))
		{
			return;
		}
		if (entry.Values.Remove(order))
		{
			Reapply(target, property, entry);
		}
	}

	public void ClearAll()
	{
		foreach (Entry entry in _entries.Values)
		{
			entry.Applied?.Dispose();
			entry.Applied = null;
			entry.Values.Clear();
		}
		_entries.Clear();
	}

	private void Reapply(AvaloniaObject target, AvaloniaProperty property, Entry entry)
	{
		if (entry.Values.Count == 0)
		{
			entry.Applied?.Dispose();
			entry.Applied = null;
			entry.AppliedValue = AvaloniaProperty.UnsetValue;
			return;
		}
		object? value = null;
		foreach (KeyValuePair<int, object?> pair in entry.Values)
		{
			value = pair.Value;
		}
		if (entry.Applied != null && ReferenceEquals(entry.AppliedValue, value))
		{
			return;
		}
		IDisposable? previous = entry.Applied;
		entry.AppliedValue = value;
		try
		{
			entry.Applied = Apply(target, property, value);
		}
		catch (Exception exception)
		{
			entry.Applied = null;
			App.LogException(exception, $"Trigger setter {property.Name}");
		}
		previous?.Dispose();
	}

	private IDisposable? Apply(AvaloniaObject target, AvaloniaProperty property, object? value)
	{
		if (value is DynamicResourceReference resource)
		{
			StyledElement anchor = target as StyledElement ?? _resourceAnchor;
			IObservable<object?> observable = anchor.GetResourceObservable(resource.Key, v => ValueConversion.ConvertTo(v, property.PropertyType));
			return target.Bind(property, observable, BindingPriority.StyleTrigger);
		}
		if (value is IBinding binding)
		{
			if (binding is Avalonia.Data.Binding concrete)
			{
				concrete.Priority = BindingPriority.StyleTrigger;
			}
			return target.Bind(property, binding);
		}
		object? converted = ValueConversion.ConvertTo(value, property.PropertyType);
		return target.SetValue(property, converted, BindingPriority.StyleTrigger);
	}
}

internal sealed class TriggerHost : IDisposable
{
	private readonly StyledElement _anchor;

	private readonly TriggerCollection _triggers;

	private readonly WpfTriggers.TriggerScope _scope;

	private readonly List<IDisposable> _subscriptions = new List<IDisposable>();

	private readonly Dictionary<string, (Storyboard Storyboard, object Key)> _namedStoryboards = new Dictionary<string, (Storyboard, object)>(StringComparer.Ordinal);

	private readonly List<Storyboard> _runningStoryboards = new List<Storyboard>();

	private SetterArbiter? _arbiter;

	private bool[] _states = Array.Empty<bool>();

	private bool _active;

	private bool _disposed;

	private StyledElement? _context;

	private static int _proxyCounter;

	private static readonly List<AttachedProperty<object?>> ProxyPool = new List<AttachedProperty<object?>>();

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StyledElement, StrongBox> ProxyUsage = new System.Runtime.CompilerServices.ConditionalWeakTable<StyledElement, StrongBox>();

	private sealed class StrongBox
	{
		public readonly HashSet<int> Used = new HashSet<int>();
	}

	public TriggerHost(StyledElement anchor, TriggerCollection triggers, WpfTriggers.TriggerScope scope)
	{
		_anchor = anchor;
		_triggers = triggers;
		_scope = scope;
	}

	public void Initialize()
	{
		if (_anchor is Visual visual)
		{
			visual.AttachedToVisualTree += OnAttached;
			visual.DetachedFromVisualTree += OnDetached;
			if (visual.IsAttachedToVisualTree())
			{
				Activate();
			}
			if (!_active)
			{
				// The collection may still be filling (XAML) or the element may attach later; retry once things settle.
				Dispatcher.UIThread.Post(() =>
				{
					if (!_disposed && !_active && visual.IsAttachedToVisualTree())
					{
						Activate();
					}
				}, DispatcherPriority.Loaded);
			}
		}
		else
		{
			Activate();
		}
	}

	private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
	{
		if (!_active)
		{
			Activate();
		}
	}

	private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
	{
		Deactivate();
	}

	private StyledElement ResolveContext()
	{
		if (_scope == WpfTriggers.TriggerScope.Template && _anchor.TemplatedParent is StyledElement templatedParent)
		{
			return templatedParent;
		}
		return _anchor;
	}

	private void Activate()
	{
		if (DebugEnabled)
		{
			Console.Error.WriteLine($"[trigger] activate {_scope} {_anchor.GetType().Name}#{_anchor.Name} tp={_anchor.TemplatedParent?.GetType().Name}");
		}
		if (_disposed || _active)
		{
			return;
		}
		if (_triggers.Count == 0)
		{
			return;
		}
		_active = true;
		_context = ResolveContext();
		_arbiter = new SetterArbiter(_anchor);
		_states = new bool[_triggers.Count];
		for (int i = 0; i < _triggers.Count; i++)
		{
			int index = i;
			TriggerBase trigger = _triggers[i];
			List<Func<bool>> evaluators = new List<Func<bool>>();
			switch (trigger)
			{
			case Trigger propertyTrigger:
				AddPropertyCondition(propertyTrigger.Property, propertyTrigger.SourceName, propertyTrigger.Value, evaluators, () => Evaluate(index));
				break;
			case DataTrigger dataTrigger:
				AddBindingCondition(dataTrigger.Binding, dataTrigger.Value, evaluators, () => Evaluate(index));
				break;
			case MultiTrigger multiTrigger:
				foreach (Condition condition in multiTrigger.Conditions)
				{
					AddPropertyCondition(condition.Property, condition.SourceName, condition.Value, evaluators, () => Evaluate(index));
				}
				break;
			case MultiDataTrigger multiDataTrigger:
				foreach (Condition condition in multiDataTrigger.Conditions)
				{
					if (condition.Binding != null)
					{
						AddBindingCondition(condition.Binding, condition.Value, evaluators, () => Evaluate(index));
					}
					else
					{
						AddPropertyCondition(condition.Property, condition.SourceName, condition.Value, evaluators, () => Evaluate(index));
					}
				}
				break;
			}
			_evaluators[index] = evaluators;
		}
		_initializing = true;
		for (int i = 0; i < _triggers.Count; i++)
		{
			Evaluate(i);
		}
		_initializing = false;
	}

	private readonly Dictionary<int, List<Func<bool>>> _evaluators = new Dictionary<int, List<Func<bool>>>();

	private bool _initializing;

	private void AddPropertyCondition(string? propertyName, string? sourceName, object? expected, List<Func<bool>> evaluators, Action changed)
	{
		if (string.IsNullOrWhiteSpace(propertyName) || _context == null)
		{
			evaluators.Add(() => false);
			return;
		}
		AvaloniaObject? source = string.IsNullOrEmpty(sourceName) ? _context : ResolveName(sourceName);
		if (source == null)
		{
			evaluators.Add(() => false);
			return;
		}
		AvaloniaProperty? property = WpfPropertyMap.Resolve(source.GetType(), propertyName);
		if (property == null)
		{
			evaluators.Add(() => false);
			return;
		}
		evaluators.Add(() => ValueConversion.LooseEquals(source.GetValue(property), expected));
		bool first = true;
		_subscriptions.Add(source.GetObservable(property).Subscribe(new ActionObserver<object?>(_ =>
		{
			if (first)
			{
				first = false;
				return;
			}
			changed();
		})));
	}

	private void AddBindingCondition(IBinding? binding, object? expected, List<Func<bool>> evaluators, Action changed)
	{
		if (binding == null || _context == null)
		{
			evaluators.Add(() => false);
			return;
		}
		StyledElement context = _context;
		if (binding is Avalonia.Data.Binding reflectionBinding && reflectionBinding.Path.Contains(':'))
		{
			// Bindings created by XAML outside a property assignment have no namespace context for "(prefix:Type.Prop)" paths.
			reflectionBinding.TypeResolver = (ns, name) => WpfPropertyMap.ResolveType(name) ?? throw new InvalidOperationException("Unknown type " + name);
		}
		AttachedProperty<object?> proxy = AcquireProxy(context, out int proxySlot);
		IDisposable? bindingSubscription = null;
		try
		{
			bindingSubscription = context.Bind(proxy, binding);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "DataTrigger binding");
		}
		if (bindingSubscription != null)
		{
			_subscriptions.Add(bindingSubscription);
		}
		_subscriptions.Add(new ActionDisposable(() =>
		{
			context.ClearValue(proxy);
			if (ProxyUsage.TryGetValue(context, out StrongBox? usage))
			{
				usage.Used.Remove(proxySlot);
			}
		}));
		evaluators.Add(() => ValueConversion.LooseEquals(context.GetValue(proxy), expected));
		bool first = true;
		_subscriptions.Add(context.GetObservable(proxy).Subscribe(new ActionObserver<object?>(_ =>
		{
			if (first)
			{
				first = false;
				return;
			}
			changed();
		})));
	}

	private static AttachedProperty<object?> AcquireProxy(StyledElement context, out int slot)
	{
		StrongBox usage = ProxyUsage.GetOrCreateValue(context);
		slot = 0;
		while (usage.Used.Contains(slot))
		{
			slot++;
		}
		usage.Used.Add(slot);
		lock (ProxyPool)
		{
			while (ProxyPool.Count <= slot)
			{
				int id = System.Threading.Interlocked.Increment(ref _proxyCounter);
				ProxyPool.Add(AvaloniaProperty.RegisterAttached<StyledElement, object?>("TriggerProxy" + id, typeof(TriggerHost)));
			}
			return ProxyPool[slot];
		}
	}

	internal static readonly bool DebugEnabled = Environment.GetEnvironmentVariable("DASHX360_TRIGGER_DEBUG") == "1";

	private void Evaluate(int index)
	{
		if (!_active || _disposed || !_evaluators.TryGetValue(index, out List<Func<bool>>? evaluators))
		{
			return;
		}
		bool state = evaluators.Count > 0;
		foreach (Func<bool> evaluator in evaluators)
		{
			if (!evaluator())
			{
				state = false;
				break;
			}
		}
		if (DebugEnabled)
		{
			Console.Error.WriteLine($"[trigger] {_scope} anchor={_anchor.GetType().Name}#{_anchor.Name} ctx={_context?.GetType().Name}#{_context?.Name} dc={_context?.DataContext?.GetType().Name} #{index} {_triggers[index].GetType().Name} -> {state}");
		}
		if (_states[index] == state)
		{
			return;
		}
		_states[index] = state;
		TriggerBase trigger = _triggers[index];
		ApplySetters(index, trigger, state);
		RunActions(state ? trigger.EnterActions : trigger.ExitActions);
	}

	private void ApplySetters(int triggerIndex, TriggerBase trigger, bool active)
	{
		if (_arbiter == null || _context == null)
		{
			return;
		}
		for (int s = 0; s < trigger.Setters.Count; s++)
		{
			Setter setter = trigger.Setters[s];
			if (string.IsNullOrWhiteSpace(setter.Property) || WpfPropertyMap.IsIgnored(setter.Property))
			{
				continue;
			}
			AvaloniaObject? target = string.IsNullOrEmpty(setter.TargetName) ? _context : ResolveName(setter.TargetName);
			if (target == null)
			{
				continue;
			}
			AvaloniaProperty? property = WpfPropertyMap.Resolve(target.GetType(), setter.Property);
			if (property == null || property.IsReadOnly)
			{
				continue;
			}
			int order = triggerIndex * 1000 + s;
			if (active)
			{
				_arbiter.Set(target, property, order, setter.Value);
			}
			else
			{
				_arbiter.Clear(target, property, order);
			}
		}
	}

	private void RunActions(List<TriggerAction> actions)
	{
		foreach (TriggerAction action in actions)
		{
			try
			{
				switch (action)
				{
				case BeginStoryboard begin when begin.Storyboard != null:
					begin.Storyboard.BeginCore(this, _context, ResolveName);
					if (!_runningStoryboards.Contains(begin.Storyboard))
					{
						_runningStoryboards.Add(begin.Storyboard);
					}
					if (!string.IsNullOrEmpty(begin.Name))
					{
						_namedStoryboards[begin.Name] = (begin.Storyboard, this);
					}
					break;
				case StopStoryboard stop when stop.BeginStoryboardName != null && _namedStoryboards.TryGetValue(stop.BeginStoryboardName, out var stopTarget):
					stopTarget.Storyboard.StopCore(stopTarget.Key, remove: true);
					break;
				case RemoveStoryboard remove when remove.BeginStoryboardName != null && _namedStoryboards.TryGetValue(remove.BeginStoryboardName, out var removeTarget):
					removeTarget.Storyboard.StopCore(removeTarget.Key, remove: true);
					break;
				case PauseStoryboard pause when pause.BeginStoryboardName != null && _namedStoryboards.TryGetValue(pause.BeginStoryboardName, out var pauseTarget):
					pauseTarget.Storyboard.Pause(pauseTarget.Key);
					break;
				case ResumeStoryboard resume when resume.BeginStoryboardName != null && _namedStoryboards.TryGetValue(resume.BeginStoryboardName, out var resumeTarget):
					resumeTarget.Storyboard.Resume(resumeTarget.Key);
					break;
				}
			}
			catch (Exception exception)
			{
				App.LogException(exception, "Trigger action");
			}
		}
	}

	private AvaloniaObject? ResolveName(string name)
	{
		return _scope switch
		{
			WpfTriggers.TriggerScope.Template => NameLookup.FindTemplatePart(_anchor, _anchor.TemplatedParent, name),
			WpfTriggers.TriggerScope.DataTemplate => NameLookup.FindTemplatePart(_anchor, null, name),
			_ => NameLookup.Find(_anchor, name)
		};
	}

	private void Deactivate()
	{
		if (!_active)
		{
			return;
		}
		_active = false;
		foreach (IDisposable subscription in _subscriptions)
		{
			try
			{
				subscription.Dispose();
			}
			catch
			{
			}
		}
		_subscriptions.Clear();
		_evaluators.Clear();
		_arbiter?.ClearAll();
		_arbiter = null;
		_context = null;
		_namedStoryboards.Clear();
		_runningStoryboards.Clear();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		Deactivate();
		_disposed = true;
		if (_anchor is Visual visual)
		{
			visual.AttachedToVisualTree -= OnAttached;
			visual.DetachedFromVisualTree -= OnDetached;
		}
	}
}

internal sealed class ActionObserver<T> : IObserver<T>
{
	private readonly Action<T> _onNext;

	public ActionObserver(Action<T> onNext)
	{
		_onNext = onNext;
	}

	public void OnCompleted()
	{
	}

	public void OnError(Exception error)
	{
	}

	public void OnNext(T value) => _onNext(value);
}

internal sealed class ActionDisposable : IDisposable
{
	private Action? _action;

	public ActionDisposable(Action action)
	{
		_action = action;
	}

	public void Dispose()
	{
		System.Threading.Interlocked.Exchange(ref _action, null)?.Invoke();
	}
}

internal static class VisualAttachmentExtensions
{
	public static bool IsAttachedToVisualTree(this Visual visual) => visual.GetVisualRoot() != null;

	private static Avalonia.Rendering.IRenderRoot? GetVisualRoot(this Visual visual) => Avalonia.VisualTree.VisualExtensions.GetVisualRoot(visual) as Avalonia.Rendering.IRenderRoot;
}
