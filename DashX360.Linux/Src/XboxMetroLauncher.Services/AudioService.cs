using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class AudioService : IAudioService
{
	private readonly Func<bool> _isEnabled;

	private readonly Func<string> _selectedOutputDeviceName;

	private readonly Func<double> _dashboardVolume;

	private readonly Func<string> _dashboardSoundTheme;

	private readonly Dictionary<string, DateTimeOffset> _lastPlayTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, XboxMetroLauncher.Platform.AudioClip> _clips = new Dictionary<string, XboxMetroLauncher.Platform.AudioClip>(StringComparer.Ordinal);

	private readonly HashSet<string> _loading = new HashSet<string>(StringComparer.Ordinal);

	private readonly object _clipsLock = new object();

	private readonly XboxMetroLauncher.Platform.AudioMixer _mixer = new XboxMetroLauncher.Platform.AudioMixer();

	private static readonly Dictionary<string, string[]> SoundFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["boot-intro"] = new string[1] { "02. Startup (2010).mp3" },
		["startup"] = new string[3] { "startup-after-loading.wav", "startup.wav", "02. Startup (2010).mp3" },
		["notify-popup"] = new string[1] { "notify-popup.wav" },
		["page-left"] = new string[4] { "09. Page Right.mp3", "swipe-left.wav", "08. Page Left.mp3", "tab.wav" },
		["page-right"] = new string[4] { "08. Page Left.mp3", "swipe-right.wav", "09. Page Right.mp3", "tab.wav" },
		["tab"] = new string[2] { "09. Page Right.mp3", "tab.wav" },
		["tab-switch-0"] = new string[2] { "09. Page Right.mp3", "tab.wav" },
		["tab-switch-1"] = new string[2] { "09. Page Right.mp3", "tab.wav" },
		["tab-switch-2"] = new string[2] { "09. Page Right.mp3", "tab.wav" },
		["tab-switch-3"] = new string[2] { "09. Page Right.mp3", "tab.wav" },
		["tab-switch-4"] = new string[2] { "09. Page Right.mp3", "tab.wav" },
		["tab-switch-5"] = new string[2] { "09. Page Right.mp3", "tab.wav" },
		["tab-switch-6"] = new string[2] { "09. Page Right.mp3", "tab.wav" },
		["select"] = new string[3] { "10. Select A.mp3", "13. Select.mp3", "select.wav" },
		["settings-box"] = new string[2] { "11. Select A (Alt).mp3", "10. Select A.mp3" },
		["menu-in"] = new string[4] { "select-into-menu.wav", "select-into-alt.wav", "10. Select A.mp3", "select.wav" },
		["guide-music-sources-load"] = new string[1] { "select-into-menu.wav" },
		["menu-out"] = new string[3] { "select-out-menu.wav", "14. Back.mp3", "back.wav" },
		["activate"] = new string[3] { "10. Select A.mp3", "13. Select.mp3", "select.wav" },
		["back"] = new string[3] { "14. Back.mp3", "15. Back 2.mp3", "back.wav" },
		["focus"] = new string[4] { "tile-hover.wav", "13. Select.mp3", "11. Select A (Alt).mp3", "focus.wav" },
		["hover"] = new string[4] { "tile-hover.wav", "13. Select.mp3", "11. Select A (Alt).mp3", "focus.wav" },
		["guide-open"] = new string[2] { "hud-open.wav", "10. Select A.mp3" },
		["guide-close"] = new string[2] { "hud-close.wav", "14. Back.mp3" },
		["guide-blade-open"] = new string[2] { "blade-open.wav", "hud-open.wav" },
		["guide-blade-switch-1"] = new string[2] { "blade-switch-1.wav", "09. Page Right.mp3" },
		["guide-blade-switch-2"] = new string[2] { "blade-switch-2.wav", "09. Page Right.mp3" },
		["guide-blade-switch-3"] = new string[2] { "blade-switch-3.wav", "09. Page Right.mp3" },
		["guide-blade-switch-4"] = new string[2] { "blade-switch-4.wav", "09. Page Right.mp3" },
		["guide-hover"] = new string[2] { "guide-hover.wav", "13. Select.mp3" },
		["guide-select"] = new string[2] { "guide-select.wav", "10. Select A.mp3" },
		["guide-type-1"] = new string[1] { "8 (xwb).wav" },
		["guide-type-2"] = new string[1] { "29 (xwb).wav" },
		["guide-type-3"] = new string[1] { "31 (xwb).wav" },
		["guide-type-backspace"] = new string[1] { "7 (xwb).wav" },
		["guide-back"] = new string[2] { "guide-back.wav", "14. Back.mp3" }
	};

	private static readonly Dictionary<string, string[]> BladesSoundFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["page-left"] = new string[1] { "blades-switch-live-to-games.wav" },
		["page-right"] = new string[1] { "blades-switch-games-to-media.wav" },
		["tab"] = new string[1] { "blades-switch-live-to-games.wav" },
		["tab-switch-0"] = new string[1] { "blades-switch-bing-to-home.wav" },
		["tab-switch-1"] = new string[1] { "blades-switch-home-to-social.wav" },
		["tab-switch-2"] = new string[1] { "blades-switch-social-to-media.wav" },
		["tab-switch-3"] = new string[1] { "blades-switch-media-to-games.wav" },
		["tab-switch-4"] = new string[1] { "blades-switch-games-to-music.wav" },
		["tab-switch-5"] = new string[1] { "blades-switch-music-to-apps.wav" },
		["tab-switch-6"] = new string[1] { "blades-switch-apps-to-settings.wav" },
		["select"] = new string[1] { "blades-select.wav" },
		["settings-box"] = new string[1] { "blades-select.wav" },
		["menu-in"] = new string[1] { "blades-menu-open.wav" },
		["guide-music-sources-load"] = new string[1] { "blades-menu-open.wav" },
		["menu-out"] = new string[1] { "blades-back.wav" },
		["activate"] = new string[1] { "blades-select.wav" },
		["back"] = new string[1] { "blades-back.wav" },
		["focus"] = new string[1] { "blades-hover.wav" },
		["hover"] = new string[1] { "blades-hover.wav" },
		["guide-open"] = new string[1] { "blades-guide-open.wav" },
		["guide-close"] = new string[1] { "blades-guide-close.wav" },
		["guide-hover"] = new string[1] { "blades-hover.wav" },
		["guide-select"] = new string[1] { "blades-select.wav" },
		["guide-back"] = new string[1] { "blades-back.wav" },
		["guide-blade-open"] = new string[1] { "blades-menu-open.wav" },
		["guide-blade-switch-1"] = new string[1] { "blades-switch-marketplace-to-live.wav" },
		["guide-blade-switch-2"] = new string[1] { "blades-switch-live-to-games.wav" },
		["guide-blade-switch-3"] = new string[1] { "blades-switch-games-to-media.wav" },
		["guide-blade-switch-4"] = new string[1] { "blades-switch-media-to-system.wav" }
	};

	public AudioService(Func<bool> isEnabled, Panel? host = null, Func<string>? selectedOutputDeviceName = null, Func<double>? dashboardVolume = null, Func<string>? dashboardSoundTheme = null)
	{
		_isEnabled = isEnabled;
		_selectedOutputDeviceName = selectedOutputDeviceName ?? ((Func<string>)(() => "Default"));
		_dashboardVolume = dashboardVolume ?? ((Func<double>)(() => 1.0));
		_dashboardSoundTheme = dashboardSoundTheme ?? ((Func<string>)(() => "Metro"));
		Task.Run(() => _mixer.EnsureDevice(SafeDeviceName()));
		PreloadLowLatencyWave("focus");
		PreloadLowLatencyWave("startup");
		PreloadLowLatencyWave("notify-popup");
		PreloadLowLatencyWave("guide-type-1");
		PreloadLowLatencyWave("guide-type-2");
		PreloadLowLatencyWave("guide-type-3");
		PreloadLowLatencyWave("guide-type-backspace");
	}

	public IReadOnlyList<string> GetOutputDeviceNames()
	{
		List<string> list = new List<string> { "Default" };
		try
		{
			Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (string name in XboxMetroLauncher.Platform.AudioMixer.GetDeviceNames())
			{
				seen.TryGetValue(name, out int value);
				seen[name] = value + 1;
				list.Add(value == 0 ? name : $"{name} ({value + 1})");
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "AudioService.GetOutputDeviceNames");
		}
		return list;
	}

	public void Play(string soundName)
	{
		if (!_isEnabled() || IsThrottled(soundName))
		{
			return;
		}
		string? path = ResolveSoundPath(soundName);
		if (path == null)
		{
			return;
		}
		float volume = (float)GetDashboardVolume();
		string device = SafeDeviceName();
		XboxMetroLauncher.Platform.AudioClip? clip;
		lock (_clipsLock)
		{
			_clips.TryGetValue(path, out clip);
		}
		if (clip != null && _mixer.EnsureDevice(device))
		{
			_mixer.Play(clip, volume, soundName);
			return;
		}
		// First use: decode off the UI thread, then play.
		Task.Run(() =>
		{
			XboxMetroLauncher.Platform.AudioClip? loaded = LoadClip(path);
			if (loaded != null && _mixer.EnsureDevice(device))
			{
				_mixer.Play(loaded, volume, soundName);
			}
		});
	}

	private string SafeDeviceName()
	{
		try
		{
			string name = _selectedOutputDeviceName();
			// Strip the " (2)" suffix used to disambiguate identical device names.
			if (name.EndsWith(")", StringComparison.Ordinal) && name.LastIndexOf(" (", StringComparison.Ordinal) is int i && i > 0 && int.TryParse(name[(i + 2)..^1], out _))
			{
				name = name[..i];
			}
			return name;
		}
		catch
		{
			return "Default";
		}
	}

	private XboxMetroLauncher.Platform.AudioClip? LoadClip(string path)
	{
		lock (_clipsLock)
		{
			if (_clips.TryGetValue(path, out XboxMetroLauncher.Platform.AudioClip? cached))
			{
				return cached;
			}
		}
		try
		{
			XboxMetroLauncher.Platform.AudioClip clip = XboxMetroLauncher.Platform.AudioClip.Load(path, XboxMetroLauncher.Platform.AudioMixer.SampleRate);
			lock (_clipsLock)
			{
				_clips[path] = clip;
			}
			return clip;
		}
		catch (Exception exception)
		{
			App.LogException(exception, "AudioService.LoadClip " + path);
			return null;
		}
	}

	private void PreloadLowLatencyWave(string soundName)
	{
		Task.Run(delegate
		{
			try
			{
				string? path = ResolveSoundPath(soundName);
				if (!string.IsNullOrWhiteSpace(path))
				{
					LoadClip(path);
				}
			}
			catch
			{
			}
		});
	}

	public void WarmUp(string soundName)
	{
		PreloadLowLatencyWave(soundName);
	}

	public void TrimCachedResources(bool keepGuideReady)
	{
		lock (_clipsLock)
		{
			foreach (string key in _clips.Keys.ToList())
			{
				string fileName = Path.GetFileName(key);
				if (keepGuideReady && IsGuideSoundFile(fileName))
				{
					continue;
				}
				if (string.Equals(fileName, "tile-hover.wav", StringComparison.OrdinalIgnoreCase) || string.Equals(fileName, "blades-hover.wav", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				_clips.Remove(key);
			}
		}
	}

	private static bool IsGuideSoundFile(string fileName)
	{
		return string.Equals(fileName, "hud-open.wav", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fileName, "hud-close.wav", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fileName, "guide-hover.wav", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fileName, "guide-select.wav", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fileName, "guide-back.wav", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(fileName, "blade-open.wav", StringComparison.OrdinalIgnoreCase)
			|| fileName.StartsWith("blade-switch-", StringComparison.OrdinalIgnoreCase)
			|| fileName.StartsWith("blades-guide-", StringComparison.OrdinalIgnoreCase);
	}

	public void Stop(string soundName)
	{
		_mixer.Stop(soundName);
	}

	private string? ResolveSoundPath(string soundName)
	{
		string[] value;
		Dictionary<string, string[]> soundFiles = IsBladesSoundTheme() ? BladesSoundFiles : SoundFiles;
		string[] array = (soundFiles.TryGetValue(soundName, out value) || SoundFiles.TryGetValue(soundName, out value) ? value : new string[2]
		{
			soundName + ".mp3",
			soundName + ".wav"
		});
		foreach (string item in AppPaths.CandidateRoots().SelectMany((string root) => new string[3]
		{
			Path.Combine(root, "Assets", "Audio", "Sounds"),
			Path.Combine(root, "sounds"),
			Path.Combine(root, "Assets", "Audio")
		}).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			.ToList())
		{
			string[] array2 = array;
			foreach (string path in array2)
			{
				string text = Path.Combine(item, path);
				if (File.Exists(text))
				{
					return text;
				}
			}
		}
		return null;
	}

	private bool IsBladesSoundTheme()
	{
		try
		{
			return string.Equals(_dashboardSoundTheme(), "Blades", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}



	private double GetDashboardVolume()
	{
		try
		{
			return Math.Clamp(_dashboardVolume(), 0.0, 1.0);
		}
		catch
		{
			return 1.0;
		}
	}





	private bool IsThrottled(string soundName)
	{
		TimeSpan timeSpan;
		switch (soundName)
		{
		case "select":
			timeSpan = TimeSpan.FromMilliseconds(80.0);
			break;
		case "menu-in":
		case "menu-out":
			timeSpan = TimeSpan.FromMilliseconds(120.0);
			break;
		case "guide-open":
		case "guide-close":
		case "guide-blade-open":
		case "guide-select":
		case "guide-back":
			timeSpan = TimeSpan.FromMilliseconds(60.0);
			break;
		case "focus":
		case "guide-hover":
		case "guide-blade-switch-1":
		case "guide-blade-switch-2":
		case "guide-blade-switch-3":
		case "guide-blade-switch-4":
			timeSpan = TimeSpan.Zero;
			break;
		case "page-left":
		case "page-right":
		case "tab":
		case "tab-switch-0":
		case "tab-switch-1":
		case "tab-switch-2":
		case "tab-switch-3":
		case "tab-switch-4":
		case "tab-switch-5":
		case "tab-switch-6":
			timeSpan = TimeSpan.FromMilliseconds(150.0);
			break;
		default:
			timeSpan = TimeSpan.Zero;
			break;
		}
		TimeSpan timeSpan2 = timeSpan;
		if (timeSpan2 == TimeSpan.Zero)
		{
			return false;
		}
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		if (_lastPlayTimes.TryGetValue(soundName, out var value) && utcNow - value < timeSpan2)
		{
			return true;
		}
		_lastPlayTimes[soundName] = utcNow;
		return false;
	}
}
