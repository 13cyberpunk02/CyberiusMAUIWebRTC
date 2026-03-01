using MauiWebRtc.Core.Models;

namespace MauiWebRtc.Core.Interfaces;

/// <summary>
/// Платформенный провайдер кадров с камеры.
/// Реализуется отдельно для Android (Camera2), iOS (AVFoundation), Windows (MediaCapture).
/// </summary>
public interface ICameraFrameProvider : IAsyncDisposable
{
    bool IsRunning { get; }

    Task StartAsync(CameraFacing facing = CameraFacing.Front, CancellationToken ct = default);
    Task StopAsync();
    Task SwitchCameraAsync();

    /// <summary>
    /// Вызывается на каждый новый кадр с камеры.
    /// ВАЖНО: вызывается из фонового потока, не UI!
    /// </summary>
    event Action<RawVideoFrame>? OnFrameAvailable;
}