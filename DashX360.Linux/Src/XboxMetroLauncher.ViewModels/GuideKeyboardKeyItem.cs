using System;

namespace XboxMetroLauncher.ViewModels;

public sealed class GuideKeyboardKeyItem : ObservableObject
{
	private string _label = string.Empty;

	private bool _isSelected;

	private bool _isPressedVisual;

	private int _pressVisualVersion;

	public string Label
	{
		get
		{
			return _label;
		}
		set
		{
			SetProperty(ref _label, value, "Label");
		}
	}

	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			SetProperty(ref _isSelected, value, "IsSelected");
		}
	}

	public bool IsPressedVisual
	{
		get
		{
			return _isPressedVisual;
		}
		set
		{
			SetProperty(ref _isPressedVisual, value, "IsPressedVisual");
		}
	}

	public bool IsWide { get; }

	public int PressVisualVersion
	{
		get
		{
			return _pressVisualVersion;
		}
		set
		{
			SetProperty(ref _pressVisualVersion, value, "PressVisualVersion");
		}
	}

	public Action Action { get; }

	public GuideKeyboardKeyItem(string label, Action action, bool isWide = false)
	{
		_label = label;
		Action = action;
		IsWide = isWide;
	}
}
