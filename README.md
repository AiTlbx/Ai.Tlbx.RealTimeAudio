# Ai.Tlbx.RealTimeAudio

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/download)

A toolkit for real-time audio processing in .NET applications with seamless integration with AI services.

## Version 3.0.0

This major release includes:
- Updated to use the latest OpenAI Realtime API model (`gpt-4o-realtime-preview-2025-06-03`)
- Enhanced audio processing capabilities
- Improved cross-platform support for Windows, Linux, and Web platforms

## GitHub Repository

[https://github.com/AiTlbx/Ai.Tlbx.RealTimeAudio](https://github.com/AiTlbx/Ai.Tlbx.RealTimeAudio)

## Packages

This toolkit offers the following NuGet packages:

### Ai.Tlbx.RealTimeAudio.OpenAi

[![NuGet](https://img.shields.io/nuget/v/Ai.Tlbx.RealTimeAudio.OpenAi.svg)](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.OpenAi/)
[![NuGet](https://img.shields.io/nuget/dt/Ai.Tlbx.RealTimeAudio.OpenAi.svg)](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.OpenAi/)

Real-time audio processing library with OpenAI integration for speech recognition, transcription, and analysis.

**NuGet Package**: [https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.OpenAi/](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.OpenAi/)

#### Installation

```
dotnet add package Ai.Tlbx.RealTimeAudio.OpenAi
```

#### Usage

```csharp
using Ai.Tlbx.RealTimeAudio.OpenAi;

// Initialize the OpenAI client
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var openAiClient = new OpenAiAudioClient(apiKey);

// Process audio in real-time
await using (var audioStream = await openAiClient.CreateStreamingSessionAsync())
{
    // Connect to audio input source
    audioStream.StartCapture();
    
    // Handle real-time transcription
    audioStream.OnTranscription += (sender, text) =>
    {
        Debug.WriteLine($"Transcription: {text}");
    };
    
    // Wait for completion
    await Task.Delay(TimeSpan.FromMinutes(1));
    
    // Stop capturing
    audioStream.StopCapture();
}
```

### Ai.Tlbx.RealTimeAudio.Hardware.Windows

[![NuGet](https://img.shields.io/nuget/v/Ai.Tlbx.RealTimeAudio.Hardware.Windows.svg)](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Windows/)
[![NuGet](https://img.shields.io/nuget/dt/Ai.Tlbx.RealTimeAudio.Hardware.Windows.svg)](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Windows/)

Windows-specific hardware integration for real-time audio processing, supporting microphone access, audio device management, and low-latency playback.

**NuGet Package**: [https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Windows/](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Windows/)

#### Installation

```
dotnet add package Ai.Tlbx.RealTimeAudio.Hardware.Windows
```

#### Usage

```csharp
using Ai.Tlbx.RealTimeAudio.Hardware.Windows;

// List available audio devices
var devices = AudioDeviceManager.GetAvailableInputDevices();
foreach (var device in devices)
{
    Debug.WriteLine($"Device: {device.Name}, ID: {device.Id}");
}

// Initialize an audio capture session
var captureSession = new AudioCaptureSession(sampleRate: 44100, channels: 1);

// Start capturing audio
captureSession.StartCapture(deviceId: devices[0].Id);

// Handle audio data
captureSession.OnAudioDataAvailable += (sender, data) =>
{
    // Process audio buffer
    Debug.WriteLine($"Received {data.Length} audio samples");
};

// Wait for some time
await Task.Delay(TimeSpan.FromSeconds(10));

// Stop capturing
captureSession.StopCapture();
```

### Ai.Tlbx.RealTimeAudio.Hardware.Linux

[![NuGet](https://img.shields.io/nuget/v/Ai.Tlbx.RealTimeAudio.Hardware.Linux.svg)](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Linux/)
[![NuGet](https://img.shields.io/nuget/dt/Ai.Tlbx.RealTimeAudio.Hardware.Linux.svg)](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Linux/)

Linux-specific hardware integration for real-time audio processing using ALSA (Advanced Linux Sound Architecture).

**NuGet Package**: [https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Linux/](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Linux/)

#### Installation

```
dotnet add package Ai.Tlbx.RealTimeAudio.Hardware.Linux
```

### Ai.Tlbx.RealTimeAudio.Hardware.Web

[![NuGet](https://img.shields.io/nuget/v/Ai.Tlbx.RealTimeAudio.Hardware.Web.svg)](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Web/)
[![NuGet](https://img.shields.io/nuget/dt/Ai.Tlbx.RealTimeAudio.Hardware.Web.svg)](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Web/)

Web-based audio integration for real-time audio processing in Blazor applications using Web Audio API.

**NuGet Package**: [https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Web/](https://www.nuget.org/packages/Ai.Tlbx.RealTimeAudio.Hardware.Web/)

#### Installation

```
dotnet add package Ai.Tlbx.RealTimeAudio.Hardware.Web
```

## Requirements

- .NET 9.0 or later
- Windows 10 or later (for Windows-specific components)
- Linux with ALSA support (for Linux-specific components)
- Modern web browser with Web Audio API support (for Web components)

## Architecture & Development

- **Code Style**: See [`CodeStyleGuide.md`](CodeStyleGuide.md) for German-style coding conventions
- **Logging Strategy**: See [`LoggingStrategy.md`](LoggingStrategy.md) for centralized logging architecture
- **Development Guide**: See [`CLAUDE.md`](CLAUDE.md) for development environment setup

**⚠️ Important**: This codebase uses a centralized logging pattern. **Do not use `ILogger<T>` or Microsoft.Extensions.Logging** - all logging flows up to `OpenAiRealTimeApiAccess` where users configure their preferred logging approach.

## License

MIT 



Live long and prosper 🖖
