// WPF MediaPlayer and a video surface control, both backed by GStreamer.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using XboxMetroLauncher.Platform;

namespace XboxMetroLauncher.Compat;

public readonly struct NaturalDurationValue
{
	public NaturalDurationValue(TimeSpan? timeSpan)
	{
		HasTimeSpan = timeSpan.HasValue;
		TimeSpan = timeSpan ?? TimeSpan.Zero;
	}

	public bool HasTimeSpan { get; }

	public TimeSpan TimeSpan { get; }
}

/// <summary>WPF System.Windows.Media.MediaPlayer equivalent (audio only). Events are raised on the UI thread.</summary>
public sealed class MediaPlayer : IDisposable
{
	private GstPlayer? _player;

	private double _volume = 0.5;

	private Uri? _source;

	public event EventHandler? MediaOpened;

	public event EventHandler? MediaEnded;

	public event EventHandler<EventArgs>? MediaFailed;

	public Uri? Source => _source;

	public double Volume
	{
		get => _volume;
		set
		{
			_volume = Math.Clamp(value, 0.0, 1.0);
			if (_player != null)
			{
				_player.Volume = _volume;
			}
		}
	}

	public TimeSpan Position
	{
		get => _player?.Position ?? TimeSpan.Zero;
		set
		{
			if (_player != null)
			{
				_player.Position = value;
			}
		}
	}

	public NaturalDurationValue NaturalDuration => new NaturalDurationValue(_player?.Duration);

	public bool HasAudio => _player != null;

	public void Open(Uri source)
	{
		_source = source;
		try
		{
			EnsurePlayer();
			_player!.Open(source.IsFile ? source.LocalPath : source.AbsoluteUri);
			_player.Pause();
			WaitForDurationThenRaiseOpened();
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MediaPlayer.Open");
			Dispatcher.UIThread.Post(() => MediaFailed?.Invoke(this, EventArgs.Empty));
		}
	}

	private void WaitForDurationThenRaiseOpened()
	{
		GstPlayer? player = _player;
		System.Threading.Tasks.Task.Run(async () =>
		{
			for (int i = 0; i < 40 && player != null && player.Duration == null; i++)
			{
				await System.Threading.Tasks.Task.Delay(25).ConfigureAwait(false);
			}
			Dispatcher.UIThread.Post(() =>
			{
				if (ReferenceEquals(player, _player))
				{
					MediaOpened?.Invoke(this, EventArgs.Empty);
				}
			});
		});
	}

	private void EnsurePlayer()
	{
		if (_player != null)
		{
			return;
		}
		_player = new GstPlayer(withVideo: false) { Volume = _volume };
		_player.Ended += () => Dispatcher.UIThread.Post(() => MediaEnded?.Invoke(this, EventArgs.Empty));
		_player.Failed += _ => Dispatcher.UIThread.Post(() => MediaFailed?.Invoke(this, EventArgs.Empty));
	}

	public void Play() => _player?.Play();

	public void Pause() => _player?.Pause();

	public void Stop()
	{
		if (_player == null)
		{
			return;
		}
		_player.Stop();
		_player.Position = TimeSpan.Zero;
	}

	public void Close()
	{
		_player?.Dispose();
		_player = null;
		_source = null;
	}

	public void Dispose() => Close();
}

/// <summary>Renders a video file (used for the boot animation; replaces the WinForms WebBrowser host).</summary>
public sealed class VideoView : Control
{
	private GstPlayer? _player;

	private WriteableBitmap? _bitmap;

	private readonly object _frameLock = new object();

	private byte[]? _pendingFrame;

	private int _pendingWidth;

	private int _pendingHeight;

	private bool _framePosted;

	public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<VideoView>();

	public static readonly StyledProperty<Stretch> StretchProperty = Image.StretchProperty.AddOwner<VideoView>();

	public IBrush? Background
	{
		get => GetValue(BackgroundProperty);
		set => SetValue(BackgroundProperty, value);
	}

	public Stretch Stretch
	{
		get => GetValue(StretchProperty);
		set => SetValue(StretchProperty, value);
	}

	public event EventHandler? MediaEnded;

	public event EventHandler? MediaFailed;

	public bool HasStarted { get; private set; }

	public bool HasEnded { get; private set; }

	public bool HasFirstFrame { get; private set; }

	public void Play(string path, double volume, bool muted)
	{
		Stop();
		HasEnded = false;
		HasFirstFrame = false;
		try
		{
			_player = new GstPlayer(withVideo: true, 1280, 720) { Volume = muted ? 0.0 : volume };
			_player.FrameReady += OnFrame;
			_player.Ended += () => Dispatcher.UIThread.Post(() =>
			{
				HasEnded = true;
				MediaEnded?.Invoke(this, EventArgs.Empty);
			});
			_player.Failed += _ => Dispatcher.UIThread.Post(() =>
			{
				HasEnded = true;
				MediaFailed?.Invoke(this, EventArgs.Empty);
			});
			_player.Open(path);
			_player.Play();
			HasStarted = true;
		}
		catch (Exception exception)
		{
			App.LogException(exception, "VideoView.Play");
			HasEnded = true;
			Dispatcher.UIThread.Post(() => MediaFailed?.Invoke(this, EventArgs.Empty));
		}
	}

	public void Stop()
	{
		GstPlayer? player = _player;
		_player = null;
		if (player != null)
		{
			player.FrameReady -= OnFrame;
			System.Threading.Tasks.Task.Run(player.Dispose);
		}
		HasStarted = false;
	}

	public void ReleaseFrame()
	{
		_bitmap?.Dispose();
		_bitmap = null;
		InvalidateVisual();
	}

	private void OnFrame(nint data, int width, int height, int stride)
	{
		byte[] frame;
		lock (_frameLock)
		{
			int size = width * height * 4;
			if (_pendingFrame == null || _pendingFrame.Length != size)
			{
				_pendingFrame = new byte[size];
			}
			frame = _pendingFrame;
			for (int y = 0; y < height; y++)
			{
				System.Runtime.InteropServices.Marshal.Copy(data + y * stride, frame, y * width * 4, width * 4);
			}
			_pendingWidth = width;
			_pendingHeight = height;
			if (_framePosted)
			{
				return;
			}
			_framePosted = true;
		}
		Dispatcher.UIThread.Post(UploadFrame, DispatcherPriority.Render);
	}

	private void UploadFrame()
	{
		lock (_frameLock)
		{
			_framePosted = false;
			if (_pendingFrame == null)
			{
				return;
			}
			if (_bitmap == null || _bitmap.PixelSize.Width != _pendingWidth || _bitmap.PixelSize.Height != _pendingHeight)
			{
				_bitmap?.Dispose();
				_bitmap = new WriteableBitmap(new PixelSize(_pendingWidth, _pendingHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
			}
			using (ILockedFramebuffer fb = _bitmap.Lock())
			{
				for (int y = 0; y < _pendingHeight; y++)
				{
					System.Runtime.InteropServices.Marshal.Copy(_pendingFrame, y * _pendingWidth * 4, fb.Address + y * fb.RowBytes, _pendingWidth * 4);
				}
			}
		}
		HasFirstFrame = true;
		InvalidateVisual();
	}

	public override void Render(DrawingContext context)
	{
		Rect bounds = new Rect(Bounds.Size);
		if (Background != null)
		{
			context.FillRectangle(Background, bounds);
		}
		if (_bitmap == null)
		{
			return;
		}
		Size source = new Size(_bitmap.PixelSize.Width, _bitmap.PixelSize.Height);
		double scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
		if (Stretch == Stretch.UniformToFill)
		{
			scale = Math.Max(bounds.Width / source.Width, bounds.Height / source.Height);
		}
		Size target = Stretch == Stretch.Fill ? bounds.Size : new Size(source.Width * scale, source.Height * scale);
		Rect destination = new Rect((bounds.Width - target.Width) / 2, (bounds.Height - target.Height) / 2, target.Width, target.Height);
		using (context.PushClip(bounds))
		{
			context.DrawImage(_bitmap, new Rect(source), destination);
		}
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnDetachedFromVisualTree(e);
		Stop();
	}
}
