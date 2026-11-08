// Bitmap helpers replacing WPF imaging (BitmapImage, CroppedBitmap, FormatConvertedBitmap, RenderTargetBitmap usage).
// All helpers return real Avalonia Bitmaps so the cache/converters keep their WPF "BitmapSource" contracts.
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace XboxMetroLauncher.Compat;

public static class BitmapExtensions
{
	extension(Bitmap bitmap)
	{
		public int PixelWidth => bitmap.PixelSize.Width;

		public int PixelHeight => bitmap.PixelSize.Height;

		public double DpiX => bitmap.Dpi.X;

		public double DpiY => bitmap.Dpi.Y;
	}
}

public static class ImageLoader
{
	private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

	static ImageLoader()
	{
		Http.DefaultRequestHeaders.UserAgent.ParseAdd("DashX360/1.3 (Linux)");
	}

	/// <summary>Loads a bitmap from disk, optionally decoding to a target width/height (keeps aspect ratio).</summary>
	public static Bitmap? Load(string path, int decodePixelWidth = 0, int decodePixelHeight = 0)
	{
		try
		{
			if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
			{
				using Stream asset = AssetLoader.Open(new Uri(path));
				return Decode(asset, decodePixelWidth, decodePixelHeight);
			}
			using FileStream stream = File.OpenRead(path);
			return Decode(stream, decodePixelWidth, decodePixelHeight);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "ImageLoader.Load " + path);
			return null;
		}
	}

	public static Bitmap Decode(Stream stream, int decodePixelWidth, int decodePixelHeight)
	{
		if (decodePixelWidth > 0)
		{
			return Bitmap.DecodeToWidth(stream, decodePixelWidth, BitmapInterpolationMode.HighQuality);
		}
		if (decodePixelHeight > 0)
		{
			return Bitmap.DecodeToHeight(stream, decodePixelHeight, BitmapInterpolationMode.HighQuality);
		}
		return new Bitmap(stream);
	}

	/// <summary>Downloads (with an on-disk cache) and decodes a remote image.</summary>
	public static Bitmap? LoadRemote(Uri uri, int decodePixelWidth = 0)
	{
		try
		{
			string cacheFolder = Path.Combine(XboxMetroLauncher.Utilities.AppPaths.XdgDataFolder, "Cache", "Images");
			Directory.CreateDirectory(cacheFolder);
			string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
			string file = Path.Combine(cacheFolder, hash);
			if (!File.Exists(file) || new FileInfo(file).Length < 64)
			{
				byte[] data = Http.GetByteArrayAsync(uri).GetAwaiter().GetResult();
				File.WriteAllBytes(file, data);
			}
			return Load(file, decodePixelWidth);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "ImageLoader.LoadRemote " + uri);
			return null;
		}
	}

	/// <summary>Reads only the image header to get its size (PNG / JPEG / BMP / GIF); falls back to a full decode.</summary>
	public static PixelSize? ReadSize(string path)
	{
		try
		{
			using FileStream stream = File.OpenRead(path);
			byte[] header = new byte[32];
			int read = stream.Read(header, 0, header.Length);
			if (read >= 24 && header[0] == 0x89 && header[1] == 'P' && header[2] == 'N' && header[3] == 'G')
			{
				return new PixelSize(ReadBigEndian(header, 16), ReadBigEndian(header, 20));
			}
			if (read >= 26 && header[0] == 'B' && header[1] == 'M')
			{
				return new PixelSize(BitConverter.ToInt32(header, 18), Math.Abs(BitConverter.ToInt32(header, 22)));
			}
			if (read >= 10 && header[0] == 'G' && header[1] == 'I' && header[2] == 'F')
			{
				return new PixelSize(BitConverter.ToUInt16(header, 6), BitConverter.ToUInt16(header, 8));
			}
			if (read >= 2 && header[0] == 0xFF && header[1] == 0xD8)
			{
				stream.Position = 2;
				while (stream.Position < stream.Length)
				{
					int marker = stream.ReadByte();
					if (marker != 0xFF)
					{
						continue;
					}
					int type = stream.ReadByte();
					while (type == 0xFF)
					{
						type = stream.ReadByte();
					}
					if (type is 0xD8 or 0x01 || (type >= 0xD0 && type <= 0xD7))
					{
						continue;
					}
					int length = (stream.ReadByte() << 8) | stream.ReadByte();
					if (type is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
					{
						stream.ReadByte();
						int height = (stream.ReadByte() << 8) | stream.ReadByte();
						int width = (stream.ReadByte() << 8) | stream.ReadByte();
						return new PixelSize(width, height);
					}
					stream.Position += length - 2;
				}
			}
		}
		catch
		{
		}
		using Bitmap? bitmap = Load(path);
		return bitmap?.PixelSize;
	}

	private static int ReadBigEndian(byte[] data, int offset) => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}

public static class BitmapOps
{
	/// <summary>Copies the pixels as premultiplied BGRA (WPF Pbgra32 layout).</summary>
	public static byte[] ReadPbgra(Bitmap bitmap, out int stride)
	{
		PixelSize size = bitmap.PixelSize;
		stride = size.Width * 4;
		byte[] pixels = new byte[stride * size.Height];
		PixelFormat? format = bitmap.Format;
		if (format == PixelFormat.Bgra8888 || format == PixelFormat.Rgba8888)
		{
			GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
			try
			{
				bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
			}
			finally
			{
				handle.Free();
			}
			if (format == PixelFormat.Rgba8888)
			{
				for (int i = 0; i < pixels.Length; i += 4)
				{
					(pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
				}
			}
			if (bitmap.AlphaFormat == AlphaFormat.Unpremul)
			{
				Premultiply(pixels);
			}
			return pixels;
		}
		// Unknown format: render through a BGRA framebuffer.
		using WriteableBitmap converted = new WriteableBitmap(size, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
		using (ILockedFramebuffer fb = converted.Lock())
		{
			bitmap.CopyPixels(fb, AlphaFormat.Premul);
			for (int y = 0; y < size.Height; y++)
			{
				Marshal.Copy(fb.Address + y * fb.RowBytes, pixels, y * stride, stride);
			}
		}
		return pixels;
	}

	/// <summary>Copies the pixels as straight-alpha BGRA (WPF Bgra32 layout).</summary>
	public static byte[] ReadBgra(Bitmap bitmap, out int stride)
	{
		byte[] pixels = ReadPbgra(bitmap, out stride);
		for (int i = 0; i < pixels.Length; i += 4)
		{
			byte a = pixels[i + 3];
			if (a != 0 && a != 255)
			{
				pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
				pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
				pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
			}
		}
		return pixels;
	}

	private static void Premultiply(byte[] pixels)
	{
		for (int i = 0; i < pixels.Length; i += 4)
		{
			byte a = pixels[i + 3];
			if (a == 255)
			{
				continue;
			}
			pixels[i] = (byte)(pixels[i] * a / 255);
			pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
			pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
		}
	}

	/// <summary>Creates a bitmap from premultiplied BGRA pixels.</summary>
	public static WriteableBitmap Create(int width, int height, byte[] pbgra, int stride, double dpi = 96)
	{
		WriteableBitmap bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(dpi, dpi), PixelFormat.Bgra8888, AlphaFormat.Premul);
		using ILockedFramebuffer fb = bitmap.Lock();
		for (int y = 0; y < height; y++)
		{
			Marshal.Copy(pbgra, y * stride, fb.Address + y * fb.RowBytes, Math.Min(stride, fb.RowBytes));
		}
		return bitmap;
	}

	/// <summary>WPF WriteableBitmap.WritePixels(rect, pixels, stride, offset) for premultiplied BGRA data.</summary>
	public static void WritePixels(this WriteableBitmap bitmap, PixelRect rect, byte[] pixels, int stride, int offset)
	{
		using ILockedFramebuffer fb = bitmap.Lock();
		for (int y = 0; y < rect.Height; y++)
		{
			Marshal.Copy(pixels, offset + y * stride, fb.Address + (rect.Y + y) * fb.RowBytes + rect.X * 4, rect.Width * 4);
		}
	}

	/// <summary>WPF BitmapSource.CopyPixels(array, stride, offset) semantics (premultiplied BGRA).</summary>
	public static void CopyPixels(this Bitmap bitmap, byte[] destination, int destinationStride, int offset)
	{
		byte[] pixels = ReadPbgra(bitmap, out int stride);
		for (int y = 0; y < bitmap.PixelSize.Height; y++)
		{
			int target = offset + y * destinationStride;
			if (target + stride > destination.Length)
			{
				break;
			}
			Buffer.BlockCopy(pixels, y * stride, destination, target, stride);
		}
	}

	public static Bitmap Crop(Bitmap source, PixelRect rect)
	{
		PixelRect bounds = rect.Intersect(new PixelRect(source.PixelSize));
		if (bounds.Width <= 0 || bounds.Height <= 0)
		{
			return source;
		}
		byte[] pixels = ReadPbgra(source, out int stride);
		int croppedStride = bounds.Width * 4;
		byte[] cropped = new byte[croppedStride * bounds.Height];
		for (int y = 0; y < bounds.Height; y++)
		{
			Buffer.BlockCopy(pixels, (bounds.Y + y) * stride + bounds.X * 4, cropped, y * croppedStride, croppedStride);
		}
		return Create(bounds.Width, bounds.Height, cropped, croppedStride, source.Dpi.X);
	}

	/// <summary>Renders a drawing callback into a new bitmap (WPF DrawingVisual + RenderTargetBitmap).</summary>
	public static Bitmap Render(int width, int height, Action<DrawingContext> draw)
	{
		RenderTargetBitmap target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
		using (DrawingContext context = target.CreateDrawingContext())
		{
			draw(context);
		}
		return target;
	}

	/// <summary>Returns a scaled bitmap (WPF TransformedBitmap / DecodePixelWidth on an existing image).</summary>
	public static Bitmap Scale(Bitmap source, int width, int height)
	{
		return source.CreateScaledBitmap(new PixelSize(Math.Max(1, width), Math.Max(1, height)), BitmapInterpolationMode.HighQuality);
	}
}

/// <summary>WPF DrawingContext.Push*/Pop emulation (Avalonia uses disposable push states).</summary>
public static class DrawingContextStack
{
	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DrawingContext, System.Collections.Generic.Stack<DrawingContext.PushedState>> Stacks = new();

	public static void PushOpacityW(this DrawingContext context, double opacity) => Push(context, context.PushOpacity(opacity));

	public static void PushClipW(this DrawingContext context, Geometry clip) => Push(context, context.PushGeometryClip(clip));

	public static void PushClipW(this DrawingContext context, Rect clip) => Push(context, context.PushClip(clip));

	public static void PushTransformW(this DrawingContext context, Matrix matrix) => Push(context, context.PushTransform(matrix));

	public static void PushTransformW(this DrawingContext context, Transform transform) => Push(context, context.PushTransform(transform.Value));

	public static void PushOpacityMaskW(this DrawingContext context, IBrush mask, Rect bounds) => Push(context, context.PushOpacityMask(mask, bounds));

	private static void Push(DrawingContext context, DrawingContext.PushedState state)
	{
		Stacks.GetOrCreateValue(context).Push(state);
	}

	public static void Pop(this DrawingContext context)
	{
		if (Stacks.TryGetValue(context, out System.Collections.Generic.Stack<DrawingContext.PushedState>? stack) && stack.Count > 0)
		{
			stack.Pop().Dispose();
		}
	}
}
