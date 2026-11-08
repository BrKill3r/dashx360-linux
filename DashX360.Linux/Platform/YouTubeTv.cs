// YouTube TV on Linux: a kiosk browser window with a TV user agent, driven by the controller through a
// uinput virtual keyboard (replacement for the embedded WebView2 of the Windows build).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace XboxMetroLauncher.Platform;

/// <summary>Virtual keyboard through /dev/uinput (needs the "input" group or a udev rule for /dev/uinput).</summary>
public sealed class UInputKeyboard : IDisposable
{
	public const ushort KeyEsc = 1;

	public const ushort KeyBackspace = 14;

	public const ushort KeyEnter = 28;

	public const ushort KeySlash = 53;

	public const ushort KeyUp = 103;

	public const ushort KeyLeft = 105;

	public const ushort KeyRight = 106;

	public const ushort KeyDown = 108;

	public const ushort KeyPageUp = 104;

	public const ushort KeyPageDown = 109;

	public const ushort KeySpace = 57;

	private const uint UI_SET_EVBIT = 0x40045564;

	private const uint UI_SET_KEYBIT = 0x40045565;

	private const uint UI_DEV_CREATE = 0x5501;

	private const uint UI_DEV_DESTROY = 0x5502;

	private const int O_WRONLY = 1;

	private const int O_NONBLOCK = 0x800;

	[DllImport("libc", SetLastError = true)]
	private static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

	[DllImport("libc", SetLastError = true)]
	private static extern int close(int fd);

	[DllImport("libc", SetLastError = true)]
	private static extern int ioctl(int fd, uint request, int value);

	[DllImport("libc", SetLastError = true)]
	private static extern nint write(int fd, byte[] buffer, nint count);

	private int _fd = -1;

	public static UInputKeyboard? TryCreate()
	{
		UInputKeyboard keyboard = new UInputKeyboard();
		return keyboard.Initialize() ? keyboard : null;
	}

	private bool Initialize()
	{
		try
		{
			_fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
			if (_fd < 0)
			{
				return false;
			}
			ioctl(_fd, UI_SET_EVBIT, 1); // EV_KEY
			foreach (ushort key in new[] { KeyEsc, KeyBackspace, KeyEnter, KeySlash, KeyUp, KeyLeft, KeyRight, KeyDown, KeyPageUp, KeyPageDown, KeySpace })
			{
				ioctl(_fd, UI_SET_KEYBIT, key);
			}
			byte[] device = new byte[1116];
			byte[] name = System.Text.Encoding.ASCII.GetBytes("DashX360 controller keyboard");
			Array.Copy(name, device, Math.Min(name.Length, 79));
			BitConverter.GetBytes((ushort)0x06).CopyTo(device, 80); // BUS_VIRTUAL
			BitConverter.GetBytes((ushort)0x1209).CopyTo(device, 82);
			BitConverter.GetBytes((ushort)0x0360).CopyTo(device, 84);
			BitConverter.GetBytes((ushort)1).CopyTo(device, 86);
			if (write(_fd, device, device.Length) != device.Length || ioctl(_fd, UI_DEV_CREATE, 0) < 0)
			{
				close(_fd);
				_fd = -1;
				return false;
			}
			Thread.Sleep(150); // let udev/compositor pick up the new device
			return true;
		}
		catch
		{
			return false;
		}
	}

	public void Tap(ushort key)
	{
		if (_fd < 0)
		{
			return;
		}
		Emit(1, key, 1);
		Emit(0, 0, 0);
		Emit(1, key, 0);
		Emit(0, 0, 0);
	}

	private void Emit(ushort type, ushort code, int value)
	{
		byte[] ev = new byte[24];
		BitConverter.GetBytes(type).CopyTo(ev, 16);
		BitConverter.GetBytes(code).CopyTo(ev, 18);
		BitConverter.GetBytes(value).CopyTo(ev, 20);
		write(_fd, ev, ev.Length);
	}

	public void Dispose()
	{
		if (_fd >= 0)
		{
			ioctl(_fd, UI_DEV_DESTROY, 0);
			close(_fd);
			_fd = -1;
		}
	}
}

public sealed class YouTubeTvSession : IDisposable
{
	public const string Url = "https://www.youtube.com/tv";

	public const string UserAgent = "Mozilla/5.0 (SMART-TV; Linux; Tizen 6.0) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/4.0 TV Safari/537.36";

	private Process? _process;

	private UInputKeyboard? _keyboard;

	public event EventHandler? Exited;

	public bool IsRunning => _process is { HasExited: false };

	public bool HasVirtualKeyboard => _keyboard != null;

	public string BrowserName { get; private set; } = string.Empty;

	public bool Start()
	{
		if (IsRunning)
		{
			return true;
		}
		_keyboard ??= UInputKeyboard.TryCreate();
		ProcessStartInfo? info = CreateChromiumInfo() ?? CreateFirefoxInfo();
		if (info == null)
		{
			return false;
		}
		_process = Process.Start(info);
		if (_process == null)
		{
			return false;
		}
		_process.EnableRaisingEvents = true;
		_process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
		return true;
	}

	private ProcessStartInfo? CreateChromiumInfo()
	{
		foreach (string name in new[] { "google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "microsoft-edge", "brave-browser" })
		{
			string? exe = LinuxApps.FindExecutable(name);
			if (exe == null)
			{
				continue;
			}
			string profile = Path.Combine(XboxMetroLauncher.Utilities.AppPaths.XdgDataFolder, "youtube-tv-chromium");
			Directory.CreateDirectory(profile);
			ProcessStartInfo info = new ProcessStartInfo(exe) { UseShellExecute = false };
			foreach (string arg in new[] { "--user-data-dir=" + profile, "--user-agent=" + UserAgent, "--kiosk", "--app=" + Url, "--no-first-run", "--noerrdialogs", "--disable-translate", "--autoplay-policy=no-user-gesture-required", "--ozone-platform-hint=auto" })
			{
				info.ArgumentList.Add(arg);
			}
			BrowserName = name;
			return info;
		}
		return null;
	}

	private ProcessStartInfo? CreateFirefoxInfo()
	{
		string? exe = LinuxApps.FindExecutable("firefox") ?? LinuxApps.FindExecutable("firefox-esr");
		if (exe == null)
		{
			return null;
		}
		// Snap Firefox can only read profiles under ~/snap/firefox/common (hidden home folders are blocked).
		bool isSnap = Directory.Exists("/snap/firefox") || exe.StartsWith("/snap/", StringComparison.Ordinal);
		string profile = isSnap ? Path.Combine(LinuxApps.Home, "snap", "firefox", "common", "dashx360-youtube-tv") : Path.Combine(XboxMetroLauncher.Utilities.AppPaths.XdgDataFolder, "youtube-tv-firefox");
		Directory.CreateDirectory(profile);
		File.WriteAllText(Path.Combine(profile, "user.js"), string.Join("\n", new[]
		{
			"user_pref(\"general.useragent.override\", \"" + UserAgent + "\");",
			"user_pref(\"browser.shell.checkDefaultBrowser\", false);",
			"user_pref(\"browser.startup.homepage_override.mstone\", \"ignore\");",
			"user_pref(\"browser.aboutwelcome.enabled\", false);",
			"user_pref(\"datareporting.policy.firstRunURL\", \"\");",
			"user_pref(\"media.autoplay.default\", 0);",
			"user_pref(\"browser.sessionstore.resume_from_crash\", false);",
			"user_pref(\"toolkit.legacyUserProfileCustomizations.stylesheets\", true);",
			""
		}));
		ProcessStartInfo info = new ProcessStartInfo(exe) { UseShellExecute = false };
		foreach (string arg in new[] { "--profile", profile, "--no-remote", "--new-instance", "--kiosk", Url })
		{
			info.ArgumentList.Add(arg);
		}
		BrowserName = "firefox";
		return info;
	}

	public void SendKey(ushort key)
	{
		_keyboard?.Tap(key);
	}

	public void Stop()
	{
		Process? process = _process;
		_process = null;
		if (process == null)
		{
			return;
		}
		try
		{
			if (!process.HasExited)
			{
				LinuxWindows.RequestClose(process.Id);
				if (!process.WaitForExit(1500))
				{
					process.Kill(entireProcessTree: true);
				}
			}
		}
		catch
		{
		}
		process.Dispose();
	}

	public void Dispose()
	{
		Stop();
		_keyboard?.Dispose();
		_keyboard = null;
	}
}
