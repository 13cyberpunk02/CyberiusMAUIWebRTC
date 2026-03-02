using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Java.Util.Concurrent;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Java.Lang;
using MauiWebRtc.Core.Interfaces;
using MauiWebRtc.Core.Models;
using Microsoft.Extensions.Logging;
using Exception = System.Exception;
using Image = Android.Media.Image;
using PixelFormat = MauiWebRtc.Core.Models.PixelFormat;

namespace MauiWebRtc;

/// <summary>
/// Захватывает кадры с камеры через Camera2 API.
/// Отдаёт фреймы в формате I420 — именно этот формат ожидает SIPSorcery.
///
/// Поток кадров:
///   Camera2 → ImageReader (YUV_420_888) → конвертация NV12/YUV → I420 → OnFrameAvailable
/// </summary>
public sealed class Camera2FrameProvider(ILogger<Camera2FrameProvider> logger) : ICameraFrameProvider
{
    private CameraManager? _cameraManager;
    private CameraDevice? _cameraDevice;
    private CameraCaptureSession? _captureSession;
    private ImageReader? _imageReader;
    private HandlerThread? _backgroundThread;
    private Handler? _backgroundHandler;

    private string? _frontCameraId;
    private string? _backCameraId;
    private CameraFacing _currentFacing = CameraFacing.Front;
    private int _disposed;

    // Разрешение кадра — 640x480 достаточно для видеозвонка, не перегружает сеть
    private const int FrameWidth = 640;
    private const int FrameHeight = 480;

    public bool IsRunning { get; private set; }
    public event Action<RawVideoFrame>? OnFrameAvailable;

    // ── Запуск ────────────────────────────────────────────────────

    public async Task StartAsync(CameraFacing facing = CameraFacing.Front, CancellationToken ct = default)
    {
        if (IsRunning) return;

        _currentFacing = facing;

        StartBackgroundThread();
        InitCameraManager();
        FindCameraIds();

        var cameraId = GetCameraId(facing)
            ?? throw new InvalidOperationException($"Камера {facing} не найдена на устройстве");

        await OpenCameraAsync(cameraId, ct);
        StartCaptureSession();

        IsRunning = true;
        logger.LogInformation("Camera2 started: {Facing} ({W}x{H})", facing, FrameWidth, FrameHeight);
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        _captureSession?.Close();
        _captureSession = null;

        _cameraDevice?.Close();
        _cameraDevice = null;

        _imageReader?.Close();
        _imageReader = null;

        StopBackgroundThread();

        IsRunning = false;
        logger.LogInformation("Camera2 stopped");
    }

    public async Task SwitchCameraAsync()
    {
        var newFacing = _currentFacing == CameraFacing.Front
            ? CameraFacing.Back
            : CameraFacing.Front;

        logger.LogInformation("Switching camera: {Old} → {New}", _currentFacing, newFacing);

        await StopAsync();
        await StartAsync(newFacing);
    }

    // ── Camera2 инициализация ─────────────────────────────────────

    private void InitCameraManager()
    {
        var context = Android.App.Application.Context;
        _cameraManager = (CameraManager)context.GetSystemService(Context.CameraService)!;
    }

    private void FindCameraIds()
    {
        if (_cameraManager is null) return;

        foreach (var id in _cameraManager.GetCameraIdList())
        {
            var chars = _cameraManager.GetCameraCharacteristics(id);
            var facing = (Integer?)chars.Get(CameraCharacteristics.LensFacing);

            if (facing?.IntValue() == (int)LensFacing.Front)
                _frontCameraId = id;
            else if (facing?.IntValue() == (int)LensFacing.Back)
                _backCameraId = id;
        }

        logger.LogDebug("Cameras found — front: {F}, back: {B}", _frontCameraId, _backCameraId);
    }

    private string? GetCameraId(CameraFacing facing) =>
        facing == CameraFacing.Front ? _frontCameraId : _backCameraId;

    private Task OpenCameraAsync(string cameraId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();

        var callback = new CameraStateCallback(
            onOpened: device =>
            {
                _cameraDevice = device;
                tcs.TrySetResult(true);
            },
            onError: (_, error) =>
            {
                var ex = new Exception($"Camera2 open error: {error}");
                logger.LogError(ex, "Failed to open camera {Id}", cameraId);
                tcs.TrySetException(ex);
            },
            onDisconnected: _ =>
            {
                logger.LogWarning("Camera {Id} disconnected", cameraId);
                tcs.TrySetCanceled();
            }
        );

        ct.Register(() => tcs.TrySetCanceled());
        _cameraManager!.OpenCamera(cameraId, callback, _backgroundHandler);

        return tcs.Task;
    }

    private void StartCaptureSession()
    {
        // ImageReader — сюда Camera2 будет складывать кадры
        _imageReader = ImageReader.NewInstance(
            FrameWidth, FrameHeight,
            ImageFormatType.Yuv420888, // NV12/YUV420 — то что Camera2 отдаёт нативно
            maxImages: 2               // 2 буфера достаточно, больше = больше задержка
        );
        _imageReader.SetOnImageAvailableListener(new ImageAvailableListener(OnImageAvailable), _backgroundHandler);

        var surface = _imageReader.Surface;

        var sessionCallback = new CaptureSessionStateCallback(
            onConfigured: session =>
            {
                _captureSession = session;

                // Строим запрос повторяющегося захвата
                var requestBuilder = _cameraDevice!.CreateCaptureRequest(CameraTemplate.Preview);
                if (surface is not null) requestBuilder.AddTarget(surface);

                // Автофокус если поддерживается
                if (CaptureRequest.ControlAfMode is not null)
                    requestBuilder.Set(CaptureRequest.ControlAfMode,
                        (int)ControlAFMode.ContinuousVideo);

                session.SetRepeatingRequest(requestBuilder.Build(), null, _backgroundHandler);
            },
            onConfigureFailed: _ =>
            {
                logger.LogError("CaptureSession configuration failed");
            }
        );

        // Android 30+: CreateCaptureSession(List, Callback, Handler) устарел
        // новый API — SessionConfiguration с OutputConfiguration
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            if (surface is null) return;
            var outputConfig = new OutputConfiguration(surface);
            var sessionConfig = new SessionConfiguration(
                (int)SessionType.Regular,
                new List<OutputConfiguration> { outputConfig },
                new HandlerExecutorCompat(_backgroundHandler!),
                sessionCallback);
            _cameraDevice!.CreateCaptureSession(sessionConfig);
        }
        else
        {
#pragma warning disable CA1422
            if (surface is not null)
                _cameraDevice!.CreateCaptureSession(
                    new List<Surface> { surface },
                    sessionCallback,
                    _backgroundHandler);
#pragma warning restore CA1422
        }
    }

    // ── Обработка кадра ───────────────────────────────────────────

    private void OnImageAvailable(ImageReader reader)
    {
        using var image = reader.AcquireLatestImage();
        if (image is null) return;

        try
        {
            var i420 = ConvertYuv420888ToI420(image);
            OnFrameAvailable?.Invoke(new RawVideoFrame
            {
                Data = i420,
                Width = image.Width,
                Height = image.Height,
                Format = PixelFormat.I420
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error converting camera frame");
        }
        finally
        {
            image.Close();
        }
    }

    /// <summary>
    /// Конвертирует YUV_420_888 (Camera2) → I420 (planar YUV).
    ///
    /// YUV_420_888 может быть как planar (I420) так и semi-planar (NV12/NV21)
    /// в зависимости от устройства. Определяем по pixelStride плоскости U.
    ///
    /// I420 layout: Y plane (W*H) | U plane (W/2 * H/2) | V plane (W/2 * H/2)
    /// </summary>
    private static byte[] ConvertYuv420888ToI420(Image image)
    {
        var width = image.Width;
        var height = image.Height;
        var chromaWidth = width / 2;
        var chromaHeight = height / 2;

        var yPlane = image.GetPlanes()[0];
        var uPlane = image.GetPlanes()[1];
        var vPlane = image.GetPlanes()[2];

        var yBuffer = yPlane.Buffer!;
        var uBuffer = uPlane.Buffer!;
        var vBuffer = vPlane.Buffer!;

        var yRowStride = yPlane.RowStride;
        var uvRowStride = uPlane.RowStride;
        var uvPixelStride = uPlane.PixelStride; // 1 = planar (I420), 2 = semi-planar (NV12)

        var i420Size = width * height + chromaWidth * chromaHeight * 2;
        var result = new byte[i420Size];

        var offset = 0;

        // Y плоскость
        for (var row = 0; row < height; row++)
        {
            yBuffer.Position(row * yRowStride);
            yBuffer.Get(result, offset, width);
            offset += width;
        }

        // U плоскость
        for (var row = 0; row < chromaHeight; row++)
        {
            uBuffer.Position(row * uvRowStride);
            for (var col = 0; col < chromaWidth; col++)
            {
                result[offset++] = (byte)uBuffer.Get();
                if (uvPixelStride == 2) uBuffer.Get(); // пропускаем V байт в NV12
            }
        }

        // V плоскость
        for (var row = 0; row < chromaHeight; row++)
        {
            vBuffer.Position(row * uvRowStride);
            for (var col = 0; col < chromaWidth; col++)
            {
                result[offset++] = (byte)vBuffer.Get();
                if (uvPixelStride == 2) vBuffer.Get(); // пропускаем U байт в NV21
            }
        }

        return result;
    }

    // ── Фоновый поток ─────────────────────────────────────────────

    // Camera2 требует Looper — запускаем выделенный HandlerThread
    private void StartBackgroundThread()
    {
        _backgroundThread = new HandlerThread("Camera2Background");
        _backgroundThread.Start();
        _backgroundHandler = new Handler(_backgroundThread.Looper!);
    }

    private void StopBackgroundThread()
    {
        _backgroundThread?.QuitSafely();
        _backgroundThread?.Join();
        _backgroundThread = null;
        _backgroundHandler = null;
    }

    // ── IAsyncDisposable ──────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync();
    }

    // ── Вспомогательные callback классы ──────────────────────────

    private sealed class CameraStateCallback(
        Action<CameraDevice> onOpened,
        Action<CameraDevice, CameraError> onError,
        Action<CameraDevice> onDisconnected) : CameraDevice.StateCallback
    {
        public override void OnOpened(CameraDevice camera) => onOpened(camera);
        public override void OnError(CameraDevice camera, [GeneratedEnum] CameraError error) => onError(camera, error);
        public override void OnDisconnected(CameraDevice camera) => onDisconnected(camera);
    }

    private sealed class CaptureSessionStateCallback(
        Action<CameraCaptureSession> onConfigured,
        Action<CameraCaptureSession> onConfigureFailed) : CameraCaptureSession.StateCallback
    {
        public override void OnConfigured(CameraCaptureSession session) => onConfigured(session);
        public override void OnConfigureFailed(CameraCaptureSession session) => onConfigureFailed(session);
    }

    private sealed class ImageAvailableListener(Action<ImageReader> onAvailable) : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader? reader)
        {
            if (reader is not null) onAvailable(reader);
        }
    }

    /// <summary>
    /// HandlerExecutor не забиндён в Xamarin.Android.
    /// Это простая обёртка — постит Runnable в Handler (= нужный поток с Looper).
    /// </summary>
    private sealed class HandlerExecutorCompat(Handler handler) : Java.Lang.Object, IExecutor
    {
        public void Execute(IRunnable? command)
        {
            if (command is not null) handler.Post(command);
        }
    }
}
