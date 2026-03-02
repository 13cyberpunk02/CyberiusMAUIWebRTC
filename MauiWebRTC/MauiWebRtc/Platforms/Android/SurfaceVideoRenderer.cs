using Android.Graphics;
using Android.Views;
using MauiWebRtc.Core.Interfaces;
using MauiWebRtc.Core.Models;
using Microsoft.Extensions.Logging;
using Color = Android.Graphics.Color;
using Rect = Android.Graphics.Rect;
using RectF = Android.Graphics.RectF;

namespace MauiWebRtc;

/// <summary>
/// Рендерит входящие I420 фреймы на SurfaceView через Canvas.
///
/// Pipeline:
///   RawVideoFrame (I420) → конвертация I420→ARGB → Bitmap → Canvas на SurfaceHolder
///
/// Рендер происходит в фоновом потоке (не UI), так как фреймы приходят из Camera2/SIPSorcery.
/// </summary>
public sealed class SurfaceVideoRenderer(SurfaceView surface, ILogger<SurfaceVideoRenderer> logger)
    : IVideoRenderer, IDisposable
{
    private readonly Lock _lock = new();
    private bool _disposed;

    // ── IVideoRenderer ────────────────────────────────────────────

    public void RenderFrame(RawVideoFrame frame)
    {
        if (_disposed) return;

        var holder = surface.Holder;
        if (holder is null || !holder.Surface?.IsValid == true) return;

        lock (_lock)
        {
            Canvas? canvas = null;
            try
            {
                canvas = holder.LockCanvas(null);
                if (canvas is null) return;

                using var bitmap = I420ToBitmap(frame);

                // Растягиваем на весь Surface с сохранением пропорций
                var srcRect = new Rect(0, 0, frame.Width, frame.Height);
                var dstRect = ComputeDestRect(canvas.Width, canvas.Height, frame.Width, frame.Height);

                canvas.DrawBitmap(bitmap, srcRect, dstRect, null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error rendering frame");
            }
            finally
            {
                if (canvas is not null)
                    holder.UnlockCanvasAndPost(canvas);
            }
        }
    }

    public void Clear()
    {
        var holder = surface.Holder;
        if (holder is null) return;

        Canvas? canvas = null;
        try
        {
            canvas = holder.LockCanvas(null);
            canvas?.DrawColor(Color.Black);
        }
        finally
        {
            if (canvas is not null)
                holder.UnlockCanvasAndPost(canvas);
        }
    }

    // ── I420 → Bitmap ─────────────────────────────────────────────

    /// <summary>
    /// Конвертирует I420 (YUV planar) → ARGB_8888 Bitmap.
    ///
    /// Формула YCbCr → RGB (BT.601):
    ///   R = Y + 1.402 * (V - 128)
    ///   G = Y - 0.344 * (U - 128) - 0.714 * (V - 128)
    ///   B = Y + 1.772 * (U - 128)
    /// </summary>
    private static Bitmap I420ToBitmap(RawVideoFrame frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        var chromaWidth = width / 2;
        var chromaHeight = height / 2;

        var data = frame.Data;
        var ySize = width * height;
        var vOffset = ySize + chromaWidth * chromaHeight;

        var pixels = new int[width * height];

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var y = data[row * width + col] & 0xFF;
                var chromaRow = row / 2;
                var chromaCol = col / 2;
                var chromaIndex = chromaRow * chromaWidth + chromaCol;

                var u = (data[ySize + chromaIndex] & 0xFF) - 128;
                var v = (data[vOffset + chromaIndex] & 0xFF) - 128;

                var r = Clamp(y + (int)(1.402f * v));
                var g = Clamp(y - (int)(0.344f * u) - (int)(0.714f * v));
                var b = Clamp(y + (int)(1.772f * u));

                pixels[row * width + col] = (255 << 24) | (r << 16) | (g << 8) | b;
            }
        }

        var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888)!;
        bitmap.SetPixels(pixels, 0, width, 0, 0, width, height);
        return bitmap;
    }

    private static int Clamp(int value) => Math.Max(0, Math.Min(255, value));

    /// <summary>
    /// Вписываем фрейм в Surface с letterbox (чёрные полосы) сохраняя пропорции.
    /// </summary>
    private static RectF ComputeDestRect(int surfaceW, int surfaceH, int frameW, int frameH)
    {
        var scaleX = (float)surfaceW / frameW;
        var scaleY = (float)surfaceH / frameH;
        var scale = Math.Min(scaleX, scaleY);

        var dstW = frameW * scale;
        var dstH = frameH * scale;
        var left = (surfaceW - dstW) / 2f;
        var top = (surfaceH - dstH) / 2f;

        return new RectF(left, top, left + dstW, top + dstH);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
