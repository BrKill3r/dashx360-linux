namespace XboxMetroLauncher.Models;

public sealed class DashboardToastRequest
{
	public string Line1 { get; set; } = string.Empty;

	public string Line2 { get; set; } = string.Empty;

	public bool AcceptPartyInviteWithGuide { get; set; }

	public string ActionUri { get; set; } = string.Empty;
}
