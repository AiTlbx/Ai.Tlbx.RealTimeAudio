# Voice Assistant v4.0 Architecture Refactoring Plan

## Executive Summary

**Objective**: Refactor the monolithic `OpenAiRealTimeApiAccess` class into a clean composition-based architecture that supports multiple AI providers (OpenAI, Google, xAI) without breaking UI integration patterns.

**Key Motivation**: The current `OpenAiRealTimeApiAccess` class mixes two distinct concerns:
1. **Voice Assistant Orchestration** - Hardware management, chat flow, start/stop/interrupt operations
2. **OpenAI API Implementation** - WebSocket protocols, OpenAI-specific message formats

This tight coupling prevents adding other AI providers without duplicating orchestration logic.

**Solution**: Extract orchestration into a provider-agnostic `VoiceAssistant` class that composes with provider-specific implementations via `IVoiceProvider` interface.

**Breaking Changes**: This is version 4.0 with intentional breaking changes. No backward compatibility concerns.

## Current Problems

1. **Tight Coupling**: Cannot add Google/xAI providers without duplicating hardware management and chat flow logic
2. **Naming Conflict**: "RealTime" term is OpenAI-specific, conflicts with multi-provider vision  
3. **Monolithic Design**: Single class handles everything from WebSocket protocols to hardware orchestration
4. **Provider Lock-in**: UI code is forced to use OpenAI-specific models and interfaces

## Target Architecture (Composition-Based)

### Core Principle
**Composition over Inheritance**: Separate orchestrator composes with pluggable provider implementations.

```
VoiceAssistant (orchestrator)
├── IVoiceProvider (pluggable AI provider)
├── IAudioHardwareAccess (unchanged)
└── ChatHistoryManager (provider-agnostic)
```

## Project Structure & NuGet Packages

### New Package Structure
```
Provider/
├── Ai.Tlbx.VoiceAssistant/                        # Main orchestrator package
│   ├── VoiceAssistant.cs                          # Main user-facing orchestrator
│   ├── Interfaces/
│   │   ├── IVoiceProvider.cs                      # Provider interface
│   │   ├── IVoiceSettings.cs                      # Settings interface
│   │   ├── IVoiceTool.cs                          # Tool interface
│   │   └── IAudioHardwareAccess.cs                # Moved from OpenAI package
│   ├── Models/
│   │   ├── ChatMessage.cs                         # Provider-agnostic message
│   │   ├── AudioDeviceInfo.cs                     # Moved from OpenAI package
│   │   ├── LoggingModels.cs                       # Moved from OpenAI package
│   │   └── VoiceAssistantFactory.cs               # Factory for provider creation
│   └── Managers/
│       └── ChatHistoryManager.cs                  # Provider-agnostic chat history
│
├── Ai.Tlbx.VoiceAssistant.Provider.OpenAi/        # OpenAI provider package
│   ├── OpenAiVoiceProvider.cs                     # IVoiceProvider implementation
│   ├── OpenAiVoiceSettings.cs                     # OpenAI-specific settings
│   ├── Models/
│   │   ├── OpenAiVoice.cs                         # OpenAI voice enum
│   │   ├── TurnDetectionSettings.cs               # OpenAI turn detection
│   │   └── OpenAiChatMessage.cs                   # OpenAI message format
│   ├── Internal/
│   │   ├── WebSocketConnection.cs                 # OpenAI WebSocket logic
│   │   ├── MessageProcessor.cs                    # OpenAI protocol handling
│   │   ├── SessionConfigurator.cs                 # OpenAI session setup
│   │   └── AudioStreamManager.cs                  # OpenAI audio management
│   └── Tools/
│       └── OpenAiRealTimeTool.cs                  # OpenAI tool base class
│
├── Ai.Tlbx.VoiceAssistant.Provider.Google/        # Future Google provider
│   ├── GoogleVoiceProvider.cs
│   ├── GoogleVoiceSettings.cs
│   └── Internal/
│       └── GoogleWebRtcConnection.cs
│
└── Ai.Tlbx.VoiceAssistant.Provider.Xai/           # Future xAI provider
    ├── XaiVoiceProvider.cs
    ├── XaiVoiceSettings.cs
    └── Internal/
        └── XaiApiConnection.cs

Hardware/                                           # Updated package references
├── Ai.Tlbx.VoiceAssistant.Hardware.Windows/       # Now references main package
├── Ai.Tlbx.VoiceAssistant.Hardware.Linux/         # Now references main package
└── Ai.Tlbx.VoiceAssistant.Hardware.Web/           # Now references main package

WebUi/                                              # Updated package references  
└── Ai.Tlbx.VoiceAssistant.WebUi/                  # Now references main package

Demo/                                               # Updated package references
├── Ai.Tlbx.VoiceAssistant.Demo.Windows/           # Updated to use new API
├── Ai.Tlbx.VoiceAssistant.Demo.Linux/             # Updated to use new API
└── Ai.Tlbx.VoiceAssistant.Demo.Web/               # Updated to use new API
```

### NuGet Package Dependencies
```
Ai.Tlbx.VoiceAssistant (v4.0.0)
└── No external dependencies (pure interfaces and orchestration)

Ai.Tlbx.VoiceAssistant.Provider.OpenAi (v4.0.0)
├── → Ai.Tlbx.VoiceAssistant
└── → System.Net.WebSockets.Client

Ai.Tlbx.VoiceAssistant.Provider.Google (v4.0.0)
├── → Ai.Tlbx.VoiceAssistant  
└── → Google.Cloud.Speech.V1 (or equivalent)

Ai.Tlbx.VoiceAssistant.Hardware.Windows (v4.0.0)
├── → Ai.Tlbx.VoiceAssistant (was OpenAI package before)
└── → NAudio

Ai.Tlbx.VoiceAssistant.WebUi (v4.0.0)
├── → Ai.Tlbx.VoiceAssistant (was OpenAI package before)
└── → Microsoft.AspNetCore.Components.Web
```

## Core Interfaces

### IVoiceProvider Interface
```csharp
public interface IVoiceProvider : IAsyncDisposable
{
    // Connection state
    bool IsConnected { get; }
    
    // Core operations
    Task ConnectAsync(IVoiceSettings settings);
    Task DisconnectAsync();
    Task ProcessAudioAsync(string base64Audio);
    Task SendInterruptAsync();
    
    // Provider → Orchestrator callbacks
    Action<ChatMessage>? OnMessageReceived { get; set; }
    Action<string>? OnStatusChanged { get; set; }
    Action<string>? OnError { get; set; }
}
```

### IVoiceSettings Interface
```csharp
public interface IVoiceSettings
{
    string Instructions { get; set; }
    List<IVoiceTool> Tools { get; set; }
}
```

### Main Orchestrator Class
```csharp
public class VoiceAssistant : IAsyncDisposable
{
    private readonly IAudioHardwareAccess _hardware;
    private readonly IVoiceProvider _provider;
    private readonly ChatHistoryManager _chatHistory;
    
    // Clean callback API (same pattern as current v3)
    public Action<string>? OnConnectionStatusChanged { get; set; }
    public Action<ChatMessage>? OnMessageAdded { get; set; }
    public Action<List<AudioDeviceInfo>>? OnMicrophoneDevicesChanged { get; set; }
    
    // State properties
    public bool IsRecording => _hardware.IsRecording;
    public bool IsConnected => _provider.IsConnected;
    public IReadOnlyList<ChatMessage> ChatHistory => _chatHistory.Messages;
    
    // Core operations
    public async Task StartAsync(IVoiceSettings settings);
    public async Task StopAsync();
    public async Task InterruptAsync();
    public async Task<bool> TestMicrophoneAsync();
}
```

## Implementation Phases

### Phase 1: Create Main Package Structure
**Goal**: Establish the main `Ai.Tlbx.VoiceAssistant` package with core interfaces

**Actions**:
- Create new `Ai.Tlbx.VoiceAssistant` project
- Define `IVoiceProvider` interface with connection, audio processing, and callback methods
- Define `IVoiceSettings` interface for provider-agnostic settings
- Define `IVoiceTool` interface for provider-agnostic tools
- Move `IAudioHardwareAccess` interface from current OpenAI project to main package
- Create `VoiceAssistant` orchestrator class with composition-based design
- Create provider-agnostic `ChatMessage` model (remove OpenAI-specific fields)
- Move and adapt `ChatHistoryManager` from OpenAI project to work with generic `ChatMessage`
- Create `VoiceAssistantFactory` class with factory methods

**Key Files Created**:
- `Ai.Tlbx.VoiceAssistant/VoiceAssistant.cs`
- `Ai.Tlbx.VoiceAssistant/Interfaces/IVoiceProvider.cs`
- `Ai.Tlbx.VoiceAssistant/Interfaces/IVoiceSettings.cs` 
- `Ai.Tlbx.VoiceAssistant/Models/ChatMessage.cs`
- `Ai.Tlbx.VoiceAssistant/VoiceAssistantFactory.cs`

### Phase 2: Extract OpenAI Provider
**Goal**: Transform current OpenAI implementation into pluggable provider

**Actions**:
- Rename project: `Ai.Tlbx.RealTimeAudio.OpenAi` → `Ai.Tlbx.VoiceAssistant.Provider.OpenAi`
- Add package reference to main `Ai.Tlbx.VoiceAssistant` package
- **DELETE** `OpenAiRealTimeApiAccess` class entirely (breaking change)
- Create `OpenAiVoiceProvider` class implementing `IVoiceProvider` interface
- Create `OpenAiVoiceSettings` class implementing `IVoiceSettings` interface
- Move OpenAI-specific models (`OpenAiVoice`, `TurnDetectionSettings`) to provider package
- Keep all `Internal/` classes (`WebSocketConnection`, `MessageProcessor`, `SessionConfigurator`, `AudioStreamManager`) in provider package
- Adapt `MessageProcessor` to call provider callbacks instead of events
- Update tool system to work with provider interface

**Key Files**:
- `Ai.Tlbx.VoiceAssistant.Provider.OpenAi/OpenAiVoiceProvider.cs` (NEW - implements IVoiceProvider)
- `Ai.Tlbx.VoiceAssistant.Provider.OpenAi/OpenAiVoiceSettings.cs` (NEW - implements IVoiceSettings)
- `Ai.Tlbx.VoiceAssistant.Provider.OpenAi/Internal/WebSocketConnection.cs` (KEPT)
- `Ai.Tlbx.VoiceAssistant.Provider.OpenAi/Internal/MessageProcessor.cs` (ADAPTED)

**Files Deleted**:
- `OpenAiRealTimeApiAccess.cs` (completely removed)

### Phase 3: Update Hardware Package Dependencies
**Goal**: Decouple hardware packages from OpenAI-specific dependencies

**Actions**:
- Update `Ai.Tlbx.RealTimeAudio.Hardware.Windows` project references:
  - Remove reference to `Ai.Tlbx.RealTimeAudio.OpenAi` 
  - Add reference to `Ai.Tlbx.VoiceAssistant`
- Rename package to `Ai.Tlbx.VoiceAssistant.Hardware.Windows`
- Update `Ai.Tlbx.RealTimeAudio.Hardware.Linux` project references:
  - Remove reference to `Ai.Tlbx.RealTimeAudio.OpenAi`
  - Add reference to `Ai.Tlbx.VoiceAssistant` 
- Rename package to `Ai.Tlbx.VoiceAssistant.Hardware.Linux`
- Update `Ai.Tlbx.RealTimeAudio.Hardware.Web` project references:
  - Remove reference to `Ai.Tlbx.RealTimeAudio.OpenAi`
  - Add reference to `Ai.Tlbx.VoiceAssistant`
- Rename package to `Ai.Tlbx.VoiceAssistant.Hardware.Web`
- Update all using statements in hardware packages

### Phase 4: Update UI and Demo Dependencies
**Goal**: Update all consumer projects to use new package structure

**Actions**:
- Update `Ai.Tlbx.RealTime.WebUi` project:
  - Remove reference to `Ai.Tlbx.RealTimeAudio.OpenAi`
  - Add reference to `Ai.Tlbx.VoiceAssistant`
  - Rename package to `Ai.Tlbx.VoiceAssistant.WebUi`
  - Update component code to use new `VoiceAssistant` class instead of `OpenAiRealTimeApiAccess`
- Update demo projects:
  - `Ai.Tlbx.RealTimeAudio.Demo.Windows` → `Ai.Tlbx.VoiceAssistant.Demo.Windows`
  - `Ai.Tlbx.RealTimeAudio.Demo.Linux` → `Ai.Tlbx.VoiceAssistant.Demo.Linux`  
  - `Ai.Tlbx.RealTimeAudio.Demo.Web` → `Ai.Tlbx.VoiceAssistant.Demo.Web`
- Update all demo code to use new factory pattern and `VoiceAssistant` class
- Update package references in all demo projects

### Phase 5: Implement Factory Pattern
**Goal**: Provide clean, intuitive API for provider selection

**Actions**:
- Implement `VoiceAssistantFactory.CreateOpenAi()` method in main package
- Implement generic `VoiceAssistantFactory.Create<TProvider>()` method for extensibility
- Add convenient static methods for common provider scenarios
- Document usage patterns for different providers

**Factory Implementation**:
```csharp
public static class VoiceAssistantFactory
{
    public static VoiceAssistant CreateOpenAi(
        IAudioHardwareAccess hardware, 
        string apiKey, 
        Action<LogLevel, string>? logger = null)
    {
        var provider = new OpenAiVoiceProvider(apiKey, logger);
        return new VoiceAssistant(hardware, provider, logger);
    }
    
    public static VoiceAssistant Create<TProvider>(
        IAudioHardwareAccess hardware, 
        TProvider provider, 
        Action<LogLevel, string>? logger = null)
        where TProvider : IVoiceProvider
    {
        return new VoiceAssistant(hardware, provider, logger);
    }
}
```

## File Migrations and Transformations

### Files Moving from OpenAI Package to Main Package
- `IAudioHardwareAccess.cs` → `Ai.Tlbx.VoiceAssistant/Interfaces/`
- `AudioDeviceInfo.cs` → `Ai.Tlbx.VoiceAssistant/Models/`
- `LoggingModels.cs` → `Ai.Tlbx.VoiceAssistant/Models/`
- `ChatHistoryManager.cs` → `Ai.Tlbx.VoiceAssistant/Managers/` (adapted for generic ChatMessage)

### Files Staying in OpenAI Provider Package
- `Internal/WebSocketConnection.cs` (OpenAI-specific WebSocket protocol)
- `Internal/MessageProcessor.cs` (OpenAI message format handling)
- `Internal/SessionConfigurator.cs` (OpenAI session configuration)
- `Internal/AudioStreamManager.cs` (OpenAI audio streaming)
- `Tools/RealTimeTool.cs` → `Tools/OpenAiVoiceTool.cs`

### Files Being Deleted
- `OpenAiRealTimeApiAccess.cs` (replaced by VoiceAssistant + OpenAiVoiceProvider composition)

### Files Being Created
- `Ai.Tlbx.VoiceAssistant/VoiceAssistant.cs` (main orchestrator)
- `Ai.Tlbx.VoiceAssistant.Provider.OpenAi/OpenAiVoiceProvider.cs` (provider implementation)
- `Ai.Tlbx.VoiceAssistant.Provider.OpenAi/OpenAiVoiceSettings.cs` (provider settings)

## NuGet Packaging and Deployment Scripts

### Updated publish-nuget.ps1 Script
The current `publish-nuget.ps1` script needs to be updated to handle multiple packages with the new naming scheme.

**Required Changes**:
- Support multiple project paths and package names
- Update version.txt to track main package version (all packages use same version)
- Handle dependency references between packages
- Support publishing packages in correct order (main package first, then providers and hardware)

**New Script Structure**:
```powershell
# Updated publish-nuget.ps1 
# Publishes all VoiceAssistant packages in correct dependency order

$version = Get-Content "version.txt"
$newVersion = [Version]::new($version).ToString()

# Package publishing order (dependencies first)
$packages = @(
    @{Path="Provider/Ai.Tlbx.VoiceAssistant"; Name="Ai.Tlbx.VoiceAssistant"},
    @{Path="Provider/Ai.Tlbx.VoiceAssistant.Provider.OpenAi"; Name="Ai.Tlbx.VoiceAssistant.Provider.OpenAi"},
    @{Path="Hardware/Ai.Tlbx.VoiceAssistant.Hardware.Windows"; Name="Ai.Tlbx.VoiceAssistant.Hardware.Windows"},
    @{Path="Hardware/Ai.Tlbx.VoiceAssistant.Hardware.Linux"; Name="Ai.Tlbx.VoiceAssistant.Hardware.Linux"},
    @{Path="Hardware/Ai.Tlbx.VoiceAssistant.Hardware.Web"; Name="Ai.Tlbx.VoiceAssistant.Hardware.Web"},
    @{Path="WebUi/Ai.Tlbx.VoiceAssistant.WebUi"; Name="Ai.Tlbx.VoiceAssistant.WebUi"}
)

foreach ($package in $packages) {
    Write-Host "Publishing $($package.Name) v$newVersion..."
    dotnet pack $package.Path -c Release
    dotnet nuget push "nupkg/$($package.Name).$newVersion.nupkg" --source https://api.nuget.org/v3/index.json
}
```

### Version Management
- Single `version.txt` file continues to control all package versions
- All packages in the VoiceAssistant family use the same version number
- Version increments apply to all packages simultaneously

## User Experience After Migration

### Package Installation
```bash
# Install core orchestrator
Install-Package Ai.Tlbx.VoiceAssistant

# Install OpenAI provider
Install-Package Ai.Tlbx.VoiceAssistant.Provider.OpenAi

# Install hardware support
Install-Package Ai.Tlbx.VoiceAssistant.Hardware.Windows
```

### Code Usage Pattern
```csharp
// v4.0 API using composition and factory
using Ai.Tlbx.VoiceAssistant;
using Ai.Tlbx.VoiceAssistant.Provider.OpenAi;
using Ai.Tlbx.VoiceAssistant.Hardware.Windows;

var hardware = new WindowsAudioHardware();
var assistant = VoiceAssistantFactory.CreateOpenAi(hardware, apiKey, logger);

assistant.OnMessageAdded = message => UpdateUI(message);
assistant.OnConnectionStatusChanged = status => UpdateStatus(status);

await assistant.StartAsync(new OpenAiVoiceSettings 
{
    Voice = OpenAiVoice.Nova,
    Instructions = "You are a helpful assistant"
});
```

### Future Provider Support
```csharp
// Future Google provider support (same pattern)
var assistant = VoiceAssistantFactory.CreateGoogle(hardware, googleApiKey, logger);
await assistant.StartAsync(new GoogleVoiceSettings 
{
    Voice = GoogleVoice.Neural2,
    Instructions = "You are a helpful assistant"
});
```

## Breaking Changes Documentation

### v3.x → v4.0 Breaking Changes
1. **Package Names**: All packages renamed from `Ai.Tlbx.RealTimeAudio.*` to `Ai.Tlbx.VoiceAssistant.*`
2. **Main Class**: `OpenAiRealTimeApiAccess` deleted, replaced with `VoiceAssistant` + provider composition
3. **Factory Pattern**: Must use `VoiceAssistantFactory.CreateOpenAi()` or manual composition
4. **Settings Classes**: `OpenAiRealTimeSettings` → `OpenAiVoiceSettings`
5. **Hardware Dependencies**: Hardware packages now reference main package instead of OpenAI package
6. **Callback Naming**: Maintain same callback pattern (OnMessageAdded, etc.) for UI compatibility

### Migration Guide for Existing Users
```csharp
// v3.x code
var api = new OpenAiRealTimeApiAccess(hardware, logger);
api.OnMessageAdded = msg => UpdateUI(msg);
await api.Start(new OpenAiRealTimeSettings { Voice = AssistantVoice.Nova });

// v4.0 equivalent
var assistant = VoiceAssistantFactory.CreateOpenAi(hardware, apiKey, logger);
assistant.OnMessageAdded = msg => UpdateUI(msg);
await assistant.StartAsync(new OpenAiVoiceSettings { Voice = OpenAiVoice.Nova });
```

## Success Criteria

### Technical Validation
- [ ] All packages build successfully with new structure
- [ ] All existing demo applications work with new API
- [ ] WebUI components work with new orchestrator
- [ ] Factory pattern creates working VoiceAssistant instances
- [ ] OpenAI provider maintains all current functionality
- [ ] Hardware packages work with main package instead of OpenAI package
- [ ] NuGet publish script works with new package structure

### Architectural Validation  
- [ ] Clear separation between orchestration and provider concerns
- [ ] Easy to add new providers by implementing IVoiceProvider
- [ ] No OpenAI-specific dependencies in main package
- [ ] Callback patterns remain consistent for UI compatibility
- [ ] Settings hierarchy supports provider-specific extensions

### User Experience Validation
- [ ] Package installation is intuitive (main + provider + hardware)
- [ ] Factory methods provide clean API
- [ ] Migration path from v3.x is documented and straightforward
- [ ] Future provider addition requires no changes to existing UI code