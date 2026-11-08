using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace XboxMetroLauncher.ViewModels;

public sealed class DashboardTabCustomizationViewModel
{
	public string Key { get; }

	public string Name { get; }

	public ObservableCollection<DashboardTileCustomizationViewModel> Tiles { get; }

	public Thickness ContentMargin { get; }

	public DashboardTabCustomizationViewModel(string key, string name, IEnumerable<DashboardTileCustomizationViewModel> tiles)
	{
		Key = key;
		Name = name;
		Tiles = new ObservableCollection<DashboardTileCustomizationViewModel>(tiles);
		ContentMargin = CalculateContentMargin(Tiles);
	}

	private static Thickness CalculateContentMargin(IEnumerable<DashboardTileCustomizationViewModel> tiles)
	{
		List<DashboardTileCustomizationViewModel> list = tiles.ToList();
		if (list.Count == 0)
		{
			return default(Thickness);
		}
		double num = list.Min((DashboardTileCustomizationViewModel tile) => tile.Left);
		double num2 = list.Max((DashboardTileCustomizationViewModel tile) => tile.Left + tile.Width);
		return new Thickness(Math.Max(0.0, (806.0 - (num2 - num)) / 2.0 - num), 0.0, 0.0, 0.0);
	}
}
