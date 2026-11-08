// Minimal GStreamer (playbin + appsink) binding used for music playback and the boot video.
// Only non-variadic GLib/GStreamer entry points are used so the P/Invoke is ABI-safe on x86-64.
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace XboxMetroLauncher.Platform;

internal static class GstNative
{
	private const string Gst = "libgstreamer-1.0.so.0";

	private const string GstApp = "libgstapp-1.0.so.0";

	private const string GObject = "libgobject-2.0.so.0";

	public const int GST_STATE_NULL = 1;

	public const int GST_STATE_READY = 2;

	public const int GST_STATE_PAUSED = 3;

	public const int GST_STATE_PLAYING = 4;

	public const int GST_FORMAT_TIME = 3;

	public const int GST_SEEK_FLAG_FLUSH = 1;

	public const int GST_SEEK_FLAG_ACCURATE = 2;

	public const int GST_SEEK_FLAG_KEY_UNIT = 4;

	public const int GST_MESSAGE_EOS = 1 << 0;

	public const int GST_MESSAGE_ERROR = 1 << 1;

	public const int GST_MESSAGE_ASYNC_DONE = 1 << 21;

	public const int GST_MAP_READ = 1;

	[StructLayout(LayoutKind.Sequential)]
	public struct GstMapInfo
	{
		public nint memory;

		public int flags;

		public nint data;

		public nuint size;

		public nuint maxsize;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public nint[] user_data;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
		public nint[] _gst_reserved;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct GValue
	{
		public nuint g_type;

		public long data0;

		public long data1;
	}

	[DllImport(Gst)]
	public static extern bool gst_init_check(nint argc, nint argv, out nint error);

	[DllImport(Gst)]
	public static extern nint gst_element_factory_make([MarshalAs(UnmanagedType.LPUTF8Str)] string factoryname, [MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

	[DllImport(Gst)]
	public static extern int gst_element_set_state(nint element, int state);

	[DllImport(Gst)]
	public static extern int gst_element_get_state(nint element, out int state, out int pending, ulong timeout);

	[DllImport(Gst)]
	public static extern bool gst_element_query_position(nint element, int format, out long cur);

	[DllImport(Gst)]
	public static extern bool gst_element_query_duration(nint element, int format, out long duration);

	[DllImport(Gst)]
	public static extern bool gst_element_seek_simple(nint element, int format, int seekFlags, long seekPos);

	[DllImport(Gst)]
	public static extern nint gst_element_get_bus(nint element);

	[DllImport(Gst)]
	public static extern nint gst_bus_timed_pop_filtered(nint bus, ulong timeout, int types);

	[DllImport(Gst)]
	public static extern void gst_mini_object_unref(nint obj);

	[DllImport(Gst)]
	public static extern void gst_object_unref(nint obj);

	[DllImport(Gst)]
	public static extern void gst_util_set_object_arg(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

	[DllImport(Gst)]
	public static extern nint gst_parse_bin_from_description([MarshalAs(UnmanagedType.LPUTF8Str)] string binDescription, bool ghostUnlinkedPads, out nint error);

	[DllImport(Gst)]
	public static extern nint gst_bin_get_by_name(nint bin, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

	[DllImport(Gst)]
	public static extern nint gst_sample_get_buffer(nint sample);

	[DllImport(Gst)]
	public static extern nint gst_sample_get_caps(nint sample);

	[DllImport(Gst)]
	public static extern nint gst_caps_get_structure(nint caps, uint index);

	[DllImport(Gst)]
	public static extern bool gst_structure_get_int(nint structure, [MarshalAs(UnmanagedType.LPUTF8Str)] string fieldname, out int value);

	[DllImport(Gst)]
	public static extern bool gst_buffer_map(nint buffer, ref GstMapInfo info, int flags);

	[DllImport(Gst)]
	public static extern void gst_buffer_unmap(nint buffer, ref GstMapInfo info);

	[DllImport(Gst)]
	public static extern nint gst_filename_to_uri([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out nint error);

	[DllImport(GstApp)]
	public static extern nint gst_app_sink_try_pull_sample(nint appsink, ulong timeout);

	[DllImport(GObject)]
	public static extern nint g_value_init(ref GValue value, nuint gtype);

	[DllImport(GObject)]
	public static extern void g_value_set_object(ref GValue value, nint vObject);

	[DllImport(GObject)]
	public static extern void g_value_unset(ref GValue value);

	[DllImport(GObject)]
	public static extern void g_object_set_property(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string propertyName, ref GValue value);

	[DllImport(GObject)]
	public static extern nuint gst_element_get_type();

	[DllImport("libglib-2.0.so.0")]
	public static extern void g_free(nint mem);

	private static int _initState;

	public static bool EnsureInitialized()
	{
		if (_initState == 1)
		{
			return true;
		}
		if (_initState == -1)
		{
			return false;
		}
		try
		{
			_initState = gst_init_check(0, 0, out _) ? 1 : -1;
		}
		catch (Exception exception)
		{
			App.LogException(exception, "GStreamer init");
			_initState = -1;
		}
		return _initState == 1;
	}

	public static void SetObjectProperty(nint target, string name, nint value)
	{
		GValue gvalue = default;
		g_value_init(ref gvalue, 80); // G_TYPE_OBJECT
		g_value_set_object(ref gvalue, value);
		g_object_set_property(target, name, ref gvalue);
		g_value_unset(ref gvalue);
	}

	public static string ToUri(string pathOrUri)
	{
		if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
		{
			return uri.AbsoluteUri;
		}
		string path = uri != null && uri.IsFile ? uri.LocalPath : System.IO.Path.GetFullPath(pathOrUri);
		nint native = gst_filename_to_uri(path, out _);
		if (native == 0)
		{
			return new Uri(path).AbsoluteUri;
		}
		string result = Marshal.PtrToStringUTF8(native) ?? new Uri(path).AbsoluteUri;
		g_free(native);
		return result;
	}
}

/// <summary>A playbin wrapper. Events are raised on a background thread.</summary>
internal sealed class GstPlayer : IDisposable
{
	private readonly object _sync = new object();

	private nint _playbin;

	private nint _bus;

	private nint _appsink;

	// Software volume stage. playbin's own "volume" is forwarded to the PulseAudio/PipeWire stream volume, which
	// WirePlumber persists per application name: a fade-out to 0 then muted every DashX360 stream (SDL sounds too).
	private nint _volumeElement;

	private Thread? _busThread;

	private Thread? _videoThread;

	private volatile bool _running;

	private double _volume = 1.0;

	private bool _disposed;

	public event Action? Ended;

	public event Action<string>? Failed;

	/// <summary>Raised with (BGRA pixels, width, height, stride). Buffer is only valid during the call.</summary>
	public event Action<nint, int, int, int>? FrameReady;

	public bool HasVideoSink { get; }

	public GstPlayer(bool withVideo, int videoWidth = 0, int videoHeight = 0)
	{
		HasVideoSink = withVideo;
		if (!GstNative.EnsureInitialized())
		{
			throw new InvalidOperationException("GStreamer is not available.");
		}
		_playbin = GstNative.gst_element_factory_make("playbin", null);
		if (_playbin == 0)
		{
			throw new InvalidOperationException("GStreamer playbin element is missing (install gstreamer1.0-plugins-base).");
		}
		if (withVideo)
		{
			string scale = videoWidth > 0 && videoHeight > 0 ? $",width={videoWidth},height={videoHeight},pixel-aspect-ratio=1/1" : string.Empty;
			nint bin = GstNative.gst_parse_bin_from_description($"videoconvert ! videoscale ! video/x-raw,format=BGRA{scale} ! appsink name=dashsink max-buffers=2 drop=true sync=true", true, out _);
			if (bin != 0)
			{
				_appsink = GstNative.gst_bin_get_by_name(bin, "dashsink");
				GstNative.SetObjectProperty(_playbin, "video-sink", bin);
			}
		}
		else
		{
			nint fakeVideo = GstNative.gst_element_factory_make("fakesink", null);
			if (fakeVideo != 0)
			{
				GstNative.SetObjectProperty(_playbin, "video-sink", fakeVideo);
			}
		}
		_volumeElement = GstNative.gst_element_factory_make("volume", "dashvolume");
		if (_volumeElement != 0)
		{
			GstNative.SetObjectProperty(_playbin, "audio-filter", _volumeElement);
		}
		// Keep the stream volume at 100% (this also repairs a 0% volume WirePlumber may have saved for the app).
		GstNative.gst_util_set_object_arg(_playbin, "volume", "1");
		GstNative.gst_util_set_object_arg(_playbin, "mute", "false");
		_bus = GstNative.gst_element_get_bus(_playbin);
	}

	public void Open(string pathOrUri)
	{
		lock (_sync)
		{
			ThrowIfDisposed();
			GstNative.gst_element_set_state(_playbin, GstNative.GST_STATE_NULL);
			GstNative.gst_util_set_object_arg(_playbin, "uri", GstNative.ToUri(pathOrUri));
			ApplyVolume();
			StartThreads();
		}
	}

	public void Play()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}
			GstNative.gst_element_set_state(_playbin, GstNative.GST_STATE_PLAYING);
		}
	}

	public void Pause()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}
			GstNative.gst_element_set_state(_playbin, GstNative.GST_STATE_PAUSED);
		}
	}

	public void Stop()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}
			GstNative.gst_element_set_state(_playbin, GstNative.GST_STATE_READY);
		}
	}

	public double Volume
	{
		get => _volume;
		set
		{
			_volume = Math.Clamp(value, 0.0, 1.0);
			lock (_sync)
			{
				if (!_disposed)
				{
					ApplyVolume();
				}
			}
		}
	}

	private void ApplyVolume()
	{
		string value = _volume.ToString("0.###", CultureInfo.InvariantCulture);
		if (_volumeElement != 0)
		{
			GstNative.gst_util_set_object_arg(_volumeElement, "volume", value);
		}
		else
		{
			GstNative.gst_util_set_object_arg(_playbin, "volume", value);
		}
	}

	public TimeSpan Position
	{
		get
		{
			lock (_sync)
			{
				if (_disposed || !GstNative.gst_element_query_position(_playbin, GstNative.GST_FORMAT_TIME, out long ns) || ns < 0)
				{
					return TimeSpan.Zero;
				}
				return TimeSpan.FromTicks(ns / 100);
			}
		}
		set
		{
			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}
				GstNative.gst_element_seek_simple(_playbin, GstNative.GST_FORMAT_TIME, GstNative.GST_SEEK_FLAG_FLUSH | GstNative.GST_SEEK_FLAG_KEY_UNIT, Math.Max(0, value.Ticks) * 100);
			}
		}
	}

	public TimeSpan? Duration
	{
		get
		{
			lock (_sync)
			{
				if (_disposed || !GstNative.gst_element_query_duration(_playbin, GstNative.GST_FORMAT_TIME, out long ns) || ns <= 0)
				{
					return null;
				}
				return TimeSpan.FromTicks(ns / 100);
			}
		}
	}

	private void StartThreads()
	{
		if (_running)
		{
			return;
		}
		_running = true;
		_busThread = new Thread(BusLoop) { IsBackground = true, Name = "gst-bus" };
		_busThread.Start();
		if (_appsink != 0)
		{
			_videoThread = new Thread(VideoLoop) { IsBackground = true, Name = "gst-video" };
			_videoThread.Start();
		}
	}

	private void BusLoop()
	{
		while (_running)
		{
			nint message = GstNative.gst_bus_timed_pop_filtered(_bus, 100_000_000UL, GstNative.GST_MESSAGE_EOS | GstNative.GST_MESSAGE_ERROR);
			if (message == 0)
			{
				continue;
			}
			int type = Marshal.ReadInt32(message, 64); // GstMessage.type follows GstMiniObject (64 bytes on x86-64)
			GstNative.gst_mini_object_unref(message);
			try
			{
				if ((type & GstNative.GST_MESSAGE_ERROR) != 0)
				{
					Failed?.Invoke("GStreamer playback error");
				}
				else
				{
					Ended?.Invoke();
				}
			}
			catch (Exception exception)
			{
				App.LogException(exception, "GstPlayer.Bus");
			}
		}
	}

	private void VideoLoop()
	{
		while (_running)
		{
			nint sample = GstNative.gst_app_sink_try_pull_sample(_appsink, 100_000_000UL);
			if (sample == 0)
			{
				continue;
			}
			try
			{
				nint caps = GstNative.gst_sample_get_caps(sample);
				nint structure = caps != 0 ? GstNative.gst_caps_get_structure(caps, 0) : 0;
				if (structure == 0 || !GstNative.gst_structure_get_int(structure, "width", out int width) || !GstNative.gst_structure_get_int(structure, "height", out int height))
				{
					continue;
				}
				nint buffer = GstNative.gst_sample_get_buffer(sample);
				GstNative.GstMapInfo map = new GstNative.GstMapInfo { user_data = new nint[4], _gst_reserved = new nint[4] };
				if (buffer != 0 && GstNative.gst_buffer_map(buffer, ref map, GstNative.GST_MAP_READ))
				{
					try
					{
						int stride = (int)((long)map.size / Math.Max(1, height));
						FrameReady?.Invoke(map.data, width, height, stride);
					}
					finally
					{
						GstNative.gst_buffer_unmap(buffer, ref map);
					}
				}
			}
			catch (Exception exception)
			{
				App.LogException(exception, "GstPlayer.Video");
			}
			finally
			{
				GstNative.gst_mini_object_unref(sample);
			}
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	public void Dispose()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			_running = false;
		}
		_busThread?.Join(500);
		_videoThread?.Join(500);
		GstNative.gst_element_set_state(_playbin, GstNative.GST_STATE_NULL);
		if (_appsink != 0)
		{
			GstNative.gst_object_unref(_appsink);
		}
		if (_bus != 0)
		{
			GstNative.gst_object_unref(_bus);
		}
		GstNative.gst_object_unref(_playbin);
		_playbin = 0;
		_volumeElement = 0;
	}
}
