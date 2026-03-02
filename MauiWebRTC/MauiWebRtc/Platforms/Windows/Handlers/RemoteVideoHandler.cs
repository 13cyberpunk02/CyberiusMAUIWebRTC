using Microsoft.Maui.Handlers;
using MauiWebRtc.Controls;
using MauiWebRtc.Core.Models;
using Microsoft.Extensions.Logging;
using Image = Microsoft.UI.Xaml.Controls.Image;

namespace MauiWebRtc.Handlers;

public partial class RemoteVideoHandler : ViewHandler<RemoteVideoView, Image>
{
    private WinVideoRenderer? _renderer;

    protected override Image CreatePlatformView() => new();

    protected override void ConnectHandler(Image platformView)
    {
        base.ConnectHandler(platformView);
        var loggerFactory = MauiContext!.Services.GetRequiredService<ILoggerFactory>();
        _renderer = new WinVideoRenderer(platformView, loggerFactory.CreateLogger<WinVideoRenderer>());
        VirtualView.FrameReceived += OnFrameReceived;
    }

    protected override void DisconnectHandler(Image platformView)
    {
        VirtualView.FrameReceived -= OnFrameReceived;
        _renderer?.Dispose();
        _renderer = null;
        base.DisconnectHandler(platformView);
    }

    private void OnFrameReceived(RawVideoFrame frame) => _renderer?.RenderFrame(frame);
}