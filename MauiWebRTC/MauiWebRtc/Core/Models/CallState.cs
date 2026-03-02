namespace MauiWebRtc.Core.Models;

public enum CallState
{
    Idle,
    Calling,        // Мы звоним, ждём ответа
    Receiving,      // Нам звонят, ждём нашего ответа
    Connecting,     // ICE negotiation в процессе
    Connected,      // Звонок установлен
    Ended           // Звонок завершён
}