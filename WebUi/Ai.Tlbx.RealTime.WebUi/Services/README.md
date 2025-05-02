# OpenAI RealTime API Access in Components

This document explains how to properly use the `OpenAiRealTimeApiAccess` service in components.

## Using CascadingParameter

The recommended approach is to use a CascadingParameter to access the service:

```razor
@using Ai.Tlbx.RealTimeAudio.OpenAi

@code {
    [CascadingParameter] private OpenAiRealTimeApiAccess RealTimeApi { get; set; } = null!;
    
    protected override void OnInitialized()
    {
        if (RealTimeApi == null)
        {
            throw new InvalidOperationException(
                "This component requires a cascading parameter of type OpenAiRealTimeApiAccess. " +
                "Please ensure you have included <CascadingValue Value=\"rtaService\"> in a parent component.");
        }
        
        // Register to events
        RealTimeApi.ConnectionStatusChanged += OnConnectionStatusChanged;
    }
    
    private void OnConnectionStatusChanged(object? sender, string status)
    {
        // Handle status change
        StateHasChanged();
    }
    
    public void Dispose()
    {
        // Always unsubscribe from events
        if (RealTimeApi != null)
        {
            RealTimeApi.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }
    }
}
```

## Providing the Service

In your main layout or page, provide the service like this:

```razor
@page "/"
@using Ai.Tlbx.RealTimeAudio.OpenAi
@inject OpenAiRealTimeApiAccess rta

<CascadingValue Value="rta">
    <div>
        <!-- Your content here -->
        <YourComponent />
    </div>
</CascadingValue>
```

## Using the OpenAiAccessHelper

Alternatively, you can use the `OpenAiAccessHelper` class:

```csharp
@using Ai.Tlbx.RealTime.WebUi.Services
@inject OpenAiRealTimeApiAccess rta

@OpenAiAccessHelper.ProvideCascadingApiAccess(rta, @<YourComponent />)
```

## Common Use Cases

1. Start/stop recording
2. Get microphone devices
3. Register for status updates
4. Send user messages

```csharp
// Start recording
await RealTimeApi.Start();

// Stop recording
await RealTimeApi.Stop();

// Get microphones
var microphones = await RealTimeApi.GetAvailableMicrophones();

// Send a message
await RealTimeApi.SendUserMessageAsync("Hello AI!");
``` 