// Process / window helpers for Linux (EWMH over X11/XWayland + /proc), replacing user32 calls.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace XboxMetroLauncher.Platform;

public static class LinuxWindows
{
	private const string X11 = "libX11.so.6";

	private const int ClientMessage = 33;

	private const long SubstructureRedirectMask = 1L << 20;

	private const long SubstructureNotifyMask = 1L << 19;

	[DllImport(X11)]
	private static extern nint XOpenDisplay(nint name);

	[DllImport(X11)]
	private static extern int XCloseDisplay(nint display);

	[DllImport(X11)]
	private static extern nint XDefaultRootWindow(nint display);

	[DllImport(X11)]
	private static extern nint XInternAtom(nint display, [MarshalAs(UnmanagedType.LPStr)] string name, bool onlyIfExists);

	[DllImport(X11)]
	private static extern int XGetWindowProperty(nint display, nint window, nint property, nint offset, nint length, bool delete, nint reqType, out nint actualType, out int actualFormat, out nuint nitems, out nuint bytesAfter, out nint prop);

	[DllImport(X11)]
	private static extern int XFree(nint data);

	[DllImport(X11)]
	private static extern int XSendEvent(nint display, nint window, bool propagate, nint eventMask, nint eventSend);

	[DllImport(X11)]
	private static extern int XFlush(nint display);

	[DllImport("libc", SetLastError = true)]
	private static extern int kill(int pid, int sig);

	[DllImport(X11)]
	private static extern int XSetInputFocus(nint display, nint focus, int revertTo, nint time);

	[DllImport(X11)]
	private static extern int XRaiseWindow(nint display, nint window);

	/// <summary>Gives X input focus to one of our own windows (used when no window manager is running, e.g. Xvfb/kiosk sessions).</summary>
	public static void ForceInputFocus(nint xid)
	{
		WithDisplay(display =>
		{
			XRaiseWindow(display, xid);
			XSetInputFocus(display, xid, 2, 0);
			XFlush(display);
			return true;
		}, false);
	}

	private static bool _x11Unavailable;

	private static T WithDisplay<T>(Func<nint, T> action, T fallback)
	{
		if (_x11Unavailable || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
		{
			return fallback;
		}
		nint display;
		try
		{
			display = XOpenDisplay(0);
		}
		catch (Exception)
		{
			_x11Unavailable = true;
			return fallback;
		}
		if (display == 0)
		{
			return fallback;
		}
		try
		{
			return action(display);
		}
		catch
		{
			return fallback;
		}
		finally
		{
			XCloseDisplay(display);
		}
	}

	private static nint[] GetWindowArray(nint display, nint window, string property)
	{
		nint atom = XInternAtom(display, property, true);
		if (atom == 0)
		{
			return Array.Empty<nint>();
		}
		if (XGetWindowProperty(display, window, atom, 0, 4096, false, 0, out _, out int format, out nuint count, out _, out nint data) != 0 || data == 0)
		{
			return Array.Empty<nint>();
		}
		try
		{
			if (format != 32 || count == 0)
			{
				return Array.Empty<nint>();
			}
			nint[] result = new nint[(int)count];
			Marshal.Copy(data, result, 0, result.Length);
			return result;
		}
		finally
		{
			XFree(data);
		}
	}

	private static int GetWindowPid(nint display, nint window)
	{
		nint[] values = GetWindowArray(display, window, "_NET_WM_PID");
		return values.Length > 0 ? (int)values[0] : -1;
	}

	private static void SendClientMessage(nint display, nint window, string messageType, params long[] data)
	{
		nint root = XDefaultRootWindow(display);
		nint atom = XInternAtom(display, messageType, false);
		nint ev = Marshal.AllocHGlobal(192);
		try
		{
			for (int i = 0; i < 192; i += 8)
			{
				Marshal.WriteInt64(ev, i, 0);
			}
			Marshal.WriteInt32(ev, 0, ClientMessage);
			Marshal.WriteInt32(ev, 16, 1);
			Marshal.WriteIntPtr(ev, 24, display);
			Marshal.WriteIntPtr(ev, 32, window);
			Marshal.WriteIntPtr(ev, 40, atom);
			Marshal.WriteInt32(ev, 48, 32);
			for (int i = 0; i < data.Length && i < 5; i++)
			{
				Marshal.WriteInt64(ev, 56 + i * 8, data[i]);
			}
			XSendEvent(display, root, false, (nint)(SubstructureRedirectMask | SubstructureNotifyMask), ev);
			XFlush(display);
		}
		finally
		{
			Marshal.FreeHGlobal(ev);
		}
	}

	public static int GetForegroundProcessId()
	{
		return WithDisplay(display =>
		{
			nint root = XDefaultRootWindow(display);
			nint[] active = GetWindowArray(display, root, "_NET_ACTIVE_WINDOW");
			return active.Length > 0 && active[0] != 0 ? GetWindowPid(display, active[0]) : -1;
		}, -1);
	}

	/// <summary>Top-level client windows owned by the process or any of its descendants.</summary>
	public static List<nint> FindWindows(int pid)
	{
		HashSet<int> family = GetProcessTree(pid);
		return WithDisplay(display =>
		{
			List<nint> windows = new List<nint>();
			nint root = XDefaultRootWindow(display);
			foreach (nint window in GetWindowArray(display, root, "_NET_CLIENT_LIST"))
			{
				if (family.Contains(GetWindowPid(display, window)))
				{
					windows.Add(window);
				}
			}
			return windows;
		}, new List<nint>());
	}

	public static bool HasWindow(int pid) => FindWindows(pid).Count > 0;

	public static bool Activate(int pid)
	{
		List<nint> windows = FindWindows(pid);
		if (windows.Count == 0)
		{
			return false;
		}
		return WithDisplay(display =>
		{
			SendClientMessage(display, windows[0], "_NET_ACTIVE_WINDOW", 2, 0, 0);
			return true;
		}, false);
	}

	/// <summary>Politely asks the process to close: EWMH close request for its windows, otherwise SIGTERM.</summary>
	public static bool RequestClose(int pid)
	{
		List<nint> windows = FindWindows(pid);
		if (windows.Count > 0)
		{
			bool sent = WithDisplay(display =>
			{
				foreach (nint window in windows)
				{
					SendClientMessage(display, window, "_NET_CLOSE_WINDOW", 0, 2);
				}
				return true;
			}, false);
			if (sent)
			{
				return true;
			}
		}
		try
		{
			return kill(pid, 15) == 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>Best path describing a game process: Windows .exe path for Proton/Wine games, otherwise /proc/pid/exe.</summary>
	public static string? GetGamePath(Process process)
	{
		string? exe = null;
		try
		{
			exe = process.MainModule?.FileName;
		}
		catch
		{
		}
		string? cmdline = GetCommandLine(process.Id);
		if (!string.IsNullOrWhiteSpace(cmdline))
		{
			foreach (string part in cmdline.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				if (part.StartsWith('/') && (part.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || part.Contains("/steamapps/common/", StringComparison.Ordinal)) && File.Exists(part))
				{
					return part;
				}
			}
		}
		return exe;
	}

	public static string? GetCommandLine(int pid)
	{
		try
		{
			string raw = File.ReadAllText($"/proc/{pid}/cmdline");
			return raw.Replace('\0', ' ').Trim();
		}
		catch
		{
			return null;
		}
	}

	public static HashSet<int> GetProcessTree(int pid)
	{
		HashSet<int> result = new HashSet<int> { pid };
		try
		{
			Dictionary<int, List<int>> children = new Dictionary<int, List<int>>();
			foreach (string dir in Directory.EnumerateDirectories("/proc"))
			{
				if (!int.TryParse(Path.GetFileName(dir), out int child))
				{
					continue;
				}
				try
				{
					string stat = File.ReadAllText(Path.Combine(dir, "stat"));
					int close = stat.LastIndexOf(')');
					string[] fields = stat[(close + 2)..].Split(' ');
					int parent = int.Parse(fields[1]);
					if (!children.TryGetValue(parent, out List<int>? list))
					{
						list = new List<int>();
						children[parent] = list;
					}
					list.Add(child);
				}
				catch
				{
				}
			}
			Queue<int> queue = new Queue<int>();
			queue.Enqueue(pid);
			while (queue.Count > 0)
			{
				int current = queue.Dequeue();
				if (children.TryGetValue(current, out List<int>? list))
				{
					foreach (int child in list)
					{
						if (result.Add(child))
						{
							queue.Enqueue(child);
						}
					}
				}
			}
		}
		catch
		{
		}
		return result;
	}
}
