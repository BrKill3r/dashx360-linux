using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace XboxMetroLauncher.Input;

/// <summary>
/// Guide hotkey (Super + Left Shift + Left Ctrl).
/// Linux/Wayland has no global hotkey API, so keyboards are read directly from evdev (/dev/input/event*, requires the
/// "input" group). When evdev is not readable the combination still works while a DashX360 window is focused.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
	private const int EvKey = 1;

	private const int KeyLeftCtrl = 29;

	private const int KeyLeftShift = 42;

	private const int KeyLeftMeta = 125;

	private const int KeyRightMeta = 126;

	private readonly List<Thread> _readers = new List<Thread>();

	private readonly List<FileStream> _streams = new List<FileStream>();

	private readonly object _sync = new object();

	private volatile bool _running;

	private bool _leftShiftDown;

	private bool _leftCtrlDown;

	private bool _winDown;

	private bool _hotkeyDown;

	private bool _registered;

	private DateTimeOffset _lastHotkeyRaised = DateTimeOffset.MinValue;

	public event EventHandler? HotkeyPressed;

	public bool UsesGlobalKeyboard { get; private set; }

	public void Register(Window window)
	{
		RegisterWindow(window);
		if (_registered)
		{
			return;
		}
		_registered = true;
		_running = true;
		foreach (string device in FindKeyboardDevices())
		{
			try
			{
				FileStream stream = new FileStream(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.None);
				_streams.Add(stream);
				Thread reader = new Thread(() => ReadLoop(stream))
				{
					IsBackground = true,
					Name = "DashX360 evdev " + Path.GetFileName(device)
				};
				_readers.Add(reader);
				reader.Start();
				UsesGlobalKeyboard = true;
			}
			catch (UnauthorizedAccessException)
			{
			}
			catch (Exception exception)
			{
				App.LogException(exception, "GlobalHotkeyService.Open " + device);
			}
		}
	}

	/// <summary>In-window fallback (also used for secondary windows such as the Guide).</summary>
	public void RegisterWindow(Window window)
	{
		window.AddHandler(InputElement.KeyDownEvent, OnWindowKey, RoutingStrategies.Tunnel, handledEventsToo: true);
		window.AddHandler(InputElement.KeyUpEvent, OnWindowKey, RoutingStrategies.Tunnel, handledEventsToo: true);
	}

	private void OnWindowKey(object? sender, KeyEventArgs e)
	{
		if (UsesGlobalKeyboard)
		{
			return;
		}
		bool down = e.RoutedEvent == InputElement.KeyDownEvent;
		switch (e.Key)
		{
		case Key.LeftShift:
			UpdateModifierState(KeyLeftShift, down);
			break;
		case Key.LeftCtrl:
			UpdateModifierState(KeyLeftCtrl, down);
			break;
		case Key.LWin:
		case Key.RWin:
			UpdateModifierState(KeyLeftMeta, down);
			break;
		default:
			if (down && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
			{
				RaiseHotkeyPressed();
			}
			break;
		}
	}

	private static IEnumerable<string> FindKeyboardDevices()
	{
		List<string> devices = new List<string>();
		try
		{
			string[] blocks = File.ReadAllText("/proc/bus/input/devices").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
			foreach (string block in blocks)
			{
				string name = string.Empty;
				string handlers = string.Empty;
				string keyBits = string.Empty;
				foreach (string line in block.Split('\n'))
				{
					if (line.StartsWith("N: Name=", StringComparison.Ordinal))
					{
						name = line[8..].Trim('"');
					}
					else if (line.StartsWith("H: Handlers=", StringComparison.Ordinal))
					{
						handlers = line[12..];
					}
					else if (line.StartsWith("B: KEY=", StringComparison.Ordinal))
					{
						keyBits = line[7..];
					}
				}
				// Real keyboards expose many key bits (letters, modifiers); skip power buttons, video bus, etc.
				if (!handlers.Contains("kbd", StringComparison.Ordinal) || keyBits.Split(' ').Length < 4 || name.Contains("ydotool", StringComparison.OrdinalIgnoreCase) || name.Contains("DashX360", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				foreach (string handler in handlers.Split(' ', StringSplitOptions.RemoveEmptyEntries))
				{
					if (handler.StartsWith("event", StringComparison.Ordinal))
					{
						devices.Add("/dev/input/" + handler);
					}
				}
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "GlobalHotkeyService.FindKeyboardDevices");
		}
		return devices;
	}

	private void ReadLoop(FileStream stream)
	{
		byte[] buffer = new byte[24 * 16];
		try
		{
			while (_running)
			{
				int read = stream.Read(buffer, 0, buffer.Length);
				if (read <= 0)
				{
					break;
				}
				for (int offset = 0; offset + 24 <= read; offset += 24)
				{
					ushort type = BitConverter.ToUInt16(buffer, offset + 16);
					ushort code = BitConverter.ToUInt16(buffer, offset + 18);
					int value = BitConverter.ToInt32(buffer, offset + 20);
					if (type == EvKey && value != 2)
					{
						lock (_sync)
						{
							UpdateModifierState(code, value == 1);
						}
					}
				}
			}
		}
		catch (Exception) when (!_running)
		{
		}
		catch (Exception exception)
		{
			App.LogException(exception, "GlobalHotkeyService.ReadLoop");
		}
	}

	private void UpdateModifierState(int key, bool isDown)
	{
		switch (key)
		{
		case KeyLeftShift:
			_leftShiftDown = isDown;
			break;
		case KeyLeftCtrl:
			_leftCtrlDown = isDown;
			break;
		case KeyLeftMeta:
		case KeyRightMeta:
			_winDown = isDown;
			break;
		}
		if (_leftShiftDown && _leftCtrlDown && _winDown)
		{
			if (!_hotkeyDown)
			{
				_hotkeyDown = true;
				Dispatcher.UIThread.Post(RaiseHotkeyPressed);
			}
		}
		else
		{
			_hotkeyDown = false;
		}
	}

	private void RaiseHotkeyPressed()
	{
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		if (!(utcNow - _lastHotkeyRaised < TimeSpan.FromMilliseconds(350.0)))
		{
			_lastHotkeyRaised = utcNow;
			HotkeyPressed?.Invoke(this, EventArgs.Empty);
		}
	}

	public void Dispose()
	{
		_running = false;
		foreach (FileStream stream in _streams)
		{
			try
			{
				stream.Dispose();
			}
			catch
			{
			}
		}
		_streams.Clear();
	}
}
