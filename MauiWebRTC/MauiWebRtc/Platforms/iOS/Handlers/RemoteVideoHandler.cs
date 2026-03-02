using MauiWebRtc.Controls;
using MauiWebRtc.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using UIKit;

namespace MauiWebRtc.Handlers;

public partial class RemoteVideoHandler : ViewHandler<RemoteVideoView, UIImageView>
{
    private AvSampleBufferRenderer? _renderer;

    protected override UIImageView CreatePlatformView()
        => new() { ContentMode = UIViewContentMode.ScaleAspectFit, BackgroundColor = UIColor.Black };

    protected override void ConnectHandler(UIImageView platformView)
    {
        base.ConnectHandler(platformView);
        var loggerFactory = MauiContext!.Services.GetRequiredService<ILoggerFactory>();
        _renderer = new AvSampleBufferRenderer(platformView, loggerFactory.CreateLogger<AvSampleBufferRenderer>());
        VirtualView.FrameReceived += OnFrameReceived;
    }

    protected override void DisconnectHandler(UIImageView platformView)
    {
        VirtualView.FrameReceived -= OnFrameReceived;
        _renderer?.Dispose();
        _renderer = null;
        base.DisconnectHandler(platformView);
    }

    private void OnFrameReceived(RawVideoFrame frame) => _renderer?.RenderFrame(frame);
}