using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using XboxMetroLauncher.ViewModels.Tabs;

namespace XboxMetroLauncher.Views.Tabs;

public partial class BingTabView : UserControl
{

	public BingTabView()
	{
		InitializeComponent();
	}

	private void SearchBoxFrame_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (!BingSearchBox.IsKeyboardFocusWithin)
		{
			BingSearchBox.Focus();
			BingSearchBox.CaretIndex = BingSearchBox.Text.Length;
			e.Handled = true;
		}
	}

	private void BingSearchBox_OnKeyDown(object sender, KeyEventArgs e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Invalid comparison between Unknown and I4
		if ((int)e.Key == 6 && base.DataContext is BingTabViewModel bingTabViewModel && bingTabViewModel.SubmitSearchCommand.CanExecute(null))
		{
			bingTabViewModel.SubmitSearchCommand.Execute(null);
			e.Handled = true;
		}
	}
}
