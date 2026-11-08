// MPRIS (org.mpris.MediaPlayer2) media-session access through gdbus: Linux replacement for the
// Windows GlobalSystemMediaTransportControls API and media virtual keys.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace XboxMetroLauncher.Platform;

public enum MediaPlaybackStatus
{
	Unknown,
	Playing,
	Paused,
	Stopped
}

public sealed record MediaSessionInfo(string BusName, string Title, string Artist, TimeSpan Position, TimeSpan Duration, MediaPlaybackStatus Status);

public static class Mpris
{
	private const string PlayerPath = "/org/mpris/MediaPlayer2";

	private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";

	public static async Task<IReadOnlyList<string>> ListPlayersAsync()
	{
		string? output = await RunGdbusAsync("call", "--session", "--dest", "org.freedesktop.DBus", "--object-path", "/org/freedesktop/DBus", "--method", "org.freedesktop.DBus.ListNames").ConfigureAwait(false);
		if (output == null)
		{
			return Array.Empty<string>();
		}
		return Regex.Matches(output, @"'(org\.mpris\.MediaPlayer2\.[^']+)'").Select(m => m.Groups[1].Value).Distinct().ToList();
	}

	/// <summary>Picks a Spotify session when present, otherwise the first playing session, otherwise any session.</summary>
	public static async Task<string?> ResolvePlayerAsync(string? preferred = "spotify")
	{
		IReadOnlyList<string> players = await ListPlayersAsync().ConfigureAwait(false);
		if (players.Count == 0)
		{
			return null;
		}
		if (!string.IsNullOrWhiteSpace(preferred))
		{
			string? match = players.FirstOrDefault(p => p.Contains(preferred, StringComparison.OrdinalIgnoreCase));
			if (match != null)
			{
				return match;
			}
		}
		foreach (string player in players)
		{
			MediaSessionInfo? info = await GetSessionAsync(player).ConfigureAwait(false);
			if (info?.Status == MediaPlaybackStatus.Playing)
			{
				return player;
			}
		}
		return players[0];
	}

	public static async Task<MediaSessionInfo?> GetSessionAsync(string busName)
	{
		string? output = await RunGdbusAsync("call", "--session", "--dest", busName, "--object-path", PlayerPath, "--method", "org.freedesktop.DBus.Properties.GetAll", PlayerInterface).ConfigureAwait(false);
		if (output == null)
		{
			return null;
		}
		string status = Match(output, @"'PlaybackStatus':\s*<'([^']*)'>");
		string title = Unescape(Match(output, @"'xesam:title':\s*<'((?:[^'\\]|\\.)*)'>"));
		if (string.IsNullOrEmpty(title))
		{
			title = Unescape(Match(output, @"'xesam:title':\s*<""((?:[^""\\]|\\.)*)"">"));
		}
		string artist = Unescape(Match(output, @"'xesam:artist':\s*<\['((?:[^'\\]|\\.)*)'"));
		long length = ParseLong(Match(output, @"'mpris:length':\s*<(?:u?int64\s+)?(\d+)>"));
		long position = ParseLong(Match(output, @"'Position':\s*<(?:u?int64\s+)?(-?\d+)>"));
		MediaPlaybackStatus playback = status switch
		{
			"Playing" => MediaPlaybackStatus.Playing,
			"Paused" => MediaPlaybackStatus.Paused,
			"Stopped" => MediaPlaybackStatus.Stopped,
			_ => MediaPlaybackStatus.Unknown
		};
		return new MediaSessionInfo(busName, title, artist, TimeSpan.FromTicks(Math.Max(0, position) * 10), TimeSpan.FromTicks(Math.Max(0, length) * 10), playback);
	}

	public static async Task<bool> SendAsync(string command, string? preferred = "spotify")
	{
		string? player = await ResolvePlayerAsync(preferred).ConfigureAwait(false);
		if (player == null)
		{
			return false;
		}
		string? output = await RunGdbusAsync("call", "--session", "--dest", player, "--object-path", PlayerPath, "--method", PlayerInterface + "." + command).ConfigureAwait(false);
		return output != null;
	}

	private static string Match(string input, string pattern)
	{
		System.Text.RegularExpressions.Match match = Regex.Match(input, pattern);
		return match.Success ? match.Groups[1].Value : string.Empty;
	}

	private static long ParseLong(string text) => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;

	private static string Unescape(string text) => text.Replace("\\'", "'").Replace("\\\"", "\"").Replace("\\\\", "\\");

	private static async Task<string?> RunGdbusAsync(params string[] args)
	{
		try
		{
			ProcessStartInfo info = new ProcessStartInfo("gdbus")
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			foreach (string arg in args)
			{
				info.ArgumentList.Add(arg);
			}
			using Process? process = Process.Start(info);
			if (process == null)
			{
				return null;
			}
			Task<string> read = process.StandardOutput.ReadToEndAsync();
			Task wait = process.WaitForExitAsync();
			if (await Task.WhenAny(wait, Task.Delay(2000)).ConfigureAwait(false) != wait)
			{
				try
				{
					process.Kill();
				}
				catch
				{
				}
				return null;
			}
			string output = await read.ConfigureAwait(false);
			return process.ExitCode == 0 ? output : null;
		}
		catch
		{
			return null;
		}
	}
}
