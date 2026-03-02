
using MauiWebRtc.Controls;

namespace MauiWebRtc.Handlers;

/// <summary>
/// Базовый класс — только маппер свойств.
/// Нативный тип и рендерер реализованы в partial классах по платформам.
/// </summary>
public partial class RemoteVideoHandler
{
    public static PropertyMapper<RemoteVideoView, RemoteVideoHandler> Mapper = new(ViewMapper)
    {
        [nameof(RemoteVideoView.IsMirrored)] = MapIsMirrored,
    };

    public RemoteVideoHandler() : base(Mapper) { }

    private static void MapIsMirrored(RemoteVideoHandler handler, RemoteVideoView view) { }
}