# Ai.Tlbx.RealTime.WebUi

A Razor Class Library for real-time audio interaction UI components.

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
<AiTalkControl OnStartTalking="StartAiTalk" OnStopTalking="StopAiTalk" />

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
<MicTestWidget OnStartTest="StartMicTest" />

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
<StatusWidget @ref="statusWidget" />

@code {
    private StatusWidget statusWidget;

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            statusWidget.UpdateStatus("Initialized", "blue");
        }
    }

    private void ShowErrorExample()
    {
        statusWidget.ShowError("Connection failed");
    }
}
``` 