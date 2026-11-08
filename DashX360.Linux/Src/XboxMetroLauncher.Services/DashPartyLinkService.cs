using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public sealed class DashPartyLinkService : IDashPartyLinkService
{
	private const string ConfigFileName = "dash-party-link.json";

	private const string DefaultServiceUrl = "https://dashx360-party-link.dashx360cloudflare.workers.dev";

	private static readonly HttpClient HttpClient = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(3.0)
	};

	private readonly IJsonStore _jsonStore;

	private DashPartyLinkConfig? _config;

	public bool IsConfigured => !string.IsNullOrWhiteSpace(_config?.ServiceUrl);

	public string LastStatusMessage { get; private set; } = string.Empty;

	public DashPartyLinkService(IJsonStore jsonStore)
	{
		_jsonStore = jsonStore;
	}

	public async Task<DashPartyLinkConfig> GetOrCreateConfigAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (_config != null)
		{
			return _config;
		}
		DashPartyLinkConfig dashPartyLinkConfig = await _jsonStore.ReadAsync<DashPartyLinkConfig>(ConfigFileName, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) ?? new DashPartyLinkConfig();
		bool flag = false;
		if (!string.Equals(dashPartyLinkConfig.ServiceUrl?.Trim(), DefaultServiceUrl, StringComparison.OrdinalIgnoreCase))
		{
			dashPartyLinkConfig.ServiceUrl = DefaultServiceUrl;
			flag = true;
		}
		if (string.IsNullOrWhiteSpace(dashPartyLinkConfig.DeviceId))
		{
			dashPartyLinkConfig.DeviceId = "dash-" + RandomNumberGenerator.GetHexString(16).ToLowerInvariant();
			flag = true;
		}
		if (string.IsNullOrWhiteSpace(dashPartyLinkConfig.FriendCode))
		{
			dashPartyLinkConfig.FriendCode = GenerateFriendCode();
			flag = true;
		}
		if (dashPartyLinkConfig.CreatedUtc == default(DateTimeOffset))
		{
			dashPartyLinkConfig.CreatedUtc = DateTimeOffset.UtcNow;
			flag = true;
		}
		_config = dashPartyLinkConfig;
		if (flag)
		{
			await _jsonStore.WriteAsync(ConfigFileName, dashPartyLinkConfig, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		return dashPartyLinkConfig;
	}

	public async Task SaveConfigAsync(DashPartyLinkConfig config, CancellationToken cancellationToken = default(CancellationToken))
	{
		DashPartyLinkConfig current = await GetOrCreateConfigAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		current.ServiceUrl = DefaultServiceUrl;
		if (!string.IsNullOrWhiteSpace(config.DeviceId))
		{
			current.DeviceId = config.DeviceId.Trim();
		}
		if (!string.IsNullOrWhiteSpace(config.FriendCode))
		{
			current.FriendCode = config.FriendCode.Trim().ToUpperInvariant();
		}
		await _jsonStore.WriteAsync(ConfigFileName, current, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		_config = current;
		LastStatusMessage = string.Empty;
	}

	public async Task<DashPartyLinkTestResult> RunSelfTestAsync(Profile profile, CancellationToken cancellationToken = default(CancellationToken))
	{
		DashPartyLinkConfig config = await GetOrCreateConfigAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrWhiteSpace(config.ServiceUrl))
		{
			return new DashPartyLinkTestResult
			{
				Message = "Add a Party Link service URL first."
			};
		}
		string testDeviceId = "dash-test-" + RandomNumberGenerator.GetHexString(8).ToLowerInvariant();
		string testFriendCode = GenerateFriendCode();
		try
		{
			Uri baseUri = CreateBaseUri(config.ServiceUrl);
			using HttpResponseMessage presenceResponse = await HttpClient.PostAsJsonAsync(new Uri(baseUri, "presence"), new DashPresenceRequest
			{
				DeviceId = config.DeviceId,
				FriendCode = config.FriendCode,
				Gamertag = profile.Gamertag,
				AvatarPath = profile.GamerPicturePath,
				Status = profile.OnlineStatus,
				Activity = "DashX360 Party Link Self Test"
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			presenceResponse.EnsureSuccessStatusCode();
			using HttpResponseMessage testPresenceResponse = await HttpClient.PostAsJsonAsync(new Uri(baseUri, "presence"), new DashPresenceRequest
			{
				DeviceId = testDeviceId,
				FriendCode = testFriendCode,
				Gamertag = "DashX360 Test",
				Status = "Online",
				Activity = "Testing Party Link"
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			testPresenceResponse.EnsureSuccessStatusCode();
			using HttpResponseMessage friendsResponse = await HttpClient.PostAsJsonAsync(new Uri(baseUri, "friends/status"), new DashFriendsStatusRequest
			{
				Friends = new DashFriendLookup[1]
				{
					new DashFriendLookup
					{
						FriendCode = testFriendCode
					}
				}
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			friendsResponse.EnsureSuccessStatusCode();
			DashFriendsStatusResponse? friendsStatus = await friendsResponse.Content.ReadFromJsonAsync<DashFriendsStatusResponse>(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (friendsStatus?.Friends.FirstOrDefault()?.IsOnline != true)
			{
				return new DashPartyLinkTestResult
				{
					Message = "Party Link connected, but friend lookup failed."
				};
			}
			using HttpResponseMessage inviteResponse = await HttpClient.PostAsJsonAsync(new Uri(baseUri, "party/invite"), new DashPartyInviteRequest
			{
				FromDeviceId = testDeviceId,
				FromGamertag = "DashX360 Test",
				ToDeviceId = config.DeviceId
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			inviteResponse.EnsureSuccessStatusCode();
			DashInviteResponse? invites = await HttpClient.GetFromJsonAsync<DashInviteResponse>(new Uri(baseUri, "party/invites?deviceId=" + Uri.EscapeDataString(config.DeviceId)), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			bool inviteDelivered = invites?.Invites.Any((DashPartyInvite invite) => string.Equals(invite.FromDeviceId, testDeviceId, StringComparison.OrdinalIgnoreCase)) == true;
			using HttpResponseMessage messageResponse = await HttpClient.PostAsJsonAsync(new Uri(baseUri, "messages/send"), new DashMessageSendRequest
			{
				FromDeviceId = testDeviceId,
				ToDeviceId = config.DeviceId,
				Message = "Party Link self-test message"
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			messageResponse.EnsureSuccessStatusCode();
			DashMessagesResponse? messages = await HttpClient.GetFromJsonAsync<DashMessagesResponse>(new Uri(baseUri, "messages/poll?deviceId=" + Uri.EscapeDataString(config.DeviceId)), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			bool messageDelivered = messages?.Messages.Any((DashPartyTextMessage message) => string.Equals(message.FromDeviceId, testDeviceId, StringComparison.OrdinalIgnoreCase)) == true;
			if (inviteDelivered && messageDelivered)
			{
				return new DashPartyLinkTestResult
				{
					Success = true,
					Message = "Party Link test passed. Invites and messages are reaching this dashboard."
				};
			}
			return new DashPartyLinkTestResult
			{
				Message = "Party Link connected, but " + (inviteDelivered ? "message delivery failed." : "invite delivery failed.")
			};
		}
		catch
		{
			return new DashPartyLinkTestResult
			{
				Message = "Party Link test failed. Check the service URL and internet connection."
			};
		}
	}

	public async Task<IReadOnlyList<SocialFriend>> LoadFriendsAsync(Profile profile, IReadOnlyList<FriendProfile> savedFriends, CancellationToken cancellationToken = default(CancellationToken))
	{
		DashPartyLinkConfig config = await GetOrCreateConfigAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrWhiteSpace(config.ServiceUrl))
		{
			LastStatusMessage = string.Empty;
			return Array.Empty<SocialFriend>();
		}
		try
		{
			Uri baseUri = CreateBaseUri(config.ServiceUrl);
			await PublishPresenceAsync(baseUri, config, profile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			List<DashFriendLookup> friendCodes = savedFriends.Select(CreateLookup).Where((DashFriendLookup item) => !string.IsNullOrWhiteSpace(item.FriendCode) || !string.IsNullOrWhiteSpace(item.DeviceId)).ToList();
			if (friendCodes.Count == 0)
			{
				LastStatusMessage = string.Empty;
				return Array.Empty<SocialFriend>();
			}
			using HttpResponseMessage friendsResponse = await HttpClient.PostAsJsonAsync(new Uri(baseUri, "friends/status"), new DashFriendsStatusRequest
			{
				Friends = friendCodes
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			friendsResponse.EnsureSuccessStatusCode();
			DashFriendsStatusResponse? dashFriendsStatusResponse = await friendsResponse.Content.ReadFromJsonAsync<DashFriendsStatusResponse>(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			LastStatusMessage = string.Empty;
			return (dashFriendsStatusResponse?.Friends ?? Array.Empty<DashFriendPresence>()).Select(MapPresence).ToList();
		}
		catch
		{
			LastStatusMessage = "DashX360 Party Link could not connect.";
			return Array.Empty<SocialFriend>();
		}
	}

	public async Task<SocialPartyInviteResult> InviteToPartyAsync(Profile profile, SocialFriend friend, CancellationToken cancellationToken = default(CancellationToken))
	{
		DashPartyLinkConfig config = await GetOrCreateConfigAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrWhiteSpace(config.ServiceUrl))
		{
			return new SocialPartyInviteResult
			{
				AddToPartyList = true,
				PopupMessage = "DashX360 Party Link is ready. Add a signaling service URL to connect parties online."
			};
		}
		try
		{
			Uri baseUri = CreateBaseUri(config.ServiceUrl);
			await PublishPresenceAsync(baseUri, config, profile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			using HttpResponseMessage response = await HttpClient.PostAsJsonAsync(new Uri(baseUri, "party/invite"), new DashPartyInviteRequest
			{
				FromDeviceId = config.DeviceId,
				FromGamertag = profile.Gamertag,
				ToDeviceId = ExtractDashDeviceId(friend),
				ToFriendCode = ExtractDashFriendCode(friend)
			}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			response.EnsureSuccessStatusCode();
			return new SocialPartyInviteResult
			{
				AddToPartyList = true
			};
		}
		catch
		{
			return new SocialPartyInviteResult
			{
				AddToPartyList = true,
				PopupMessage = "DashX360 Party Link invite could not be delivered."
			};
		}
	}

	public async Task<IReadOnlyList<DashPartyInvite>> GetPendingInvitesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		DashPartyLinkConfig config = await GetOrCreateConfigAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrWhiteSpace(config.ServiceUrl))
		{
			return Array.Empty<DashPartyInvite>();
		}
		try
		{
			Uri baseUri = CreateBaseUri(config.ServiceUrl);
			await PublishPresenceAsync(baseUri, config, profile: null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			DashInviteResponse? response = await HttpClient.GetFromJsonAsync<DashInviteResponse>(new Uri(baseUri, "party/invites?deviceId=" + Uri.EscapeDataString(config.DeviceId)), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return response?.Invites ?? Array.Empty<DashPartyInvite>();
		}
		catch
		{
			return Array.Empty<DashPartyInvite>();
		}
	}

	public async Task SendTextMessageAsync(SocialFriend friend, string message, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}
		DashPartyLinkConfig config = await GetOrCreateConfigAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrWhiteSpace(config.ServiceUrl))
		{
			return;
		}
		Uri baseUri = CreateBaseUri(config.ServiceUrl);
		using HttpResponseMessage response = await HttpClient.PostAsJsonAsync(new Uri(baseUri, "messages/send"), new DashMessageSendRequest
		{
			FromDeviceId = config.DeviceId,
			ToDeviceId = ExtractDashDeviceId(friend),
			ToFriendCode = ExtractDashFriendCode(friend),
			Message = message.Trim()
		}, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		response.EnsureSuccessStatusCode();
	}

	public async Task<IReadOnlyList<DashPartyTextMessage>> GetTextMessagesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		DashPartyLinkConfig config = await GetOrCreateConfigAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrWhiteSpace(config.ServiceUrl))
		{
			return Array.Empty<DashPartyTextMessage>();
		}
		try
		{
			Uri baseUri = CreateBaseUri(config.ServiceUrl);
			DashMessagesResponse? response = await HttpClient.GetFromJsonAsync<DashMessagesResponse>(new Uri(baseUri, "messages/poll?deviceId=" + Uri.EscapeDataString(config.DeviceId)), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			return response?.Messages ?? Array.Empty<DashPartyTextMessage>();
		}
		catch
		{
			return Array.Empty<DashPartyTextMessage>();
		}
	}

	public static bool IsDashFriend(FriendProfile friend)
	{
		return !string.IsNullOrWhiteSpace(friend.DashX360Id) || !string.IsNullOrWhiteSpace(friend.DashX360FriendCode);
	}

	private static DashFriendLookup CreateLookup(FriendProfile friend)
	{
		return new DashFriendLookup
		{
			Gamertag = friend.Gamertag,
			DeviceId = friend.DashX360Id,
			FriendCode = friend.DashX360FriendCode
		};
	}

	private static SocialFriend MapPresence(DashFriendPresence presence)
	{
		return new SocialFriend
		{
			Id = "dashx360:" + (string.IsNullOrWhiteSpace(presence.DeviceId) ? presence.FriendCode : presence.DeviceId),
			DisplayName = string.IsNullOrWhiteSpace(presence.Gamertag) ? "DashX360 Player" : presence.Gamertag,
			Source = SocialFriendSource.DashX360,
			AvatarPathOrUrl = presence.AvatarPath ?? string.Empty,
			IsOnline = presence.IsOnline,
			StatusText = presence.IsOnline ? (string.IsNullOrWhiteSpace(presence.Status) ? "Online" : presence.Status) : "Offline",
			ActivityText = presence.Activity ?? string.Empty,
			GamerscoreText = "0 G",
			ReputationText = "*****",
			ZoneText = "Party",
			IdentityDetailText = string.IsNullOrWhiteSpace(presence.FriendCode) ? "DashX360" : ("DashX360 " + presence.FriendCode)
		};
	}

	private static Uri CreateBaseUri(string serviceUrl)
	{
		string text = serviceUrl.Trim();
		if (!text.EndsWith("/", StringComparison.Ordinal))
		{
			text += "/";
		}
		return new Uri(text, UriKind.Absolute);
	}

	private static async Task PublishPresenceAsync(Uri baseUri, DashPartyLinkConfig config, Profile? profile, CancellationToken cancellationToken)
	{
		using HttpResponseMessage presenceResponse = await HttpClient.PostAsJsonAsync(new Uri(baseUri, "presence"), CreatePresenceRequest(config, profile), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		presenceResponse.EnsureSuccessStatusCode();
	}

	private static DashPresenceRequest CreatePresenceRequest(DashPartyLinkConfig config, Profile? profile)
	{
		return new DashPresenceRequest
		{
			DeviceId = config.DeviceId,
			FriendCode = config.FriendCode,
			Gamertag = string.IsNullOrWhiteSpace(profile?.Gamertag) ? "DashX360 Player" : profile.Gamertag,
			AvatarPath = profile?.GamerPicturePath ?? string.Empty,
			Status = string.IsNullOrWhiteSpace(profile?.OnlineStatus) ? "Online" : profile.OnlineStatus,
			Activity = "Xbox 360 Dashboard"
		};
	}

	private static string ExtractDashDeviceId(SocialFriend friend)
	{
		if (friend.Id.StartsWith("dashx360:", StringComparison.OrdinalIgnoreCase))
		{
			string text = friend.Id.Substring("dashx360:".Length).Trim();
			if (text.StartsWith("dash-", StringComparison.OrdinalIgnoreCase))
			{
				return text;
			}
		}
		return string.Empty;
	}

	private static string ExtractDashFriendCode(SocialFriend friend)
	{
		string marker = "DashX360 ";
		if (!string.IsNullOrWhiteSpace(friend.IdentityDetailText) && friend.IdentityDetailText.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
		{
			return friend.IdentityDetailText.Substring(marker.Length).Trim();
		}
		return string.Empty;
	}

	private static string GenerateFriendCode()
	{
		return "DX-" + RandomNumberGenerator.GetHexString(3).ToUpperInvariant() + "-" + RandomNumberGenerator.GetHexString(3).ToUpperInvariant();
	}

	private sealed class DashPresenceRequest
	{
		public string DeviceId { get; set; } = string.Empty;

		public string FriendCode { get; set; } = string.Empty;

		public string Gamertag { get; set; } = string.Empty;

		public string AvatarPath { get; set; } = string.Empty;

		public string Status { get; set; } = string.Empty;

		public string Activity { get; set; } = string.Empty;
	}

	private sealed class DashFriendsStatusRequest
	{
		public IReadOnlyList<DashFriendLookup> Friends { get; set; } = Array.Empty<DashFriendLookup>();
	}

	private sealed class DashFriendLookup
	{
		public string Gamertag { get; set; } = string.Empty;

		public string DeviceId { get; set; } = string.Empty;

		public string FriendCode { get; set; } = string.Empty;
	}

	private sealed class DashFriendsStatusResponse
	{
		public IReadOnlyList<DashFriendPresence> Friends { get; set; } = Array.Empty<DashFriendPresence>();
	}

	private sealed class DashFriendPresence
	{
		public string DeviceId { get; set; } = string.Empty;

		public string FriendCode { get; set; } = string.Empty;

		public string Gamertag { get; set; } = string.Empty;

		public string? AvatarPath { get; set; }

		public bool IsOnline { get; set; }

		public string? Status { get; set; }

		public string? Activity { get; set; }
	}

	private sealed class DashPartyInviteRequest
	{
		public string FromDeviceId { get; set; } = string.Empty;

		public string FromGamertag { get; set; } = string.Empty;

		public string ToDeviceId { get; set; } = string.Empty;

		public string ToFriendCode { get; set; } = string.Empty;
	}

	private sealed class DashInviteResponse
	{
		public IReadOnlyList<DashPartyInvite> Invites { get; set; } = Array.Empty<DashPartyInvite>();
	}

	private sealed class DashMessageSendRequest
	{
		public string FromDeviceId { get; set; } = string.Empty;

		public string ToDeviceId { get; set; } = string.Empty;

		public string ToFriendCode { get; set; } = string.Empty;

		public string Message { get; set; } = string.Empty;
	}

	private sealed class DashMessagesResponse
	{
		public IReadOnlyList<DashPartyTextMessage> Messages { get; set; } = Array.Empty<DashPartyTextMessage>();
	}
}
