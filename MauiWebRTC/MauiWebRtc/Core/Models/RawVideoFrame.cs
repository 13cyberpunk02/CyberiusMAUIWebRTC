namespace MauiWebRtc.Core.Models;


/// <summary>
/// Сырой видеофрейм в формате I420 (YUV planar).
/// Именно такой формат ожидает SIPSorcery для ExternalVideoSourceRawSample().
/// </summary>
public sealed class RawVideoFrame
{
    public required byte[] Data { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public PixelFormat Format { get; init; } = PixelFormat.I420;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

// ── Enums ─────────────────────────────────────────────────────────────────────

public enum PixelFormat
{
    I420,   // YUV planar — нативный для WebRTC
    NV12,   // YUV semi-planar — Camera2 на Android часто отдаёт это
    Bgra32  // Windows MediaCapture
}

public enum CallState
{
    Idle,
    Calling,        // Мы звоним, ждём ответа
    Receiving,      // Нам звонят, ждём нашего ответа
    Connecting,     // ICE negotiation в процессе
    Connected,      // Звонок установлен
    Ended           // Звонок завершён
}

public enum CameraFacing
{
    Front,
    Back
}