using Avalonia.Input;

namespace XboxMetroLauncher.ViewModels;

public sealed class GameDetailsExtraViewModel
{
	public string Title { get; init; } = string.Empty;

	public string PriceText { get; init; } = string.Empty;

	public string RatingText { get; init; } = "*****";

	public string IconPath { get; init; } = string.Empty;

	public string SteamAppId { get; init; } = string.Empty;

	public bool IsSeeAll { get; init; }

	public ICommand? Command { get; init; }
}
