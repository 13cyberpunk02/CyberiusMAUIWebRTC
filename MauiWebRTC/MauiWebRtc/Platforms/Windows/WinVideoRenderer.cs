using System.Runtime.InteropServices.WindowsRuntime;
using MauiWebRtc.Core.Interfaces;
using MauiWebRtc.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using Image = Microsoft.UI.Xaml.Controls.Image;

namespace MauiWebRtc;

/// <summary>
/// Рендерит I420 фреймы на Image через WriteableBitmap.
///
/// Pipeline:
///   RawVideoFrame (I420) → BGRA byte[] → WriteableBitmap → Image.Source
/// </summary>
public sealed class WinVideoRenderer(Image image, ILogger<WinVideoRenderer> logger) : IVideoRenderer, IDisposable
{
    private WriteableBitmap? _bitmap;
    private bool _disposed;

    // ── IVideoRenderer ────────────────────────────────────────────

    public void RenderFrame(RawVideoFrame frame)
    {
        if (_disposed) return;

        // WriteableBitmap требует UI поток
        image.DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;
            try
            {
                // Пересоздаём bitmap если изменился размер
                if (_bitmap is null
                    || _bitmap.PixelWidth != frame.Width
                    || _bitmap.PixelHeight != frame.Height)
                {
                    _bitmap = new WriteableBitmap(frame.Width, frame.Height);
                    image.Source = _bitmap;
                }

                WriteI420ToBitmap(frame, _bitmap);
                _bitmap.Invalidate(); // сообщаем WinUI что пиксели обновились
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error rendering frame");
            }
        });
    }

    public void Clear()
    {
        image.DispatcherQueue.TryEnqueue(() => image.Source = null);
    }

    // ── I420 → WriteableBitmap (BGRA8) ────────────────────────────

    /// <summary>
    /// WriteableBitmap хранит пиксели в формате BGRA8 premultiplied.
    /// Конвертируем I420 → BGRA напрямую в pixel buffer без промежуточного массива.
    /// </summary>
    private static void WriteI420ToBitmap(RawVideoFrame frame, WriteableBitmap bitmap)
    {
        var width = frame.Width;
        var height = frame.Height;
        var chromaWidth = width / 2;
        var data = frame.Data;
        var uOffset = width * height;
        var vOffset = uOffset + chromaWidth * (height / 2);

        using var stream = bitmap.PixelBuffer.AsStream();
        var bgra = new byte[width * height * 4];

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

                var i = (row * width + col) * 4;
                bgra[i]     = (byte)b;
                bgra[i + 1] = (byte)g;
                bgra[i + 2] = (byte)r;
                bgra[i + 3] = 255;
            }
        }

        stream.Seek(0, SeekOrigin.Begin);
        stream.Write(bgra, 0, bgra.Length);
    }

    private static int Clamp(int v) => Math.Max(0, Math.Min(255, v));

    public void Dispose()
    {
        _disposed = true;
        _bitmap = null;
    }
}