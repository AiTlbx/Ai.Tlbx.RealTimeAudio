# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Important Shell Commands

**ALWAYS use `pwsh` instead of `powershell`** - This system uses PowerShell Core (pwsh) as the default shell.

### PowerShell Execution Policy
Before running PowerShell scripts, the execution policy needs to be set ONCE per session:
```bash
pwsh -Command "Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process"
```

If you encounter execution policy errors, ask the user to run the above command once for their session.

## Common Development Commands

Build the entire solution:
```bash
dotnet build TLBX.Ai.RealTimeAudio.slnx
```

Build and generate NuGet packages:
```bash
dotnet pack TLBX.Ai.RealTimeAudio.slnx
```

Run specific demo applications:
```bash
# Windows Demo
dotnet run --project Demo/Ai.Tlbx.RealTimeAudio.Demo.Windows/Ai.Tlbx.RealTimeAudio.Demo.Windows.csproj

# Linux Demo
dotnet run --project Demo/Ai.Tlbx.RealTimeAudio.Demo.Linux/Ai.Tlbx.RealTimeAudio.Demo.Linux.csproj

# Web Demo
dotnet run --project Demo/Ai.Tlbx.RealTimeAudio.Demo.Web/Ai.Tlbx.RealTimeAudio.Demo.Web.csproj
```

Clean build artifacts:
```bash
dotnet clean TLBX.Ai.RealTimeAudio.slnx
```

Publish NuGet packages (auto-increment version):
```bash
pwsh publish-nuget.ps1
```

Deep clean of bin/obj folders:
```bash
cleanBinObj.cmd
```

## High-Level Architecture

This is a real-time audio processing toolkit that integrates with AI services (primarily OpenAI). The architecture follows a layered approach with clear separation between platform-specific audio handling and cross-platform AI integration.

### Core Components

1. **Provider Layer** (`Provider/Ai.Tlbx.RealTimeAudio.OpenAi/`)
   - `OpenAiRealTimeApiAccess`: Main orchestrator that manages WebSocket connections to OpenAI's real-time API, handles audio streaming, transcription, and conversation state
   - Implements event-driven architecture with events for status updates, messages, and tool calls
   - Extensible tool system via `RealTimeTool` base class
   - Supports latest OpenAI models: `gpt-4o-realtime-preview-2025-06-03`

2. **Hardware Abstraction** (`IAudioHardwareAccess` interface)
   - Defines contract for platform-specific audio operations
   - Key methods: `InitAudio()`, `StartRecordingAudio()`, `PlayAudio()`, `GetAvailableMicrophones()`
   - Each platform provides its own implementation

3. **Platform Implementations**
   - **Windows** (`Hardware/Ai.Tlbx.RealTimeAudio.Hardware.Windows/`): Uses NAudio library
   - **Linux** (`Hardware/Ai.Tlbx.RealTimeAudio.Hardware.Linux/`): Direct ALSA integration via P/Invoke
   - **Web** (`Hardware/Ai.Tlbx.RealTimeAudio.Hardware.Web/`): JavaScript interop with Web Audio API and AudioWorklet

4. **UI Components** (`WebUi/Ai.Tlbx.RealTime.WebUi/`)
   - Reusable Blazor components for audio controls and chat interfaces
   - Platform-agnostic UI elements

### Audio Processing Flow

1. Platform-specific hardware captures audio as PCM 16-bit format
2. Audio is base64 encoded and streamed via WebSocket to OpenAI
3. AI responses (text and audio) are received and processed
4. Audio responses are decoded and played through platform hardware
5. Events notify UI of status changes and new messages

### Key Design Patterns

- **Strategy Pattern**: Platform implementations via `IAudioHardwareAccess`
- **Observer Pattern**: Event-driven updates throughout the system
- **Facade Pattern**: `OpenAiRealTimeApiAccess` simplifies WebSocket complexity
- **Template Method**: `RealTimeTool` for custom AI tool extensions

### Package Distribution

The solution produces multiple NuGet packages (output to `nupkg/`):
- `Ai.Tlbx.RealTimeAudio.OpenAi`: Core provider functionality
- `Ai.Tlbx.RealTimeAudio.Hardware.Windows`: Windows audio support
- `Ai.Tlbx.RealTimeAudio.Hardware.Linux`: Linux audio support
- `Ai.Tlbx.RealTimeAudio.Hardware.Web`: Web/Blazor audio support

Version is tracked in `version.txt` and auto-incremented by `publish-nuget.ps1`.

### Code Style Guidelines

The project follows German code style guidelines from `CodeStyleGuide.md`:
- **Brace Style**: Allman style (opening brace on new line)
- **Indentation**: 4 spaces, no tabs
- **Naming**: 
  - PascalCase for public members
  - _camelCase for private fields (with underscore prefix)
  - Async methods end with `Async` suffix
- **Access Modifiers**: Always explicit
- **Modern C# Features**: Use `var`, pattern matching, null-conditional operators, expression-bodied members where appropriate
- **Comments**: Minimal - only for complex logic

### Logging Strategy

This codebase uses a **centralized logging architecture** detailed in [`LoggingStrategy.md`](LoggingStrategy.md). 

**⚠️ CRITICAL: DO NOT USE ILogger<T> OR Microsoft.Extensions.Logging**

All logging flows from lower layers up to `OpenAiRealTimeApiAccess` where users configure their preferred logging approach. This pattern:
- Maintains clean architecture boundaries
- Allows user choice of logging framework
- Simplifies testing and debugging
- Prevents tight coupling to Microsoft logging

Always use `Action<LogLevel, string>` for logging delegation and forward logs up through natural architectural layers.

### Important Notes

- The project targets .NET 9.0
- No automated tests are currently included - testing is done via demo applications
- Windows components require Windows 10 or later
- Linux components require libasound (ALSA) to be installed
- Web components require HTTPS or localhost for microphone access due to browser security
- JavaScript audio worklet processors handle real-time audio processing in web implementation