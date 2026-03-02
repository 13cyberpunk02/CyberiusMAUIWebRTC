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