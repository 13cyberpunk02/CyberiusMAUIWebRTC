namespace MauiWebRtc.Core.Models;

public enum PixelFormat
{
    I420,   // YUV planar — нативный для WebRTC
    Nv12,   // YUV semi-planar — Camera2 на Android часто отдаёт это
    Bgra32  // Windows MediaCapture
}