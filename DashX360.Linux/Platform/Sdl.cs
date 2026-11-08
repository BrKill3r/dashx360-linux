// Minimal SDL2 binding (system libSDL2) for game controllers and audio output.
using System;
using System.Runtime.InteropServices;

namespace XboxMetroLauncher.Platform;

internal static class Sdl
{
	private const string Lib = "libSDL2-2.0.so.0";

	public const uint SDL_INIT_AUDIO = 0x00000010;

	public const uint SDL_INIT_JOYSTICK = 0x00000200;

	public const uint SDL_INIT_GAMECONTROLLER = 0x00002000;

	public const uint SDL_INIT_EVENTS = 0x00004000;

	public const ushort AUDIO_F32SYS = 0x8120;

	public enum Button
	{
		A = 0,
		B = 1,
		X = 2,
		Y = 3,
		Back = 4,
		Guide = 5,
		Start = 6,
		LeftStick = 7,
		RightStick = 8,
		LeftShoulder = 9,
		RightShoulder = 10,
		DpadUp = 11,
		DpadDown = 12,
		DpadLeft = 13,
		DpadRight = 14
	}

	public enum Axis
	{
		LeftX = 0,
		LeftY = 1,
		RightX = 2,
		RightY = 3,
		TriggerLeft = 4,
		TriggerRight = 5
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void AudioCallback(nint userdata, nint stream, int len);

	[StructLayout(LayoutKind.Sequential)]
	public struct AudioSpec
	{
		public int freq;

		public ushort format;

		public byte channels;

		public byte silence;

		public ushort samples;

		public ushort padding;

		public uint size;

		public nint callback;

		public nint userdata;
	}

	[DllImport(Lib)]
	public static extern int SDL_InitSubSystem(uint flags);

	[DllImport(Lib)]
	public static extern void SDL_QuitSubSystem(uint flags);

	[DllImport(Lib)]
	public static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

	[DllImport(Lib)]
	public static extern nint SDL_GetError();

	[DllImport(Lib)]
	public static extern void SDL_PumpEvents();

	[DllImport(Lib)]
	public static extern void SDL_FlushEvents(uint minType, uint maxType);

	[DllImport(Lib)]
	public static extern int SDL_NumJoysticks();

	[DllImport(Lib)]
	public static extern bool SDL_IsGameController(int joystickIndex);

	[DllImport(Lib)]
	public static extern nint SDL_GameControllerOpen(int joystickIndex);

	[DllImport(Lib)]
	public static extern void SDL_GameControllerClose(nint controller);

	[DllImport(Lib)]
	public static extern bool SDL_GameControllerGetAttached(nint controller);

	[DllImport(Lib)]
	public static extern byte SDL_GameControllerGetButton(nint controller, Button button);

	[DllImport(Lib)]
	public static extern short SDL_GameControllerGetAxis(nint controller, Axis axis);

	[DllImport(Lib)]
	public static extern nint SDL_GameControllerName(nint controller);

	[DllImport(Lib)]
	public static extern void SDL_GameControllerUpdate();

	[DllImport(Lib)]
	public static extern nint SDL_JoystickOpen(int index);

	[DllImport(Lib)]
	public static extern void SDL_JoystickClose(nint joystick);

	[DllImport(Lib)]
	public static extern int SDL_JoystickNumButtons(nint joystick);

	[DllImport(Lib)]
	public static extern byte SDL_JoystickGetButton(nint joystick, int button);

	[DllImport(Lib)]
	public static extern short SDL_JoystickGetAxis(nint joystick, int axis);

	[DllImport(Lib)]
	public static extern byte SDL_JoystickGetHat(nint joystick, int hat);

	[DllImport(Lib)]
	public static extern bool SDL_JoystickGetAttached(nint joystick);

	[DllImport(Lib)]
	public static extern nint SDL_JoystickName(nint joystick);

	[DllImport(Lib)]
	public static extern int SDL_GetNumAudioDevices(int iscapture);

	[DllImport(Lib)]
	public static extern nint SDL_GetAudioDeviceName(int index, int iscapture);

	[DllImport(Lib)]
	public static extern uint SDL_OpenAudioDevice([MarshalAs(UnmanagedType.LPUTF8Str)] string? device, int iscapture, ref AudioSpec desired, out AudioSpec obtained, int allowedChanges);

	[DllImport(Lib)]
	public static extern void SDL_PauseAudioDevice(uint dev, int pauseOn);

	[DllImport(Lib)]
	public static extern void SDL_CloseAudioDevice(uint dev);

	public static string GetError() => Marshal.PtrToStringUTF8(SDL_GetError()) ?? string.Empty;

	public static string PtrToString(nint ptr) => ptr == 0 ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;

	private static readonly object InitLock = new object();

	private static bool _hintsApplied;

	public static bool TryInit(uint flags)
	{
		lock (InitLock)
		{
			try
			{
				if (!_hintsApplied)
				{
					_hintsApplied = true;
					SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
					SDL_SetHint("SDL_NO_SIGNAL_HANDLERS", "1");
					SDL_SetHint("SDL_APP_NAME", "DashX360");
					SDL_SetHint("SDL_AUDIO_DEVICE_APP_NAME", "DashX360");
					SDL_SetHint("SDL_AUDIO_DEVICE_STREAM_NAME", "Dashboard sounds");
					SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS4", "1");
					SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5", "1");
				}
				return SDL_InitSubSystem(flags) == 0;
			}
			catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
			{
				App.LogException(exception, "SDL2 not available (install libsdl2-2.0-0)");
				return false;
			}
		}
	}
}
