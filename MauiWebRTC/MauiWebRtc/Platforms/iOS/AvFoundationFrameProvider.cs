using AVFoundation;
using CoreMedia;
using CoreVideo;
using MauiWebRtc.Core.Interfaces;
using MauiWebRtc.Core.Models;
using Microsoft.Extensions.Logging;

namespace MauiWebRtc;

public sealed class AvFoundationFrameProvider(ILogger<AvFoundationFrameProvider> logger) : ICameraFrameProvider
{
    private AVCaptureSession? _session;
    private AVCaptureDeviceInput? _deviceInput;
    private AVCaptureVideoDataOutput? _videoOutput;
    private SampleBufferDelegate? _sampleDelegate;
    private CoreFoundation.DispatchQueue? _captureQueue;

    private CameraFacing _currentFacing = CameraFacing.Front;
    private int _disposed;

    public bool IsRunning { get; private set; }
    public event Action<RawVideoFrame>? OnFrameAvailable;

    public async Task StartAsync(CameraFacing facing = CameraFacing.Front, CancellationToken ct = default)
    {
        if (IsRunning) return;
        _currentFacing = facing;

        await RequestCameraPermissionAsync();

        _captureQueue = new CoreFoundation.DispatchQueue("MauiWebRtc.CaptureQueue");

        _session = new AVCaptureSession
        {
            SessionPreset = AVCaptureSession.PresetMedium
        };

        var device = GetCaptureDevice(facing)
            ?? throw new InvalidOperationException($"Камера {facing} не найдена");

        _deviceInput = AVCaptureDeviceInput.FromDevice(device, out var error);
        if (error is not null)
            throw new InvalidOperationException($"AVCaptureDeviceInput error: {error.LocalizedDescription}");

        if (_deviceInput is not null && !_session.CanAddInput(_deviceInput))
            throw new InvalidOperationException("Не удалось добавить вход в сессию");

        if (_deviceInput is not null) _session.AddInput(_deviceInput);

        _videoOutput = new AVCaptureVideoDataOutput();

        // NV12 — нативный формат iOS камеры
        var settings = new CVPixelBufferAttributes
        {
            PixelFormatType = CVPixelFormatType.CV420YpCbCr8BiPlanarVideoRange
        };
        _videoOutput.WeakVideoSettings = settings.Dictionary;
        _videoOutput.AlwaysDiscardsLateVideoFrames = true;

        _sampleDelegate = new SampleBufferDelegate(OnSampleBuffer, logger);
        _videoOutput.SetSampleBufferDelegate(_sampleDelegate, _captureQueue);

        if (!_session.CanAddOutput(_videoOutput))
            throw new InvalidOperationException("Не удалось добавить выход в сессию");

        _session.AddOutput(_videoOutput);
        SetVideoOrientation();

        _session.StartRunning();
        IsRunning = true;
        logger.LogInformation("AVFoundation started: {Facing}", facing);
    }

    public Task StopAsync()
    {
        if (!IsRunning) return Task.CompletedTask;

        _session?.StopRunning();

        if (_deviceInput is not null)
        {
            _session?.RemoveInput(_deviceInput);
            _deviceInput.Dispose();
            _deviceInput = null;
        }

        if (_videoOutput is not null)
        {
            _session?.RemoveOutput(_videoOutput);
            _videoOutput.Dispose();
            _videoOutput = null;
        }

        _session?.Dispose();
        _session = null;
        _sampleDelegate = null;

        IsRunning = false;
        logger.LogInformation("AVFoundation stopped");
        return Task.CompletedTask;
    }

    public async Task SwitchCameraAsync()
    {
        var newFacing = _currentFacing == CameraFacing.Front ? CameraFacing.Back : CameraFacing.Front;
        await StopAsync();
        await StartAsync(newFacing);
    }

    private void OnSampleBuffer(CMSampleBuffer sampleBuffer)
    {
        using var pixelBuffer = sampleBuffer.GetImageBuffer() as CVPixelBuffer;
        if (pixelBuffer is null) return;

        pixelBuffer.Lock(CVPixelBufferLock.ReadOnly);
        try
        {
            var i420 = ConvertNv12ToI420(pixelBuffer);
            OnFrameAvailable?.Invoke(new RawVideoFrame
            {
                Data = i420,
                Width = (int)pixelBuffer.Width,
                Height = (int)pixelBuffer.Height,
                Format = PixelFormat.I420
            });
        }
        finally
        {
            pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly);
        }
    }

    /// <summary>
    /// NV12 (semi-planar) → I420 (planar).
    /// GetBaseAddress(planeIndex) возвращает IntPtr — правильный метод в Xamarin биндингах.
    /// </summary>
    private static unsafe byte[] ConvertNv12ToI420(CVPixelBuffer pixelBuffer)
    {
        var width = (int)pixelBuffer.Width;
        var height = (int)pixelBuffer.Height;
        var chromaWidth = width / 2;
        var chromaHeight = height / 2;

        var yBytesPerRow = (int)pixelBuffer.GetBytesPerRowOfPlane(0);
        var uvBytesPerRow = (int)pixelBuffer.GetBytesPerRowOfPlane(1);

        // GetBaseAddress(planeIndex) — правильное имя метода
        var yBaseAddr = (byte*)pixelBuffer.GetBaseAddress(0);
        var uvBaseAddr = (byte*)pixelBuffer.GetBaseAddress(1);

        var i420Size = width * height + chromaWidth * chromaHeight * 2;
        var result = new byte[i420Size];
        var offset = 0;

        // Y плоскость
        for (var row = 0; row < height; row++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                (IntPtr)(yBaseAddr + row * yBytesPerRow),
                result, offset, width);
            offset += width;
        }

        // Разделяем CbCr interleaved → U и V
        var uOffset = offset;
        var vOffset = offset + chromaWidth * chromaHeight;

        for (var row = 0; row < chromaHeight; row++)
        {
            var uvRow = uvBaseAddr + row * uvBytesPerRow;
            for (var col = 0; col < chromaWidth; col++)
            {
                result[uOffset++] = uvRow[col * 2];       // Cb (U)
                result[vOffset++] = uvRow[col * 2 + 1];   // Cr (V)
            }
        }

        return result;
    }

    private static AVCaptureDevice? GetCaptureDevice(CameraFacing facing)
    {
        var position = facing == CameraFacing.Front
            ? AVCaptureDevicePosition.Front
            : AVCaptureDevicePosition.Back;

        return AVCaptureDevice.GetDefaultDevice(
                   AVCaptureDeviceType.BuiltInWideAngleCamera,
                   AVMediaTypes.Video,
                   position)
               ?? AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
    }

    private void SetVideoOrientation()
    {
        if (_videoOutput is null) return;
        var connection = _videoOutput.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant()!);
        if (connection is null) return;

        // iOS 17+: используем угол поворота вместо устаревшего VideoOrientation
        // Portrait = 90 градусов
        if (OperatingSystem.IsIOSVersionAtLeast(17))
        {
            if (connection.IsVideoRotationAngleSupported(90))
                connection.VideoRotationAngle = 90;
        }
        else
        {
#pragma warning disable CA1422
            if (connection.SupportsVideoOrientation)
                connection.VideoOrientation = AVCaptureVideoOrientation.Portrait;
#pragma warning restore CA1422
        }

        if (connection.SupportsVideoMirroring)
            connection.VideoMirrored = _currentFacing == CameraFacing.Front;
    }

    private static async Task RequestCameraPermissionAsync()
    {
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (status == AVAuthorizationStatus.Authorized) return;

        if (status == AVAuthorizationStatus.NotDetermined)
        {
            var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
            if (!granted)
                throw new UnauthorizedAccessException("Пользователь отказал в доступе к камере");
            return;
        }

        throw new UnauthorizedAccessException(
            "Доступ к камере запрещён. Разрешите в Настройки → Конфиденциальность → Камера");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync();
        _captureQueue?.Dispose();
    }

    private sealed class SampleBufferDelegate(Action<CMSampleBuffer> onSample, ILogger logger)
        : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput,
            CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
        {
            try { onSample(sampleBuffer); }
            catch (Exception ex) { logger.LogError(ex, "SampleBufferDelegate error"); }
            finally { sampleBuffer.Dispose(); }
        }

        public override void DidDropSampleBuffer(AVCaptureOutput captureOutput,
            CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
            => sampleBuffer.Dispose();
    }
}
