using Android.Views;
using MauiWebRtc.Controls;
using MauiWebRtc.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;

namespace MauiWebRtc.Handlers;

public partial class RemoteVideoHandler : ViewHandler<RemoteVideoView, SurfaceView>
{
    private SurfaceVideoRenderer? _renderer;

    protected override SurfaceView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(SurfaceView platformView)
    {
        base.ConnectHandler(platformView);
        var loggerFactory = MauiContext!.Services.GetRequiredService<ILoggerFactory>();
        _renderer = new SurfaceVideoRenderer(platformView, loggerFactory.CreateLogger<SurfaceVideoRenderer>());
        VirtualView.FrameReceived += OnFrameReceived;
    }

    protected override void DisconnectHandler(SurfaceView platformView)
    {
        VirtualView.FrameReceived -= OnFrameReceived;
        _renderer?.Dispose();
        _renderer = null;
        base.DisconnectHandler(platformView);
    }

    private void OnFrameReceived(RawVideoFrame frame) => _renderer?.RenderFrame(frame);
}