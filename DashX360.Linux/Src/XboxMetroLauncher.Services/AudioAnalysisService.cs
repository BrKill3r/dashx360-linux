using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace XboxMetroLauncher.Services;

public sealed class AudioAnalysisService : IDisposable
{
	private readonly object _sync = new object();

	private readonly float[] _analysisBuffer = new float[4096];

	private Thread? _thread;

	private CancellationTokenSource? _cts;

	private int _bufferWriteIndex;

	private double _bass;

	private double _mid;

	private double _treble;

	private double _loudness;

	private double _peak;

	private double _lastBass;

	private double _lastLoudness;

	public bool IsRunning
	{
		get
		{
			lock (_sync)
			{
				return _thread?.IsAlive ?? false;
			}
		}
	}

	public event EventHandler<AudioAnalysisFrame>? FrameReady;

	public void Start()
	{
		lock (_sync)
		{
			Thread? thread = _thread;
			if (thread == null || !thread.IsAlive)
			{
				_cts = new CancellationTokenSource();
				_thread = new Thread((ThreadStart)delegate
				{
					CaptureLoop(_cts.Token);
				})
				{
					IsBackground = true,
					Name = "Metro audio analyzer"
				};
				_thread.Start();
			}
		}
	}

	public void Stop()
	{
		CancellationTokenSource cts;
		Thread thread;
		lock (_sync)
		{
			cts = _cts;
			thread = _thread;
			_cts = null;
			_thread = null;
		}
		cts?.Cancel();
		if (thread != null && thread.IsAlive)
		{
			thread.Join(250);
		}
		cts?.Dispose();
	}

	public void Dispose()
	{
		Stop();
	}

	/// <summary>Captures what is playing on the default output (sink monitor) through PipeWire / PulseAudio.</summary>
	private void CaptureLoop(CancellationToken token)
	{
		const int sampleRate = 48000;
		System.Diagnostics.Process? process = StartCaptureProcess(sampleRate);
		if (process == null)
		{
			return;
		}
		try
		{
			using System.IO.Stream stream = process.StandardOutput.BaseStream;
			byte[] buffer = new byte[4096 * 8];
			int carry = 0;
			int framesSinceAnalysis = 0;
			while (!token.IsCancellationRequested)
			{
				int read = stream.Read(buffer, carry, buffer.Length - carry);
				if (read <= 0)
				{
					break;
				}
				int total = carry + read;
				int frames = total / 8;
				for (int i = 0; i < frames; i++)
				{
					float left = BitConverter.ToSingle(buffer, i * 8);
					float right = BitConverter.ToSingle(buffer, i * 8 + 4);
					PushSample((left + right) * 0.5f);
				}
				carry = total - frames * 8;
				if (carry > 0)
				{
					Buffer.BlockCopy(buffer, frames * 8, buffer, 0, carry);
				}
				framesSinceAnalysis += frames;
				if (framesSinceAnalysis >= 512)
				{
					framesSinceAnalysis = 0;
					Analyze(sampleRate);
				}
			}
		}
		catch
		{
		}
		finally
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill();
				}
			}
			catch
			{
			}
			process.Dispose();
		}
	}

	private static System.Diagnostics.Process? StartCaptureProcess(int sampleRate)
	{
		(string File, string[] Args)[] candidates =
		{
			("pw-record", new[] { "-P", "{ stream.capture.sink=true node.name=dashx360-visualizer media.role=Music }", "--format", "f32", "--rate", sampleRate.ToString(), "--channels", "2", "--latency", "20ms", "-" }),
			("parec", new[] { "--device=@DEFAULT_MONITOR@", "--format=float32le", "--rate=" + sampleRate, "--channels=2", "--latency-msec=20", "--client-name=DashX360" })
		};
		foreach ((string file, string[] args) in candidates)
		{
			string? executable = XboxMetroLauncher.Platform.LinuxApps.FindExecutable(file);
			if (executable == null)
			{
				continue;
			}
			try
			{
				System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo(executable)
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
				System.Diagnostics.Process? process = System.Diagnostics.Process.Start(info);
				if (process != null)
				{
					process.ErrorDataReceived += (_, _) => { };
					process.BeginErrorReadLine();
					return process;
				}
			}
			catch (Exception exception)
			{
				App.LogException(exception, "AudioAnalysisService.Start " + file);
			}
		}
		return null;
	}

	private static double Finite(double value) => double.IsFinite(value) ? value : 0.0;

	private void PushSilence(int frames)
	{
		for (int i = 0; i < frames; i++)
		{
			PushSample(0f);
		}
	}

	private void PushSample(float sample)
	{
		_analysisBuffer[_bufferWriteIndex] = Math.Clamp(sample, -1f, 1f);
		_bufferWriteIndex = (_bufferWriteIndex + 1) % _analysisBuffer.Length;
	}

	private void Analyze(int sampleRate)
	{
		if (sampleRate <= 0)
		{
			sampleRate = 48000;
		}
		double num = 0.0;
		for (int i = 0; i < _analysisBuffer.Length; i++)
		{
			float num2 = _analysisBuffer[i];
			num += (double)(num2 * num2);
		}
		num = Math.Sqrt(num / (double)_analysisBuffer.Length);
		double value = BandEnergy(sampleRate, 45.0, 65.0, 85.0, 110.0, 145.0);
		double value2 = BandEnergy(sampleRate, 260.0, 420.0, 720.0, 1100.0, 1800.0);
		double value3 = BandEnergy(sampleRate, 3200.0, 5200.0, 7600.0, 10200.0);
		double num3 = NormalizeBand(value, 42.0);
		double target = NormalizeBand(value2, 24.0);
		double target2 = NormalizeBand(value3, 42.0);
		double num4 = NormalizeBand(num, 12.0);
		double target3 = Math.Clamp(Math.Max(num3 - _lastBass * 0.72, num4 - _lastLoudness * 0.78) * 6.2, 0.0, 1.0);
		_bass = Envelope(_bass, num3, 0.86, 0.16);
		_mid = Envelope(_mid, target, 0.58, 0.14);
		_treble = Envelope(_treble, target2, 0.72, 0.22);
		_loudness = Envelope(_loudness, num4, 0.62, 0.12);
		_peak = Envelope(_peak, target3, 0.96, 0.11);
		_lastBass = _bass;
		_lastLoudness = _loudness;
		// Silence can produce NaN/Infinity (log of zero energy); Skia (unlike WPF) rejects NaN brush geometry.
		_bass = Finite(_bass);
		_mid = Finite(_mid);
		_treble = Finite(_treble);
		_loudness = Finite(_loudness);
		_peak = Finite(_peak);
		_lastBass = Finite(_lastBass);
		_lastLoudness = Finite(_lastLoudness);
		this.FrameReady?.Invoke(this, new AudioAnalysisFrame(_bass, _mid, _treble, _loudness, _peak));
	}

	private double BandEnergy(int sampleRate, params double[] frequencies)
	{
		double num = 0.0;
		foreach (double frequency in frequencies)
		{
			num += Goertzel(sampleRate, frequency);
		}
		return num / (double)frequencies.Length;
	}

	private double Goertzel(int sampleRate, double frequency)
	{
		double num = 2.0 * Math.Cos(Math.PI * 2.0 * frequency / (double)sampleRate);
		double num2 = 0.0;
		double num3 = 0.0;
		for (int i = 0; i < _analysisBuffer.Length; i++)
		{
			int num4 = (_bufferWriteIndex + i) % _analysisBuffer.Length;
			double num5 = 0.5 - 0.5 * Math.Cos(Math.PI * 2.0 * (double)i / (double)(_analysisBuffer.Length - 1));
			double num6 = (double)_analysisBuffer[num4] * num5 + num * num2 - num3;
			num3 = num2;
			num2 = num6;
		}
		double val = num2 * num2 + num3 * num3 - num * num2 * num3;
		return Math.Sqrt(Math.Max(0.0, val)) / ((double)_analysisBuffer.Length * 0.5);
	}

	private static double NormalizeBand(double value, double gain)
	{
		return Math.Clamp(1.0 - Math.Exp((0.0 - Math.Max(0.0, value)) * gain), 0.0, 1.0);
	}

	private static double Envelope(double current, double target, double attack, double release)
	{
		return current + (target - current) * ((target > current) ? attack : release);
	}

}
