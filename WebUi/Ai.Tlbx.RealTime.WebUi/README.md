# Ai.Tlbx.RealTime.WebUi

This Razor Class Library (RCL) provides reusable UI components for real-time audio applications built with the Ai.Tlbx.RealTimeAudio library.

## Available Components

### Voice Chat
- **AiTalkControl** - Start/stop voice recording with visual indicators
- **ChatWidget** - Display conversation history with message type styling
- **VoiceSelect** - Select from available AI voices

### Audio Input
- **MicrophoneSelect** - Microphone device selection with permission request
- **MicTestWidget** - Test microphone with visual level indicator

### Status & Feedback
- **StatusWidget** - Connection status and error display
- **ToastNotification** - Toast notifications for non-blocking updates
- **DiagnosticsWidget** - Real-time audio diagnostics (buffer, latency, etc.)

### Layout
- **AppLayout** - Base layout component with CSS imports
- **TwoColumnLayout** - Responsive two-column layout for chat applications

## Usage

### Installation

1. Add a project reference to this library:
   ```
   dotnet add reference ../path/to/Ai.Tlbx.RealTime.WebUi/Ai.Tlbx.RealTime.WebUi.csproj
   ```

2. Import the components in your _Imports.razor:
   ```razor
   @using Ai.Tlbx.RealTime.WebUi.Components
   ```

3. Include the CSS in your main layout:
   ```html
   <link rel="stylesheet" href="_content/Ai.Tlbx.RealTime.WebUi/css/components.css" />
   ```

### Component Examples

#### OpenAI API Access Configuration

To use the components with OpenAI, provide the OpenAiRealTimeApiAccess service via dependency injection:

```razor
@inject OpenAiRealTimeApiAccess rta

<CascadingValue Value="rta">
    <!-- Your components here -->
</CascadingValue>
```

#### AiTalkControl

```razor
<AiTalkControl 
    OnStartTalking="StartSession" 
    OnStopTalking="StopSession" 
    IsTalking="rta.IsRecording" 
    Loading="rta.IsConnecting" />
```

#### MicrophoneSelect

```razor
<MicrophoneSelect 
    AvailableMicrophones="availableMicrophones" 
    SelectedMicrophoneId="selectedMicrophoneId" 
    MicPermissionGranted="micPermissionGranted" 
    OnRequestPermission="RequestMicrophonePermission" />
```

#### ChatWidget

```razor
<ChatWidget />
```

## Styling

The components use a combination of Tailwind-based utility classes and component-specific CSS for styling. You can customize the appearance by overriding the CSS classes in your application.

## JavaScript Dependencies

The components in this library require a JavaScript object called `audioInterop` with the following methods:

```javascript
window.audioInterop = {
    initAudioWithUserInteraction, // Initialize audio context with user gesture
    getAvailableMicrophones,      // Get list of audio input devices
    startRecording,               // Start capturing audio from microphone
    stopRecording,                // Stop capturing audio
    playAudio,                    // Play audio data (PCM format)
    stopAudioPlayback,            // Stop playback and clear buffers
    setDotNetReference,           // Set .NET reference for callbacks
    startMicTest,                 // Start microphone test (loopback)
    stopMicTest                   // Stop microphone test
};
```

## Setup Instructions

1. **Add Project Reference**: Include this RCL in your project by adding a reference to `Ai.Tlbx.RealTime.WebUi.csproj`.
   ```xml
   <ProjectReference Include="../WebUi/Ai.Tlbx.RealTime.WebUi/Ai.Tlbx.RealTime.WebUi.csproj" />
   ```
2. **Register Static Assets**: Ensure your application serves static files from the RCL by adding the following to your `Program.cs` or `Startup.cs`:
   ```csharp
   app.UseStaticFiles();
   app.UseStaticFiles(new StaticFileOptions
   {
       FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")),
       RequestPath = "/Ai.Tlbx.RealTime.WebUi"
   });
   ```
3. **Include CSS**: Add the shared CSS in your layout file (`_Layout.cshtml` or `App.razor`):
   ```html
   <link href="Ai.Tlbx.RealTime.WebUi/css/shared.css" rel="stylesheet" />
   ```
4. **Register AudioInteropService**: Add to your service collection
   ```csharp
   builder.Services.AddScoped<IAudioInteropService, AudioInteropService>();
   ```

## Component Usage

### ChatWidget
Used for chat interactions with real-time audio.
```razor
<ChatWidget OnMessageSent="HandleMessageSent" />

@code {
    private async Task HandleMessageSent(string message)
    {
        // Process the sent message
        Console.WriteLine($"Message sent: {message}");
    }
}
```

### AiTalkControl
Controls for starting and stopping AI talk interactions.
```razor
<AiTalkControl OnStartTalking="StartAiTalk" OnStopTalking="StopAiTalk" Loading="IsConnecting" />

@code {
    private async Task StartAiTalk()
    {
        // Start AI interaction logic
        Console.WriteLine("AI talk started");
    }

    private async Task StopAiTalk()
    {
        // Stop AI interaction logic
        Console.WriteLine("AI talk stopped");
    }
}
```

### MicTestWidget
UI for testing microphone input.
```razor
<MicTestWidget OnStartTest="StartMicTest" Loading="IsTesting" />

@code {
    private async Task StartMicTest()
    {
        // Start microphone test logic
        Console.WriteLine("Microphone test started");
    }
}
```

### StatusWidget
Displays status and error messages.
```razor
<StatusWidget ConnectionStatus="Status" Error="ErrorMessage" IsMicrophoneTesting="IsTesting" />
```

### VoiceSelect
Select from a list of available voices.
```razor
<VoiceSelect SelectedVoice="Voice" SelectedVoiceChanged="VoiceChanged" Disabled="false" />
```

### DiagnosticsWidget
Displays audio diagnostic information (buffer levels, latency, etc.)
```razor
<DiagnosticsWidget InitiallyExpanded="false" 
                   BufferLevel="BufferLevel" 
                   Latency="Latency" 
                   SampleRate="SampleRate" 
                   AudioChunksProcessed="Chunks" />
```

### ToastNotification
Displays non-blocking notifications to the user.
```razor
<ToastNotification @ref="toastNotification" AutoHideMilliseconds="5000" />

@code {
    private ToastNotification? toastNotification;
    
    private async Task ShowMessage() {
        if (toastNotification != null) {
            await toastNotification.ShowAsync("Message", "Title", ToastNotification.ToastType.Info);
        }
    }
}
``` 