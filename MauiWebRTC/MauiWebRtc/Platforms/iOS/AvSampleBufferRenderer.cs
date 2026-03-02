using CoreGraphics;
using MauiWebRtc.Core.Interfaces;
using MauiWebRtc.Core.Models;
using Microsoft.Extensions.Logging;
using UIKit;

namespace MauiWebRtc;

/// <summary>
/// Рендерит I420 фреймы на UIImageView.
///
/// Pipeline:
///   RawVideoFrame (I420) → ARGB byte[] → CGBitmapContext → CGImage → UIImage → UIImageView
///
/// MTKView/Metal отброшен — биндинги нестабильны между версиями MAUI.
/// UIImageView достаточно для 30fps видеозвонка на современных устройствах.
/// </summary>
public sealed class AvSampleBufferRenderer : IVideoRenderer, IDisposable
{
    private readonly UIImageView _imageView;
    private readonly ILogger<AvSampleBufferRenderer> _logger;
    private bool _disposed;

    public AvSampleBufferRenderer(UIImageView imageView, ILogger<AvSampleBufferRenderer> logger)
    {
        _imageView = imageView;
        _imageView.ContentMode = UIViewContentMode.ScaleAspectFit;
        _imageView.BackgroundColor = UIColor.Black;
        _logger = logger;
    }

    // ── IVideoRenderer ────────────────────────────────────────────

    public void RenderFrame(RawVideoFrame frame)
    {
        if (_disposed) return;

        try
        {
            var uiImage = I420ToUiImage(frame);

            // UIKit требует обновления на главном потоке
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_disposed)
                    _imageView.Image = uiImage;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering frame");
        }
    }

    public void Clear()
    {
        MainThread.BeginInvokeOnMainThread(() => _imageView.Image = null);
    }

    // ── I420 → UIImage ────────────────────────────────────────────

    /// <summary>
    /// I420 → ARGB → CGBitmapContext → CGImage → UIImage.
    ///
    /// CGBitmapContext — стандартный iOS способ создать изображение из raw bytes.
    /// Цветовая формула BT.601 (стандарт для видеозвонков SD/HD).
    /// </summary>
    private static UIImage I420ToUiImage(RawVideoFrame frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        var chromaWidth = width / 2;
        var data = frame.Data;
        var ySize = width * height;
        var chromaSize = chromaWidth * (height / 2);
        var uOffset = ySize;
        var vOffset = ySize + chromaSize;

        // ARGB буфер для CGBitmapContext
        var argb = new byte[width * height * 4];

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var y = data[row * width + col] & 0xFF;
                var chromaIndex = (row / 2) * chromaWidth + (col / 2);
                var u = (data[uOffset + chromaIndex] & 0xFF) - 128;
                var v = (data[vOffset + chromaIndex] & 0xFF) - 128;

                var r = Clamp(y + (int)(1.402f * v));
                var g = Clamp(y - (int)(0.344f * u) - (int)(0.714f * v));
                var b = Clamp(y + (int)(1.772f * u));

                var pixelOffset = (row * width + col) * 4;
                argb[pixelOffset]     = 255; // A
                argb[pixelOffset + 1] = (byte)r;
                argb[pixelOffset + 2] = (byte)g;
                argb[pixelOffset + 3] = (byte)b;
            }
        }

        // CGBitmapContext из ARGB байтов
        using var colorSpace = CGColorSpace.CreateDeviceRGB();
        using var context = new CGBitmapContext(
            argb,
            width, height,
            bitsPerComponent: 8,
            bytesPerRow: width * 4,
            colorSpace,
            CGBitmapFlags.PremultipliedFirst); // ARGB

        using var cgImage = context.ToImage()!;
        return new UIImage(cgImage);
    }

    private static int Clamp(int v) => Math.Max(0, Math.Min(255, v));

    public void Dispose()
    {
        _disposed = true;
    }
}
