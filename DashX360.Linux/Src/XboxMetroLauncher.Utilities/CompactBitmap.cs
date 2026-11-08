using System;
using System.Buffers;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace XboxMetroLauncher.Utilities;

internal sealed record CompactBitmap(BitmapSource Bitmap, Int32Rect VisibleBounds, Int32Rect PixelBounds)
{
    private DrawingImage? _artwork;

    /// <summary>Canvas-sized (1920x1080) drawing of the cropped bitmap. Created lazily on the UI thread (Avalonia drawings are thread-affine).</summary>
    public DrawingImage Artwork
    {
        get
        {
            if (_artwork == null)
            {
                DrawingGroup drawing=new();
                drawing.Children.Add(new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(new Rect(0,0,1920,1080)) });
                drawing.Children.Add(new ImageDrawing { ImageSource = Bitmap, Rect = new Rect(PixelBounds.X,PixelBounds.Y,PixelBounds.Width,PixelBounds.Height) });
                _artwork=new DrawingImage(drawing);
            }
            return _artwork;
        }
    }

    internal static CompactBitmap Load(string path)
    {
        BitmapSource source = ImageLoader.Load(path) ?? throw new InvalidOperationException($"Cannot load blades artwork '{path}'.");
        if (source.PixelWidth != 1920 || source.PixelHeight != 1080)
            throw new InvalidOperationException($"Blades artwork must be 1920x1080: '{path}'.");

        int stride = source.PixelWidth * 4;
        byte[] pixels = BitmapOps.ReadPbgra(source, out stride);
        try
        {
            int minX=source.PixelWidth, minY=source.PixelHeight, maxX=-1, maxY=-1;
            for (int y=0; y<source.PixelHeight; y++)
            for (int x=0; x<source.PixelWidth; x++)
            {
                if (pixels[y*stride+x*4+3] == 0) continue;
                minX=Math.Min(minX,x); minY=Math.Min(minY,y);
                maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y);
            }
            Int32Rect visible = maxX < minX ? new(0,0,1920,1080) : new(minX,minY,maxX-minX+1,maxY-minY+1);
            // A transparent border preserves filtering; the drawing retains canvas coordinates.
            int left=Math.Max(0,visible.X-1), top=Math.Max(0,visible.Y-1);
            int right=Math.Min(1920,visible.X+visible.Width+1), bottom=Math.Min(1080,visible.Y+visible.Height+1);
            Int32Rect bounds = new(left,top,right-left,bottom-top);
            int croppedStride=bounds.Width*4;
            byte[] cropped = ArrayPool<byte>.Shared.Rent(croppedStride*bounds.Height);
            BitmapSource bitmap;
            try
            {
                for (int y=0; y<bounds.Height; y++)
                    Buffer.BlockCopy(pixels,(top+y)*stride+left*4,cropped,y*croppedStride,croppedStride);
                bitmap=BitmapOps.Create(bounds.Width,bounds.Height,cropped,croppedStride);
            }
            finally { ArrayPool<byte>.Shared.Return(cropped); }
            return new(bitmap,visible,bounds);
        }
        finally { }
    }
}
