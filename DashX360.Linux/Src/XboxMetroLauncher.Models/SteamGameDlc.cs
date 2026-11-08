namespace XboxMetroLauncher.Models;

public sealed class SteamGameDlc
{
	public string AppId { get; init; } = string.Empty;

	public string Name { get; init; } = string.Empty;

	public string PriceText { get; init; } = string.Empty;
}
