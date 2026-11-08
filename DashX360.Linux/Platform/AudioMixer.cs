// Low-latency sound effect mixer on SDL2 audio (PipeWire/PulseAudio/ALSA) with WAV + MP3 decoding.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace XboxMetroLauncher.Platform;

/// <summary>Decoded PCM clip, interleaved stereo float at the mixer rate.</summary>
public sealed class AudioClip
{
	public AudioClip(float[] samples)
	{
		Samples = samples;
	}

	public float[] Samples { get; }

	public int Frames => Samples.Length / 2;

	public static AudioClip Load(string path, int targetRate)
	{
		string extension = Path.GetExtension(path).ToLowerInvariant();
		(float[] data, int channels, int rate) = extension == ".mp3" ? DecodeMp3(path) : DecodeWav(path);
		return new AudioClip(ToStereo(data, channels, rate, targetRate));
	}

	private static (float[] Data, int Channels, int Rate) DecodeMp3(string path)
	{
		using NLayer.MpegFile mpeg = new NLayer.MpegFile(path);
		List<float> samples = new List<float>(1 << 18);
		float[] buffer = new float[8192];
		int read;
		while ((read = mpeg.ReadSamples(buffer, 0, buffer.Length)) > 0)
		{
			for (int i = 0; i < read; i++)
			{
				samples.Add(buffer[i]);
			}
		}
		return (samples.ToArray(), Math.Max(1, mpeg.Channels), Math.Max(8000, mpeg.SampleRate));
	}

	private static (float[] Data, int Channels, int Rate) DecodeWav(string path)
	{
		byte[] bytes = File.ReadAllBytes(path);
		if (bytes.Length < 12 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
		{
			throw new InvalidDataException("Not a RIFF/WAVE file: " + path);
		}
		int format = 1;
		int channels = 2;
		int rate = 44100;
		int bits = 16;
		int dataOffset = -1;
		int dataLength = 0;
		int offset = 12;
		while (offset + 8 <= bytes.Length)
		{
			string id = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
			int size = BitConverter.ToInt32(bytes, offset + 4);
			int body = offset + 8;
			if (id == "fmt ")
			{
				format = BitConverter.ToUInt16(bytes, body);
				channels = BitConverter.ToUInt16(bytes, body + 2);
				rate = BitConverter.ToInt32(bytes, body + 4);
				bits = BitConverter.ToUInt16(bytes, body + 14);
				if (format == 0xFFFE && size >= 26)
				{
					format = BitConverter.ToUInt16(bytes, body + 24);
				}
			}
			else if (id == "data")
			{
				dataOffset = body;
				dataLength = Math.Min(size < 0 ? bytes.Length - body : size, bytes.Length - body);
				break;
			}
			offset = body + size + (size & 1);
		}
		if (dataOffset < 0)
		{
			throw new InvalidDataException("WAV has no data chunk: " + path);
		}
		if (format == 2 || format == 0x11)
		{
			throw new NotSupportedException("Compressed (ADPCM) WAV is not supported: " + path);
		}
		int bytesPerSample = Math.Max(1, bits / 8);
		int count = dataLength / bytesPerSample;
		float[] data = new float[count];
		for (int i = 0; i < count; i++)
		{
			int p = dataOffset + i * bytesPerSample;
			data[i] = (format, bits) switch
			{
				(3, 32) => BitConverter.ToSingle(bytes, p),
				(3, 64) => (float)BitConverter.ToDouble(bytes, p),
				(_, 8) => (bytes[p] - 128) / 128f,
				(_, 16) => BitConverter.ToInt16(bytes, p) / 32768f,
				(_, 24) => ((bytes[p] | (bytes[p + 1] << 8) | ((sbyte)bytes[p + 2] << 16))) / 8388608f,
				(_, 32) => BitConverter.ToInt32(bytes, p) / 2147483648f,
				_ => 0f
			};
		}
		return (data, Math.Max(1, channels), Math.Max(8000, rate));
	}

	private static float[] ToStereo(float[] data, int channels, int rate, int targetRate)
	{
		int frames = data.Length / channels;
		double ratio = (double)rate / targetRate;
		int outFrames = (int)Math.Ceiling(frames / ratio);
		float[] output = new float[outFrames * 2];
		for (int i = 0; i < outFrames; i++)
		{
			double source = i * ratio;
			int index = (int)source;
			double fraction = source - index;
			int next = Math.Min(index + 1, frames - 1);
			if (index >= frames)
			{
				break;
			}
			float left0 = data[index * channels];
			float right0 = channels > 1 ? data[index * channels + 1] : left0;
			float left1 = data[next * channels];
			float right1 = channels > 1 ? data[next * channels + 1] : left1;
			output[i * 2] = (float)(left0 + (left1 - left0) * fraction);
			output[i * 2 + 1] = (float)(right0 + (right1 - right0) * fraction);
		}
		return output;
	}
}

public sealed class AudioMixer : IDisposable
{
	private sealed class Voice
	{
		public required AudioClip Clip;

		public required string Tag;

		public float Volume;

		public int Frame;
	}

	public const int SampleRate = 48000;

	private readonly List<Voice> _voices = new List<Voice>();

	private readonly object _sync = new object();

	private readonly Sdl.AudioCallback _callback;

	private uint _device;

	private string? _deviceName;

	private bool _available = true;

	public AudioMixer()
	{
		_callback = Mix;
	}

	public int MaxVoices { get; set; } = 24;

	public bool IsAvailable => _available;

	public static IReadOnlyList<string> GetDeviceNames()
	{
		List<string> names = new List<string>();
		if (!Sdl.TryInit(Sdl.SDL_INIT_AUDIO))
		{
			return names;
		}
		int count = Sdl.SDL_GetNumAudioDevices(0);
		for (int i = 0; i < count; i++)
		{
			string name = Sdl.PtrToString(Sdl.SDL_GetAudioDeviceName(i, 0));
			if (!string.IsNullOrWhiteSpace(name))
			{
				names.Add(name);
			}
		}
		return names;
	}

	/// <summary>Opens (or re-opens) the output device. null/"Default" = system default sink.</summary>
	public bool EnsureDevice(string? deviceName)
	{
		string? wanted = string.IsNullOrWhiteSpace(deviceName) || deviceName.Equals("Default", StringComparison.OrdinalIgnoreCase) ? null : deviceName;
		lock (_sync)
		{
			if (_device != 0 && string.Equals(_deviceName, wanted, StringComparison.Ordinal))
			{
				return true;
			}
			if (!_available)
			{
				return false;
			}
			if (_device != 0)
			{
				Sdl.SDL_CloseAudioDevice(_device);
				_device = 0;
			}
			if (!Sdl.TryInit(Sdl.SDL_INIT_AUDIO))
			{
				_available = false;
				return false;
			}
			Sdl.AudioSpec desired = new Sdl.AudioSpec
			{
				freq = SampleRate,
				format = Sdl.AUDIO_F32SYS,
				channels = 2,
				samples = 512,
				callback = Marshal.GetFunctionPointerForDelegate(_callback)
			};
			_device = Sdl.SDL_OpenAudioDevice(wanted, 0, ref desired, out _, 0);
			if (_device == 0 && wanted != null)
			{
				wanted = null;
				_device = Sdl.SDL_OpenAudioDevice(null, 0, ref desired, out _, 0);
			}
			if (_device == 0)
			{
				App.LogException(new InvalidOperationException("SDL_OpenAudioDevice failed: " + Sdl.GetError()), "AudioMixer");
				return false;
			}
			_deviceName = wanted;
			Sdl.SDL_PauseAudioDevice(_device, 0);
			return true;
		}
	}

	public void Play(AudioClip clip, float volume, string tag)
	{
		lock (_sync)
		{
			while (_voices.Count >= MaxVoices)
			{
				_voices.RemoveAt(0);
			}
			_voices.Add(new Voice { Clip = clip, Volume = volume, Tag = tag });
		}
	}

	public void Stop(string tag)
	{
		lock (_sync)
		{
			_voices.RemoveAll(v => string.Equals(v.Tag, tag, StringComparison.OrdinalIgnoreCase));
		}
	}

	private void Mix(nint userdata, nint stream, int len)
	{
		int samples = len / 4;
		Span<float> output;
		unsafe
		{
			output = new Span<float>((void*)stream, samples);
		}
		output.Clear();
		lock (_sync)
		{
			for (int v = _voices.Count - 1; v >= 0; v--)
			{
				Voice voice = _voices[v];
				float[] data = voice.Clip.Samples;
				int start = voice.Frame * 2;
				int available = Math.Min(samples, data.Length - start);
				for (int i = 0; i < available; i++)
				{
					output[i] += data[start + i] * voice.Volume;
				}
				voice.Frame += available / 2;
				if (start + available >= data.Length)
				{
					_voices.RemoveAt(v);
				}
			}
		}
		for (int i = 0; i < samples; i++)
		{
			float x = output[i];
			output[i] = x > 1f ? 1f : (x < -1f ? -1f : x);
		}
	}

	public void Dispose()
	{
		lock (_sync)
		{
			_voices.Clear();
			if (_device != 0)
			{
				Sdl.SDL_CloseAudioDevice(_device);
				_device = 0;
			}
		}
	}
}
