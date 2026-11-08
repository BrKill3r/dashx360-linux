using System;

namespace XboxMetroLauncher.Models;

public sealed class DashPartyLinkConfig
{
	public string ServiceUrl { get; set; } = string.Empty;

	public string DeviceId { get; set; } = string.Empty;

	public string FriendCode { get; set; } = string.Empty;

	public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
