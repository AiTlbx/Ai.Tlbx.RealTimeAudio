# Logging Strategy

This document outlines the centralized logging architecture used throughout the Ai.Tlbx.RealTimeAudio codebase.

## Architecture Overview

The logging system follows a **bottom-up layered approach** where all logging flows from lower architectural layers up to the root `OpenAiRealTimeApiAccess` class, which provides a single configurable log action for users.

## Layer Flow

```
Layer 1 (Lowest):  JavaScript/Platform-specific code
                   ↓ (logs via JSInterop/native calls)
Layer 2 (Middle):  Hardware Access Providers (WebAudioAccess, WindowsAudioHardware, LinuxAudioDevice)
                   ↓ (logs via SetLogAction)
Layer 3 (Root):    OpenAiRealTimeApiAccess
                   ↓ (executes user-provided log action)
User's Choice:     Debug.WriteLine, Serilog, NLog, etc.
```

## Implementation Details

### Root Class: OpenAiRealTimeApiAccess

The `OpenAiRealTimeApiAccess` class is the **only** class that accepts external logging configuration:

```csharp
// Constructor with optional log action
public OpenAiRealTimeApiAccess(IAudioHardwareAccess hardwareAccess, Action<LogLevel, string>? logAction = null)

// Public method for external logging
public void LogMessage(LogLevel level, string message)
```

If no log action is provided, it defaults to `Debug.WriteLine` for development/testing scenarios.

### Hardware Providers

All hardware access providers implement the `SetLogAction` method to receive the log action from the root class:

```csharp
public interface IAudioHardwareAccess
{
    void SetLogAction(Action<LogLevel, string> logAction);
    // ... other methods
}
```

Each provider forwards all internal logging up to this action.

### Platform-Specific Logging

#### Web Platform
- **JavaScript → C#**: JavaScript diagnostic messages flow via JSInterop to `WebAudioAccess`
- **WebAudioAccess → Root**: All logs forwarded via the log action

#### Windows Platform
- **NAudio/Windows APIs → WindowsAudioHardware**: Platform logs captured
- **WindowsAudioHardware → Root**: All logs forwarded via the log action

#### Linux Platform  
- **ALSA/System calls → LinuxAudioDevice**: Platform logs captured
- **LinuxAudioDevice → Root**: All logs forwarded via the log action

## LogLevel Enum

The system uses a simple 3-level enum defined in `Ai.Tlbx.RealTimeAudio.OpenAi.Models.LogLevel`:

```csharp
public enum LogLevel
{
    Error = 0,  // Critical errors, exceptions
    Warn = 1,   // Warnings, recoverable issues  
    Info = 2    // General information, debugging
}
```

## Usage Examples

### Demo Applications

```csharp
// Hook to Debug.WriteLine for development
var rta = new OpenAiRealTimeApiAccess(hardwareAccess, (level, message) => 
{
    Debug.WriteLine($"[{level}] {message}");
});
```

### Production Applications

```csharp
// Hook to your preferred logging framework
var rta = new OpenAiRealTimeApiAccess(hardwareAccess, (level, message) => 
{
    logger.Log(ConvertLogLevel(level), message);
});
```

### Web Applications

```csharp
// Hook to your preferred logging framework for web applications  
var rta = new OpenAiRealTimeApiAccess(hardwareAccess, (level, message) => 
{
    logger.Log(ConvertLogLevel(level), message);
});
```

## Important Rules

### ❌ DO NOT USE ILogger<T>

**Never** use `Microsoft.Extensions.Logging.ILogger<T>` or related interfaces in this codebase because:

1. **Architectural Violation**: Breaks the centralized logging principle by creating multiple injection points
2. **Tight Coupling**: Forces dependency on Microsoft.Extensions.Logging across all layers
3. **User Choice Loss**: Prevents users from choosing their preferred logging framework
4. **Complexity**: Creates confusion between multiple logging systems
5. **Testing Issues**: Makes it harder to capture/suppress logs during testing

### ✅ DO USE

- **Action<LogLevel, string>**: For all logging delegation
- **Debug.WriteLine**: Only as fallback when no log action provided
- **SetLogAction()**: For forwarding logs between layers

### ✅ Pattern for New Components

When adding new components:

1. **Accept log action**: Receive `Action<LogLevel, string>` from parent layer
2. **Forward up**: Never log directly, always forward through the action
3. **Use LogLevel enum**: Use the centralized 3-level enum
4. **Prefix messages**: Include component name in log messages: `[ComponentName] message`

## Benefits

- **Single Configuration Point**: Users configure logging once at the root
- **Framework Agnostic**: Works with any logging framework user chooses  
- **Testable**: Easy to capture or suppress logs during testing
- **Performant**: No reflection or dependency injection overhead
- **Clean Architecture**: Follows natural component boundaries
- **Flexible**: Can easily add filtering, formatting, or routing at the root level

## Validation

All components should follow this pattern. Any code that:
- Uses `ILogger<T>` or Microsoft.Extensions.Logging
- Logs directly without forwarding through layers
- Creates multiple logging configuration points

Should be refactored to conform to this centralized strategy.