# CyberiusMAUIWebRTC
Simple WebRTC implementation for .NET MAUI
# MauiWebRtc — NuGet Package Roadmap

> Цель: создать два NuGet пакета для видеозвонков на .NET MAUI без WebView/Blazor
> - `MauiWebRtc` — клиент (Android, iOS, Windows)
> - `MauiWebRtc.Server` — сервер (ASP.NET Core + SignalR)

---

## Финальное API (к чему идём)

```csharp
// MauiProgram.cs
builder.UseMauiWebRtc(options =>
{
    options.SignalingUrl = "https://yourserver.com/videocall";
    options.StunServer = "stun:stun.l.google.com:19302";
    options.TurnServer = "turn:yourserver.com:3478";
});

// ViewModel
await _client.StartLocalCameraAsync();
await _client.CallAsync("user-123");

// XAML
<webrtc:CameraPreviewView Facing="Front" />
<webrtc:RemoteVideoView />
```

---

## Стек

| Слой | Технология |
|---|---|
| Signaling сервер | ASP.NET Core + SignalR |
| WebRTC / RTP / ICE | SIPSorcery (C#) |
| UI | .NET MAUI Custom Handlers |
| Захват камеры Android | Camera2 API (partial class) |
| Захват камеры iOS | AVFoundation (partial class) |
| STUN/TURN | coturn на VPS |

---

## Структура проекта

```
MauiWebRtc/
├── Core/
│   ├── Interfaces/
│   │   ├── ICameraFrameProvider.cs
│   │   ├── IVideoRenderer.cs
│   │   ├── ISignalingService.cs
│   │   └── IWebRtcClient.cs
│   ├── Models/
│   │   ├── RawVideoFrame.cs
│   │   ├── OfferMessage.cs
│   │   ├── AnswerMessage.cs
│   │   └── IceCandidateMessage.cs
│   ├── WebRtcClient.cs
│   └── SignalingService.cs
├── Platforms/
│   ├── Android/
│   │   ├── Camera2FrameProvider.cs
│   │   └── SurfaceVideoRenderer.cs
│   ├── iOS/
│   │   ├── AVFoundationFrameProvider.cs
│   │   └── AVSampleBufferRenderer.cs
│   └── Windows/
│       ├── MediaCaptureFrameProvider.cs
│       └── WinVideoRenderer.cs
├── Controls/
│   ├── CameraPreviewView.cs
│   └── RemoteVideoView.cs
├── Handlers/
│   ├── CameraPreviewHandler.cs
│   └── RemoteVideoHandler.cs
└── MauiWebRtcExtensions.cs

MauiWebRtc.Server/
├── Hubs/
│   └── VideoCallHub.cs
├── Services/
│   ├── RoomManager.cs
│   └── UserTracker.cs
└── Extensions/
    └── WebRtcServerExtensions.cs
```

---

## ФАЗА 1 — Core Foundation

### Signaling Client
- [ ] Подключить `Microsoft.AspNetCore.SignalR.Client` NuGet
- [ ] Создать `ISignalingService` интерфейс с методами:
  - `ConnectAsync(string url)`
  - `SendOfferAsync(string targetId, string sdp)`
  - `SendAnswerAsync(string targetId, string sdp)`
  - `SendIceCandidateAsync(string targetId, string candidate)`
  - Events: `OnOfferReceived`, `OnAnswerReceived`, `OnIceCandidateReceived`
- [ ] Реализовать `SignalingService.cs` на SignalR
- [ ] Добавить логику reconnect при обрыве соединения
- [ ] Добавить хранение `ConnectionId` текущего пользователя

### WebRTC слой (SIPSorcery)
- [ ] Подключить `SIPSorcery` NuGet пакет
- [ ] Создать `IWebRtcClient` интерфейс:
  - `StartLocalCameraAsync()`
  - `CallAsync(string targetUserId)`
  - `HangUpAsync()`
  - `MuteMicrophone(bool mute)`
  - `MuteCamera(bool mute)`
  - Event: `OnRemoteFrameReceived`
  - Event: `OnCallStateChanged`
- [ ] Реализовать `WebRtcClient.cs`:
  - [ ] Создание `RTCPeerConnection`
  - [ ] Добавление аудио и видео треков
  - [ ] Создание SDP Offer
  - [ ] Обработка SDP Answer
  - [ ] Обмен ICE кандидатами
  - [ ] STUN/TURN конфигурация через `RTCConfiguration`
- [ ] Реализовать полный WebRTC handshake flow:
  - Caller: createOffer → setLocalDescription → sendOffer → receiveAnswer → setRemoteDescription
  - Callee: receiveOffer → setRemoteDescription → createAnswer → setLocalDescription → sendAnswer

### Модели
- [ ] `RawVideoFrame` — байты, ширина, высота, формат (I420/YUV)
- [ ] `OfferMessage` — targetId, sdp
- [ ] `AnswerMessage` — targetId, sdp
- [ ] `IceCandidateMessage` — targetId, candidate, sdpMid, sdpMLineIndex
- [ ] `CallState` enum: Idle, Calling, Connected, Ended

---

## ФАЗА 2 — Platform Implementations

### Android — Camera2
- [ ] Создать `ICameraFrameProvider` интерфейс:
  - `StartAsync(CameraFacing facing)`
  - `StopAsync()`
  - Event: `OnFrameAvailable(RawVideoFrame frame)`
- [ ] Реализовать `Camera2FrameProvider.cs` (Platforms/Android):
  - [ ] Получить `CameraManager` через `Context`
  - [ ] Открыть `CameraDevice`
  - [ ] Создать `ImageReader` с форматом `ImageFormatType.Yuv420888`
  - [ ] Реализовать `IOnImageAvailableListener`
  - [ ] Извлекать байты из `Image.Planes`
  - [ ] Конвертировать YUV420 → I420 (если нужно для SIPSorcery)
  - [ ] Обрабатывать переключение камер (фронт/тыл)
- [ ] Подключить фреймы в SIPSorcery через `ExternalVideoSourceRawSample()`
- [ ] Добавить автоматический запрос разрешения `CAMERA`

### Android — Рендер видео
- [ ] Создать `IVideoRenderer` интерфейс:
  - `RenderFrame(RawVideoFrame frame)`
  - `Clear()`
- [ ] Реализовать `SurfaceVideoRenderer.cs` (Platforms/Android):
  - [ ] Использовать `SurfaceView` или `TextureView`
  - [ ] Конвертировать I420 → Bitmap для отрисовки
  - [ ] Рисовать через `Canvas` на `SurfaceHolder`
  - [ ] Обработать rotation/mirror для фронтальной камеры

### iOS — AVFoundation
- [ ] Реализовать `AVFoundationFrameProvider.cs` (Platforms/iOS):
  - [ ] Создать `AVCaptureSession`
  - [ ] Добавить `AVCaptureDeviceInput` (фронт/тыл)
  - [ ] Добавить `AVCaptureVideoDataOutput`
  - [ ] Реализовать `ISampleBufferDelegate` для получения фреймов
  - [ ] Конвертировать `CMSampleBuffer` → байты I420
- [ ] Реализовать `AVSampleBufferRenderer.cs` (Platforms/iOS):
  - [ ] Рендер через `AVSampleBufferDisplayLayer` или Metal (`MTKView`)
- [ ] Добавить `NSCameraUsageDescription` в Info.plist

### Windows
- [ ] Реализовать `MediaCaptureFrameProvider.cs` (Platforms/Windows):
  - [ ] Использовать `Windows.Media.Capture.MediaCapture`
  - [ ] Получать фреймы через `MediaFrameReader`
- [ ] Реализовать `WinVideoRenderer.cs`:
  - [ ] Рендер через `WriteableBitmap` или Direct2D

---

## ФАЗА 3 — MAUI Controls & Handlers

### Controls
- [ ] `CameraPreviewView` — кастомный View:
  - Property: `CameraFacing` (Front/Back)
  - Property: `IsRunning`
  - Method: `StartAsync()`, `StopAsync()`
- [ ] `RemoteVideoView` — кастомный View:
  - Property: `StreamId`
  - Property: `IsMirrored`

### Handlers
- [ ] `CameraPreviewHandler`:
  - Android: маппинг на нативный `SurfaceView` с Camera2
  - iOS: маппинг на `AVCaptureVideoPreviewLayer`
  - Windows: маппинг на `CaptureElement`
- [ ] `RemoteVideoHandler`:
  - Android: маппинг на `SurfaceView` с рендером фреймов
  - iOS: маппинг на `AVSampleBufferDisplayLayer`
  - Windows: маппинг на `Image` с `WriteableBitmap`

### DI и регистрация
- [ ] Создать `MauiWebRtcExtensions.cs`:
  ```csharp
  builder.UseMauiWebRtc(options => { ... })
  ```
  - Регистрирует handlers
  - Регистрирует сервисы в DI: `ISignalingService`, `ICameraFrameProvider`, `IWebRtcClient`
  - Принимает `WebRtcOptions` (SignalingUrl, StunServer, TurnServer)

---

## ФАЗА 4 — Signaling Server

### VideoCallHub
- [ ] Создать SignalR Hub `VideoCallHub`:
  - [ ] `SendOffer(string targetConnectionId, string sdp)`
  - [ ] `SendAnswer(string targetConnectionId, string sdp)`
  - [ ] `SendIceCandidate(string targetConnectionId, string candidate)`
  - [ ] `JoinRoom(string roomId)` — войти в комнату
  - [ ] `LeaveRoom(string roomId)` — покинуть комнату
  - [ ] Override `OnConnectedAsync` / `OnDisconnectedAsync`

### RoomManager
- [ ] Хранить список комнат и участников (in-memory или Redis)
- [ ] Логика: максимум 2 участника на комнату
- [ ] Уведомлять второго участника о входе первого
- [ ] Очищать комнату при дисконнекте

### WebRtcServerExtensions
- [ ] `AddMauiWebRtcServer(options => { ... })`:
  - `MaxRoomSize`
  - `TurnServer`, `TurnUsername`, `TurnPassword`
- [ ] `MapMauiWebRtcHub(string path)` — регистрация endpoint

---

## ФАЗА 5 — Дополнительные фичи

- [ ] Аудио (SIPSorcery уже умеет — подключить микрофон нативно)
- [ ] Mute/Unmute микрофона во время звонка
- [ ] Mute/Unmute камеры (отправка чёрного фрейма)
- [ ] Переключение камеры (фронт ↔ тыл) во время звонка
- [ ] Входящий звонок — уведомление и возможность ответить/отклонить
- [ ] Таймер длительности звонка
- [ ] Запись звонка в файл
- [ ] Screen sharing (Android, Windows)
- [ ] Поддержка группового звонка (SFU режим через SIPSorcery)

---

## ФАЗА 6 — Инфраструктура на VPS

- [ ] Поднять VPS (минимум 1 vCPU, 2GB RAM)
- [ ] Установить Docker
- [ ] Настроить coturn (STUN/TURN):
  ```
  listening-port=3478
  tls-listening-port=5349
  external-ip=<VPS_IP>
  realm=yourdomain.com
  user=myuser:mypassword
  lt-cred-mech
  fingerprint
  ```
- [ ] Опубликовать ASP.NET Core Signaling Server
- [ ] Настроить Caddy или nginx как reverse proxy (SSL автоматически)
- [ ] Открыть порты: 443 (HTTPS/WSS), 3478 UDP/TCP, 5349 TLS
- [ ] Настроить systemd сервис для автозапуска

---

## ФАЗА 7 — Качество и публикация

### Тестирование
- [ ] Unit тесты для `SignalingService`
- [ ] Unit тесты для `WebRtcClient` (handshake flow)
- [ ] Интеграционные тесты SignalR Hub
- [ ] Тестирование на реальных Android устройствах (разные версии OS)
- [ ] Тестирование на iOS устройстве
- [ ] Тестирование через NAT (симметричный NAT — TURN должен работать)
- [ ] Тестирование при плохом соединении (потеря пакетов, задержки)

### NuGet публикация
- [ ] Настроить `MauiWebRtc.csproj`:
  ```xml
  <TargetFrameworks>net9.0-android;net9.0-ios;net9.0-windows10.0.19041.0</TargetFrameworks>
  ```
- [ ] Настроить `MauiWebRtc.Server.csproj`:
  ```xml
  <TargetFrameworks>net9.0</TargetFrameworks>
  ```
- [ ] Заполнить метаданные пакета (версия, описание, лицензия, иконка)
- [ ] Написать README с примерами кода
- [ ] Настроить GitHub Actions для автоматической публикации на NuGet.org
- [ ] Опубликовать `MauiWebRtc` на NuGet.org
- [ ] Опубликовать `MauiWebRtc.Server` на NuGet.org

---

## Зависимости (NuGet)

### MauiWebRtc (клиент)
```
SIPSorcery
SIPSorceryMedia.Abstractions
Microsoft.AspNetCore.SignalR.Client
```

### MauiWebRtc.Server (сервер)
```
Microsoft.AspNetCore.SignalR
```

---

## Примерный порядок работы (по неделям)

| Неделя | Задача |
|---|---|
| 1–2 | Фаза 1: SignalR клиент + SIPSorcery базовая интеграция |
| 3–4 | Фаза 1: Полный WebRTC handshake flow (Offer/Answer/ICE) |
| 5–6 | Фаза 2: Android Camera2 + передача фреймов в SIPSorcery |
| 7 | Фаза 2: Android рендер удалённого видео |
| 8–9 | Фаза 2: iOS AVFoundation + рендер |
| 10 | Фаза 3: MAUI Controls + Handlers |
| 11 | Фаза 4: Signaling Server + RoomManager |
| 12 | Фаза 6: VPS + coturn настройка |
| 13 | Фаза 5: Аудио, mute, переключение камеры |
| 14–15 | Фаза 7: Тестирование на реальных устройствах |
| 16 | Фаза 7: Документация + NuGet публикация |
