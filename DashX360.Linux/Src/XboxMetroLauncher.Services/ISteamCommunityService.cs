using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public interface ISteamCommunityService
{
	string LastStatusMessage { get; }

	bool IsConfigured { get; }

	Task<SteamCommunityConfig> LoadConfigAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task SaveConfigAsync(SteamCommunityConfig config, CancellationToken cancellationToken = default(CancellationToken));

	Task<SteamConnectionTestResult> TestConnectionAsync(SteamCommunityConfig config, CancellationToken cancellationToken = default(CancellationToken));

	Task<IReadOnlyList<SocialFriend>> LoadFriendsAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<IReadOnlyList<SteamAchievementItem>> LoadAchievementsAsync(string appId, CancellationToken cancellationToken = default(CancellationToken));

	Task<SteamGameDetails> LoadGameDetailsAsync(string appId, CancellationToken cancellationToken = default(CancellationToken));
}
