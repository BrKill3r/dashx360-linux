using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace XboxMetroLauncher.Input;

public sealed class ControllerInputService : IDisposable
{
	private struct XInputState
	{
		public uint PacketNumber;

		public XInputGamepad Gamepad;
	}

	private struct XInputGamepad
	{
		public ushort Buttons;

		public byte LeftTrigger;

		public byte RightTrigger;

		public short LeftThumbX;

		public short LeftThumbY;

		public short RightThumbX;

		public short RightThumbY;
	}

	private enum InputBackend
	{
		None,
		XInput,
		DirectInput
	}

	private readonly record struct DirectInputButtonMap(int Activate, int Back, int Details, int Search, int PreviousTab, int NextTab, int LeftTrigger, int RightTrigger, int SecondaryBack, int Options, int LeftThumbClick, int Guide)
	{
		public static DirectInputButtonMap PlayStation { get; } = new DirectInputButtonMap(1, 2, 0, 3, 4, 5, 6, 7, 8, 9, 10, 12);

		public static DirectInputButtonMap Switch { get; } = new DirectInputButtonMap(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12);

		public static DirectInputButtonMap Generic { get; } = new DirectInputButtonMap(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12);

		public static DirectInputButtonMap FromProductName(string? productName)
		{
			string text = productName ?? string.Empty;
			if (IsPlayStationProduct(text))
			{
				return PlayStation;
			}
			if (text.Contains("switch", StringComparison.OrdinalIgnoreCase) || text.Contains("joy-con", StringComparison.OrdinalIgnoreCase) || text.Contains("joycon", StringComparison.OrdinalIgnoreCase) || text.Contains("pro controller", StringComparison.OrdinalIgnoreCase))
			{
				return Switch;
			}
			return Generic;
		}

		public static bool IsPlayStationProduct(string? productName)
		{
			string text = productName ?? string.Empty;
			if (!text.Contains("playstation", StringComparison.OrdinalIgnoreCase) && !text.Contains("dualshock", StringComparison.OrdinalIgnoreCase) && !text.Contains("dualsense", StringComparison.OrdinalIgnoreCase) && !text.Contains("wireless controller", StringComparison.OrdinalIgnoreCase))
			{
				return text.Contains("sony", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}

		public static bool IsKnownGameControllerProduct(string? productName)
		{
			string text = productName ?? string.Empty;
			if (!IsPlayStationProduct(text) && !text.Contains("xbox", StringComparison.OrdinalIgnoreCase) && !text.Contains("controller", StringComparison.OrdinalIgnoreCase) && !text.Contains("gamepad", StringComparison.OrdinalIgnoreCase) && !text.Contains("switch", StringComparison.OrdinalIgnoreCase) && !text.Contains("joy-con", StringComparison.OrdinalIgnoreCase))
			{
				return text.Contains("joycon", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
	}

	private const int ErrorSuccess = 0;

	private const ushort XinputGamepadDpadUp = 1;

	private const ushort XinputGamepadDpadDown = 2;

	private const ushort XinputGamepadDpadLeft = 4;

	private const ushort XinputGamepadDpadRight = 8;

	private const ushort XinputGamepadStart = 16;

	private const ushort XinputGamepadBack = 32;

	private const ushort XinputGamepadLeftThumb = 64;

	private const ushort XinputGamepadLeftShoulder = 256;

	private const ushort XinputGamepadRightShoulder = 512;

	private const ushort XinputGamepadA = 4096;

	private const ushort XinputGamepadB = 8192;

	private const ushort XinputGamepadX = 16384;

	private const ushort XinputGamepadY = 32768;

	/// <summary>Not part of XInput's public mask (0x0400 is the hidden guide bit used by XInputGetStateEx).</summary>
	private const ushort XinputGamepadGuide = 1024;

	private const short ThumbDeadzone = 16000;

	private const byte TriggerThreshold = 120;

	private const int DirectInputAxisCenter = 32767;

	private const int DirectInputThumbDeadzone = 14000;

	private const int DirectInputPovNeutral = -1;

	private Thread? _thread;

	private readonly Action<DashboardInputAction> _onAction;

	private readonly Func<bool> _isEnabled;

	private readonly Dictionary<DashboardInputAction, DateTimeOffset> _lastMoveTimes = new Dictionary<DashboardInputAction, DateTimeOffset>();

	private ushort _previousButtons;

	private XInputState _previousState;

	private DashboardInputAction? _previousStickAction;

	private DateTimeOffset _lastStickMove = DateTimeOffset.MinValue;

	private static readonly TimeSpan MoveRepeatDelay = TimeSpan.FromMilliseconds(185.0);

	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(16.0);

	private bool _isDispatching;

	private bool _guideButtonDown;

	private InputBackend _activeBackend;

	private nint _controller;

	private nint _joystick;

	private DirectInputButtonMap _joystickButtonMap = DirectInputButtonMap.Generic;

	private DateTimeOffset _nextDeviceScan = DateTimeOffset.MinValue;

	private bool _sdlReady;

	private volatile bool _isRunning;

	/// <summary>Name of the active controller (for diagnostics/UI).</summary>
	public string ActiveControllerName { get; private set; } = string.Empty;

	/// <summary>Raised on the UI thread for every raw state while a passthrough consumer (e.g. YouTube TV) is active.</summary>
	public Func<DashboardInputAction, bool>? Interceptor { get; set; }

	public bool IsRunning => _isRunning;

	public ControllerInputService(Action<DashboardInputAction> onAction, Func<bool>? isEnabled = null)
	{
		_onAction = onAction;
		_isEnabled = isEnabled ?? ((Func<bool>)(() => true));
	}

	public void Start()
	{
		if (_isRunning)
		{
			return;
		}
		_isRunning = true;
		_thread = new Thread(PollLoop)
		{
			IsBackground = true,
			Name = "DashX360 controller (SDL2)"
		};
		_thread.Start();
	}

	public void Dispose()
	{
		_isRunning = false;
		_thread?.Join(500);
	}

	private void PollLoop()
	{
		_sdlReady = XboxMetroLauncher.Platform.Sdl.TryInit(XboxMetroLauncher.Platform.Sdl.SDL_INIT_GAMECONTROLLER | XboxMetroLauncher.Platform.Sdl.SDL_INIT_JOYSTICK | XboxMetroLauncher.Platform.Sdl.SDL_INIT_EVENTS);
		if (!_sdlReady)
		{
			App.LogException(new InvalidOperationException("SDL init failed: " + XboxMetroLauncher.Platform.Sdl.GetError()), "ControllerInputService");
			_isRunning = false;
			return;
		}
		while (_isRunning)
		{
			try
			{
				PollController();
			}
			catch (Exception exception)
			{
				App.LogException(exception, "ControllerInputService.Poll");
				ResetInputState();
			}
			Thread.Sleep(PollInterval);
		}
		CloseDevices();
		XboxMetroLauncher.Platform.Sdl.SDL_QuitSubSystem(XboxMetroLauncher.Platform.Sdl.SDL_INIT_GAMECONTROLLER | XboxMetroLauncher.Platform.Sdl.SDL_INIT_JOYSTICK);
	}

	private void PollController()
	{
		XboxMetroLauncher.Platform.Sdl.SDL_PumpEvents();
		XboxMetroLauncher.Platform.Sdl.SDL_FlushEvents(0, uint.MaxValue);
		XboxMetroLauncher.Platform.Sdl.SDL_GameControllerUpdate();
		if (!_isEnabled())
		{
			ResetInputState();
			return;
		}
		if (TryReadState(out XInputState state))
		{
			ProcessXInputState(state);
		}
		else
		{
			ResetInputState();
		}
	}

	private bool TryReadState(out XInputState state)
	{
		state = default(XInputState);
		EnsureDevice();
		if (_controller != 0)
		{
			if (!XboxMetroLauncher.Platform.Sdl.SDL_GameControllerGetAttached(_controller))
			{
				CloseDevices();
				return false;
			}
			SwitchBackend(InputBackend.XInput);
			state = ReadGameController(_controller);
			return true;
		}
		if (_joystick != 0)
		{
			if (!XboxMetroLauncher.Platform.Sdl.SDL_JoystickGetAttached(_joystick))
			{
				CloseDevices();
				return false;
			}
			SwitchBackend(InputBackend.DirectInput);
			state = ReadJoystick(_joystick);
			return true;
		}
		return false;
	}

	private void EnsureDevice()
	{
		if (_controller != 0 || _joystick != 0 || DateTimeOffset.UtcNow < _nextDeviceScan)
		{
			return;
		}
		_nextDeviceScan = DateTimeOffset.UtcNow.AddSeconds(1.0);
		int count = XboxMetroLauncher.Platform.Sdl.SDL_NumJoysticks();
		for (int i = 0; i < count; i++)
		{
			if (XboxMetroLauncher.Platform.Sdl.SDL_IsGameController(i))
			{
				_controller = XboxMetroLauncher.Platform.Sdl.SDL_GameControllerOpen(i);
				if (_controller != 0)
				{
					ActiveControllerName = XboxMetroLauncher.Platform.Sdl.PtrToString(XboxMetroLauncher.Platform.Sdl.SDL_GameControllerName(_controller));
					return;
				}
			}
		}
		for (int i = 0; i < count; i++)
		{
			nint joystick = XboxMetroLauncher.Platform.Sdl.SDL_JoystickOpen(i);
			if (joystick == 0)
			{
				continue;
			}
			string name = XboxMetroLauncher.Platform.Sdl.PtrToString(XboxMetroLauncher.Platform.Sdl.SDL_JoystickName(joystick));
			if (!DirectInputButtonMap.IsKnownGameControllerProduct(name) && XboxMetroLauncher.Platform.Sdl.SDL_JoystickNumButtons(joystick) < 4)
			{
				XboxMetroLauncher.Platform.Sdl.SDL_JoystickClose(joystick);
				continue;
			}
			_joystick = joystick;
			_joystickButtonMap = DirectInputButtonMap.FromProductName(name);
			ActiveControllerName = name;
			return;
		}
	}

	private void CloseDevices()
	{
		if (_controller != 0)
		{
			XboxMetroLauncher.Platform.Sdl.SDL_GameControllerClose(_controller);
			_controller = 0;
		}
		if (_joystick != 0)
		{
			XboxMetroLauncher.Platform.Sdl.SDL_JoystickClose(_joystick);
			_joystick = 0;
		}
		ActiveControllerName = string.Empty;
	}

	private static XInputState ReadGameController(nint controller)
	{
		ushort buttons = 0;
		void Map(XboxMetroLauncher.Platform.Sdl.Button button, ushort mask)
		{
			if (XboxMetroLauncher.Platform.Sdl.SDL_GameControllerGetButton(controller, button) != 0)
			{
				buttons |= mask;
			}
		}
		Map(XboxMetroLauncher.Platform.Sdl.Button.DpadUp, XinputGamepadDpadUp);
		Map(XboxMetroLauncher.Platform.Sdl.Button.DpadDown, XinputGamepadDpadDown);
		Map(XboxMetroLauncher.Platform.Sdl.Button.DpadLeft, XinputGamepadDpadLeft);
		Map(XboxMetroLauncher.Platform.Sdl.Button.DpadRight, XinputGamepadDpadRight);
		Map(XboxMetroLauncher.Platform.Sdl.Button.Start, XinputGamepadStart);
		Map(XboxMetroLauncher.Platform.Sdl.Button.Back, XinputGamepadBack);
		Map(XboxMetroLauncher.Platform.Sdl.Button.LeftStick, XinputGamepadLeftThumb);
		Map(XboxMetroLauncher.Platform.Sdl.Button.LeftShoulder, XinputGamepadLeftShoulder);
		Map(XboxMetroLauncher.Platform.Sdl.Button.RightShoulder, XinputGamepadRightShoulder);
		Map(XboxMetroLauncher.Platform.Sdl.Button.A, XinputGamepadA);
		Map(XboxMetroLauncher.Platform.Sdl.Button.B, XinputGamepadB);
		Map(XboxMetroLauncher.Platform.Sdl.Button.X, XinputGamepadX);
		Map(XboxMetroLauncher.Platform.Sdl.Button.Y, XinputGamepadY);
		Map(XboxMetroLauncher.Platform.Sdl.Button.Guide, XinputGamepadGuide);
		short ly = XboxMetroLauncher.Platform.Sdl.SDL_GameControllerGetAxis(controller, XboxMetroLauncher.Platform.Sdl.Axis.LeftY);
		short ry = XboxMetroLauncher.Platform.Sdl.SDL_GameControllerGetAxis(controller, XboxMetroLauncher.Platform.Sdl.Axis.RightY);
		return new XInputState
		{
			Gamepad = new XInputGamepad
			{
				Buttons = buttons,
				LeftTrigger = (byte)Math.Clamp(XboxMetroLauncher.Platform.Sdl.SDL_GameControllerGetAxis(controller, XboxMetroLauncher.Platform.Sdl.Axis.TriggerLeft) / 128, 0, 255),
				RightTrigger = (byte)Math.Clamp(XboxMetroLauncher.Platform.Sdl.SDL_GameControllerGetAxis(controller, XboxMetroLauncher.Platform.Sdl.Axis.TriggerRight) / 128, 0, 255),
				LeftThumbX = XboxMetroLauncher.Platform.Sdl.SDL_GameControllerGetAxis(controller, XboxMetroLauncher.Platform.Sdl.Axis.LeftX),
				LeftThumbY = (short)Math.Clamp(-(int)ly, short.MinValue, short.MaxValue),
				RightThumbX = XboxMetroLauncher.Platform.Sdl.SDL_GameControllerGetAxis(controller, XboxMetroLauncher.Platform.Sdl.Axis.RightX),
				RightThumbY = (short)Math.Clamp(-(int)ry, short.MinValue, short.MaxValue)
			}
		};
	}

	private XInputState ReadJoystick(nint joystick)
	{
		DirectInputButtonMap map = _joystickButtonMap;
		ushort buttons = 0;
		void Map(int index, ushort mask)
		{
			if (index >= 0 && XboxMetroLauncher.Platform.Sdl.SDL_JoystickGetButton(joystick, index) != 0)
			{
				buttons |= mask;
			}
		}
		Map(map.Activate, XinputGamepadA);
		Map(map.Back, XinputGamepadB);
		Map(map.Details, XinputGamepadX);
		Map(map.Search, XinputGamepadY);
		Map(map.PreviousTab, XinputGamepadLeftShoulder);
		Map(map.NextTab, XinputGamepadRightShoulder);
		Map(map.SecondaryBack, XinputGamepadBack);
		Map(map.Options, XinputGamepadStart);
		Map(map.LeftThumbClick, XinputGamepadLeftThumb);
		Map(map.Guide, XinputGamepadGuide);
		byte hat = XboxMetroLauncher.Platform.Sdl.SDL_JoystickGetHat(joystick, 0);
		if ((hat & 1) != 0)
		{
			buttons |= XinputGamepadDpadUp;
		}
		if ((hat & 2) != 0)
		{
			buttons |= XinputGamepadDpadRight;
		}
		if ((hat & 4) != 0)
		{
			buttons |= XinputGamepadDpadDown;
		}
		if ((hat & 8) != 0)
		{
			buttons |= XinputGamepadDpadLeft;
		}
		byte lt = (byte)(map.LeftTrigger >= 0 && XboxMetroLauncher.Platform.Sdl.SDL_JoystickGetButton(joystick, map.LeftTrigger) != 0 ? 255 : 0);
		byte rt = (byte)(map.RightTrigger >= 0 && XboxMetroLauncher.Platform.Sdl.SDL_JoystickGetButton(joystick, map.RightTrigger) != 0 ? 255 : 0);
		short y = XboxMetroLauncher.Platform.Sdl.SDL_JoystickGetAxis(joystick, 1);
		return new XInputState
		{
			Gamepad = new XInputGamepad
			{
				Buttons = buttons,
				LeftTrigger = lt,
				RightTrigger = rt,
				LeftThumbX = XboxMetroLauncher.Platform.Sdl.SDL_JoystickGetAxis(joystick, 0),
				LeftThumbY = (short)Math.Clamp(-(int)y, short.MinValue, short.MaxValue)
			}
		};
	}

	private void ProcessXInputState(XInputState state)
	{
		ushort buttons = state.Gamepad.Buttons;
		ushort num = 48;
		if ((buttons & num) == num || (buttons & XinputGamepadGuide) != 0)
		{
			if (!_guideButtonDown)
			{
				DispatchAction(DashboardInputAction.Guide);
			}
			_guideButtonDown = true;
			_previousStickAction = null;
			_previousButtons = buttons;
			_previousState = state;
			return;
		}
		_guideButtonDown = false;
		bool num2 = FireDpadMove(buttons);
		FireOnPress(buttons, 4096, DashboardInputAction.Activate);
		FireOnPress(buttons, 8192, DashboardInputAction.Back);
		FireOnPress(buttons, 16384, DashboardInputAction.Details);
		FireOnPress(buttons, 32768, DashboardInputAction.Search);
		FireOnPress(buttons, 16, DashboardInputAction.Options);
		FireOnPress(buttons, XinputGamepadLeftThumb, DashboardInputAction.LeftThumbClick);
		FireOnPress(buttons, 32, DashboardInputAction.Back);
		FireOnPress(buttons, 256, DashboardInputAction.PreviousTab);
		FireOnPress(buttons, 512, DashboardInputAction.NextTab);
		FireOnTriggerPress(state.Gamepad.LeftTrigger, _previousState.Gamepad.LeftTrigger, DashboardInputAction.LeftTrigger);
		FireOnTriggerPress(state.Gamepad.RightTrigger, _previousState.Gamepad.RightTrigger, DashboardInputAction.RightTrigger);
		DashboardInputAction? stickAction = GetStickAction(state.Gamepad.LeftThumbX, state.Gamepad.LeftThumbY);
		if (!num2 && stickAction.HasValue && ShouldFireStick(stickAction.Value))
		{
			DispatchAction(stickAction.Value);
		}
		_previousStickAction = stickAction;
		_previousButtons = buttons;
		_previousState = state;
	}

	private bool FireDpadMove(ushort buttons)
	{
		DashboardInputAction? dpadAction = GetDpadAction(buttons);
		if (!dpadAction.HasValue)
		{
			return false;
		}
		ushort buttonMask = GetButtonMask(dpadAction.Value);
		FireMoveWhileDown(buttons, buttonMask, dpadAction.Value);
		return true;
	}

	private void FireOnPress(ushort buttons, ushort mask, DashboardInputAction action)
	{
		if ((buttons & mask) != 0 && (_previousButtons & mask) == 0)
		{
			DispatchAction(action);
		}
	}

	private void FireOnTriggerPress(byte value, byte previousValue, DashboardInputAction action)
	{
		if (value >= 120 && previousValue < 120)
		{
			DispatchAction(action);
		}
	}

	private void FireMoveWhileDown(ushort buttons, ushort mask, DashboardInputAction action)
	{
		if ((buttons & mask) != 0)
		{
			if ((_previousButtons & mask) == 0)
			{
				_lastMoveTimes[action] = DateTimeOffset.UtcNow;
			}
			else if (!ShouldRepeat(action))
			{
				return;
			}
			DispatchAction(action);
		}
	}

	private bool ShouldRepeat(DashboardInputAction action)
	{
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		if (_lastMoveTimes.TryGetValue(action, out var value) && utcNow - value < MoveRepeatDelay)
		{
			return false;
		}
		_lastMoveTimes[action] = utcNow;
		return true;
	}

	private bool ShouldFireStick(DashboardInputAction action)
	{
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		if (_previousStickAction != action || utcNow - _lastStickMove >= MoveRepeatDelay)
		{
			_lastStickMove = utcNow;
			return true;
		}
		return false;
	}

	private void DispatchAction(DashboardInputAction action)
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(() => DispatchActionOnUiThread(action), DispatcherPriority.Send);
		}
		else
		{
			DispatchActionOnUiThread(action);
		}
	}

	private static readonly bool InputDebug = Environment.GetEnvironmentVariable("DASHX360_INPUT_DEBUG") == "1";

	private void DispatchActionOnUiThread(DashboardInputAction action)
	{
		if (InputDebug)
		{
			Console.Error.WriteLine($"[input] {action} via {ActiveControllerName} dispatching={_isDispatching}");
		}
		if (_isDispatching)
		{
			return;
		}
		try
		{
			_isDispatching = true;
			if (Interceptor != null && Interceptor(action))
			{
				return;
			}
			_onAction(action);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "ControllerInputService.DispatchAction");
		}
		finally
		{
			_isDispatching = false;
		}
	}

	private static DashboardInputAction? GetDpadAction(ushort buttons)
	{
		bool flag = (buttons & 4) != 0;
		bool flag2 = (buttons & 8) != 0;
		bool flag3 = (buttons & 1) != 0;
		bool flag4 = (buttons & 2) != 0;
		if (flag == flag2 && flag3 == flag4)
		{
			return null;
		}
		if (flag != flag2)
		{
			return (!flag) ? DashboardInputAction.MoveRight : DashboardInputAction.MoveLeft;
		}
		return flag3 ? DashboardInputAction.MoveUp : DashboardInputAction.MoveDown;
	}

	private static ushort GetButtonMask(DashboardInputAction action)
	{
		return action switch
		{
			DashboardInputAction.MoveLeft => 4, 
			DashboardInputAction.MoveRight => 8, 
			DashboardInputAction.MoveUp => 1, 
			DashboardInputAction.MoveDown => 2, 
			_ => 0, 
		};
	}

	private void ResetInputState()
	{
		_previousButtons = 0;
		_previousState = default(XInputState);
		_previousStickAction = null;
		_guideButtonDown = false;
		_activeBackend = InputBackend.None;
		_lastMoveTimes.Clear();
	}

	private static DashboardInputAction? GetStickAction(short x, short y)
	{
		int num = Math.Abs((int)x);
		int num2 = Math.Abs((int)y);
		if (num < 16000 && num2 < 16000)
		{
			return null;
		}
		if (num > num2)
		{
			return (x > 0) ? DashboardInputAction.MoveRight : DashboardInputAction.MoveLeft;
		}
		return (y > 0) ? DashboardInputAction.MoveUp : DashboardInputAction.MoveDown;
	}

	private void SwitchBackend(InputBackend backend)
	{
		if (_activeBackend != backend)
		{
			_activeBackend = backend;
			_previousButtons = 0;
			_previousState = default(XInputState);
				_previousStickAction = null;
			_guideButtonDown = false;
			_lastMoveTimes.Clear();
		}
	}


}
