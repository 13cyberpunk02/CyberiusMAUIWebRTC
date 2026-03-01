using MauiWebRtc.Core.Models;

namespace MauiWebRtc.Core.Interfaces;

/// <summary>
/// Платформенный рендерер видео.
/// Принимает I420-фреймы и рисует их на нативный View.
/// </summary>
public interface IVideoRenderer
{
    /// <summary>Отрисовать фрейм. Вызывается из любого потока.</summary>
    void RenderFrame(RawVideoFrame frame);

    /// <summary>Очистить поверхность (чёрный экран)</summary>
    void Clear();
}