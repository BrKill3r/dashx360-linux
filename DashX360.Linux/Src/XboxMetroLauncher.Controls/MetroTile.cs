using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace XboxMetroLauncher.Controls;

public partial class MetroTile : UserControl
{
	public static readonly StyledProperty<string> TitleProperty = (StyledProperty<string>)DependencyProperty.Register("Title", typeof(string), typeof(MetroTile), new PropertyMetadata((object)string.Empty));

	public static readonly StyledProperty<string> SubtitleProperty = (StyledProperty<string>)DependencyProperty.Register("Subtitle", typeof(string), typeof(MetroTile), new PropertyMetadata((object)string.Empty));

	public static readonly StyledProperty<string> ImagePathProperty = (StyledProperty<string>)DependencyProperty.Register("ImagePath", typeof(string), typeof(MetroTile), new PropertyMetadata((object)string.Empty));

	public static readonly StyledProperty<Brush> TileBrushProperty = (StyledProperty<Brush>)DependencyProperty.Register("TileBrush", typeof(Brush), typeof(MetroTile), new PropertyMetadata((object)Brushes.SeaGreen));

	public static readonly StyledProperty<double> TileWidthProperty = (StyledProperty<double>)DependencyProperty.Register("TileWidth", typeof(double), typeof(MetroTile), new PropertyMetadata((object)250.0));

	public static readonly StyledProperty<double> TileHeightProperty = (StyledProperty<double>)DependencyProperty.Register("TileHeight", typeof(double), typeof(MetroTile), new PropertyMetadata((object)140.0));

	public static readonly StyledProperty<ICommand> CommandProperty = (StyledProperty<ICommand>)DependencyProperty.Register("Command", typeof(ICommand), typeof(MetroTile), new PropertyMetadata((PropertyChangedCallback)null));

	public static readonly StyledProperty<object> CommandParameterProperty = (StyledProperty<object>)DependencyProperty.Register("CommandParameter", typeof(object), typeof(MetroTile), new PropertyMetadata((PropertyChangedCallback)null));

	public static readonly StyledProperty<ICommand> FocusCommandProperty = (StyledProperty<ICommand>)DependencyProperty.Register("FocusCommand", typeof(ICommand), typeof(MetroTile), new PropertyMetadata((PropertyChangedCallback)null));

	public string Title
	{
		get
		{
			return (string)((DependencyObject)this).GetValue(TitleProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(TitleProperty, (object)value);
		}
	}

	public string Subtitle
	{
		get
		{
			return (string)((DependencyObject)this).GetValue(SubtitleProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(SubtitleProperty, (object)value);
		}
	}

	public string ImagePath
	{
		get
		{
			return (string)((DependencyObject)this).GetValue(ImagePathProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(ImagePathProperty, (object)value);
		}
	}

	public Brush TileBrush
	{
		get
		{
			return (Brush)((DependencyObject)this).GetValue(TileBrushProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(TileBrushProperty, (object)value);
		}
	}

	public double TileWidth
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(TileWidthProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(TileWidthProperty, (object)value);
		}
	}

	public double TileHeight
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(TileHeightProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(TileHeightProperty, (object)value);
		}
	}

	public ICommand? Command
	{
		get
		{
			return (ICommand)((DependencyObject)this).GetValue(CommandProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(CommandProperty, (object)value);
		}
	}

	public object? CommandParameter
	{
		get
		{
			return ((DependencyObject)this).GetValue(CommandParameterProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(CommandParameterProperty, value);
		}
	}

	public ICommand? FocusCommand
	{
		get
		{
			return (ICommand)((DependencyObject)this).GetValue(FocusCommandProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(FocusCommandProperty, (object)value);
		}
	}

	public MetroTile()
	{
		InitializeComponent();
	}

	private void TileButton_OnGotKeyboardFocus(object sender, GotFocusEventArgs e)
	{
		ICommand? focusCommand = FocusCommand;
		if (focusCommand != null && focusCommand.CanExecute(CommandParameter))
		{
			FocusCommand.Execute(CommandParameter);
		}
	}
}
