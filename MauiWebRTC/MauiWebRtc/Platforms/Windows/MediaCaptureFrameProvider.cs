using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using MauiWebRtc.Core.Interfaces;
using MauiWebRtc.Core.Models;
using Microsoft.Extensions.Logging;


namespace MauiWebRtc;

/// <summary>
/// Захватывает кадры с камеры через Windows.Media.Capture.
///
/// Pipeline:
///   MediaCapture → MediaFrameReader → SoftwareBitmap (Bgra8) → конвертация Bgra→I420 → OnFrameAvailable
/// </summary>
public sealed class MediaCaptureFrameProvider(ILogger<MediaCaptureFrameProvider> logger) : ICameraFrameProvider
{
    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private int _disposed;

    public bool IsRunning { get; private set; }
    public event Action<RawVideoFrame>? OnFrameAvailable;

    // ── Запуск ────────────────────────────────────────────────────

    public async Task StartAsync(CameraFacing facing = CameraFacing.Front, CancellationToken ct = default)
    {
        if (IsRunning) return;

        _mediaCapture = new MediaCapture();

        var settings = new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = StreamingCaptureMode.Video,
            VideoDeviceId = await GetCameraDeviceIdAsync(facing)
        };

        await _mediaCapture.InitializeAsync(settings);

        // Ищем источник видео фреймов
        var frameSource = _mediaCapture.FrameSources
            .Values
            .FirstOrDefault(s => s.Info.MediaStreamType == MediaStreamType.VideoRecord
                              || s.Info.MediaStreamType == MediaStreamType.VideoPreview)
            ?? throw new InvalidOperationException("Видео источник не найден");

        // Предпочитаем Bgra8 — удобен для конвертации
        var format = frameSource.SupportedFormats
                         .FirstOrDefault(f => f.VideoFormat.Width == 640
                                              && f.Subtype == MediaEncodingSubtypes.Bgra8)
                     ?? frameSource.SupportedFormats[0];

        await frameSource.SetFormatAsync(format);

        _frameReader = await _mediaCapture.CreateFrameReaderAsync(frameSource);
        _frameReader.FrameArrived += OnFrameArrived;

        await _frameReader.StartAsync();

        IsRunning = true;
        logger.LogInformation("MediaCapture started: {Facing}", facing);
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        if (_frameReader is not null)
        {
            _frameReader.FrameArrived -= OnFrameArrived;
            await _frameReader.StopAsync();
            _frameReader.Dispose();
            _frameReader = null;
        }

        _mediaCapture?.Dispose();
        _mediaCapture = null;

        IsRunning = false;
        logger.LogInformation("MediaCapture stopped");
    }

    public async Task SwitchCameraAsync()
    {
        var facing = CameraFacing.Front; // Windows: просто перезапускаем с другой камерой
        await StopAsync();
        await StartAsync(facing);
    }

    // ── Обработка фрейма ──────────────────────────────────────────

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var frame = sender.TryAcquireLatestFrame();
        if (frame?.VideoMediaFrame is null) return;

        using var bitmap = frame.VideoMediaFrame.SoftwareBitmap;
        if (bitmap is null) return;

        // Приводим к Bgra8 premultiplied если нужно
        SoftwareBitmap? bgraFrame = null;
        try
        {
            bgraFrame = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                ? SoftwareBitmap.Copy(bitmap)
                : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var i420 = ConvertBgra8ToI420(bgraFrame);

            OnFrameAvailable?.Invoke(new RawVideoFrame
            {
                Data = i420,
                Width = bgraFrame.PixelWidth,
                Height = bgraFrame.PixelHeight,
                Format = PixelFormat.I420
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error converting frame");
        }
        finally
        {
            bgraFrame?.Dispose();
        }
    }

    // ── BGRA8 → I420 ──────────────────────────────────────────────

    /// <summary>
    /// Конвертирует BGRA8 (Windows MediaCapture) → I420 (SIPSorcery).
    /// Читаем байты через CopyToBuffer — без COM интерфейсов, безопасно.
    /// Формула RGB→YCbCr (BT.601).
    /// </summary>
    private static byte[] ConvertBgra8ToI420(SoftwareBitmap bitmap)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var chromaWidth = width / 2;
        var chromaHeight = height / 2;

        // Копируем пиксели в обычный byte[] через Windows.Storage.Streams
        var bgraData = new byte[width * height * 4];
        bitmap.CopyToBuffer(bgraData.AsBuffer());

        var i420Size = width * height + chromaWidth * chromaHeight * 2;
        var result = new byte[i420Size];

        var yOffset = 0;
        var uOffset = width * height;
        var vOffset = uOffset + chromaWidth * chromaHeight;
        var stride = width * 4;

        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var pixelOffset = row * stride + col * 4;
                var b = bgraData[pixelOffset];
                var g = bgraData[pixelOffset + 1];
                var r = bgraData[pixelOffset + 2];

                result[yOffset++] = (byte)(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);

                if (row % 2 != 0 || col % 2 != 0) continue;
                result[uOffset++] = (byte)(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                result[vOffset++] = (byte)(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
            }
        }

        return result;
    }

    // ── Выбор устройства ──────────────────────────────────────────

    private static async Task<string> GetCameraDeviceIdAsync(CameraFacing facing)
    {
        var panel = facing == CameraFacing.Front
            ? Windows.Devices.Enumeration.Panel.Front
            : Windows.Devices.Enumeration.Panel.Back;

        var devices = await Windows.Devices.Enumeration.DeviceInformation
            .FindAllAsync(Windows.Devices.Enumeration.DeviceClass.VideoCapture);

        // Ищем камеру нужной стороны, fallback на первую доступную
        return devices
            .FirstOrDefault(d => d.EnclosureLocation?.Panel == panel)
            ?.Id ?? devices.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("Камера не найдена");
    }

    // ── IAsyncDisposable ──────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync();
    }
}