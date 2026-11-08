using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface IDashPartyLinkService
{
	bool IsConfigured { get; }

	string LastStatusMessage { get; }

	Task<DashPartyLinkConfig> GetOrCreateConfigAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task SaveConfigAsync(DashPartyLinkConfig config, CancellationToken cancellationToken = default(CancellationToken));

	Task<DashPartyLinkTestResult> RunSelfTestAsync(Profile profile, CancellationToken cancellationToken = default(CancellationToken));

	Task<IReadOnlyList<SocialFriend>> LoadFriendsAsync(Profile profile, IReadOnlyList<FriendProfile> savedFriends, CancellationToken cancellationToken = default(CancellationToken));

	Task<SocialPartyInviteResult> InviteToPartyAsync(Profile profile, SocialFriend friend, CancellationToken cancellationToken = default(CancellationToken));

	Task<IReadOnlyList<DashPartyInvite>> GetPendingInvitesAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task SendTextMessageAsync(SocialFriend friend, string message, CancellationToken cancellationToken = default(CancellationToken));

	Task<IReadOnlyList<DashPartyTextMessage>> GetTextMessagesAsync(CancellationToken cancellationToken = default(CancellationToken));
}
