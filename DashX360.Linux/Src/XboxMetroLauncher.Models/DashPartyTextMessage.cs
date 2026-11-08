using System;

namespace XboxMetroLauncher.Models;

public sealed class DashPartyTextMessage
{
	public string FromDeviceId { get; set; } = string.Empty;

	public string FromGamertag { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;

	public DateTimeOffset CreatedUtc { get; set; }
}
