using MauiWebRtc.Core;
using MauiWebRtc.Core.Interfaces;

namespace MauiWebRtc;

public static class MauiWebRtcExtensions
{
    public static MauiAppBuilder UseMauiWebRtc(
        this MauiAppBuilder builder,
        Action<WebRtcOptions> configure)
    {
        var options = new WebRtcOptions { SignalingUrl = string.Empty };
        configure(options);

        if (string.IsNullOrWhiteSpace(options.SignalingUrl))
            throw new ArgumentException("WebRtcOptions.SignalingUrl не может быть пустым");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ISignalingService, SignalingService>();
        builder.Services.AddSingleton<IWebRtcClient, WebRtcClient>();

        // Платформенный провайдер камеры — каждая платформа компилирует свою реализацию
#if ANDROID
     //   builder.Services.AddSingleton<ICameraFrameProvider, Camera2FrameProvider>();
#elif IOS
       // builder.Services.AddSingleton<ICameraFrameProvider, AVFoundationFrameProvider>();
#elif WINDOWS
        //builder.Services.AddSingleton<ICameraFrameProvider, MediaCaptureFrameProvider>();
#endif

        builder.ConfigureMauiHandlers(handlers =>
        {
            // handlers.AddHandler<CameraPreviewView, CameraPreviewHandler>();
            // handlers.AddHandler<RemoteVideoView, RemoteVideoHandler>();
        });

        return builder;
    }
}
