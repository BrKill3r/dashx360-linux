namespace XboxMetroLauncher.Models;

public sealed class SocialPartyInviteResult
{
	public bool AddToPartyList { get; init; }

	public string PopupMessage { get; init; } = string.Empty;

	public string ActionUri { get; init; } = string.Empty;
}
