# How this document works
**Note:** All code implementations related to the work items below MUST adhere to the guidelines specified in `CodeStyleGuide.md`.

Tasks have headlines and workitems 
workitems look like this they have a status indicator, a ticket id (naming based on a shorthand of the headline) and description:
[ ] DOC-1 Headline of workitem 
[x] DOC-2 Headline of another workitem that is already done 

# Refactor Logging and Status Reporting in OpenAiRealTimeApiAccess
[x] LOGSTAT-1 Define Core Enums (LogLevel, StatusCategory)
[x] LOGSTAT-2 Define Status Detail Enum (StatusCode)
[x] LOGSTAT-3 Create StatusUpdateEventArgs class
[x] LOGSTAT-4 Define Unified StatusUpdated Event
[x] LOGSTAT-5 Define LogAction Delegate
[x] LOGSTAT-6 Inject Logger via Constructor
[x] LOGSTAT-7 Implement Internal ReportStatus Method
[x] LOGSTAT-8 Implement ReportStatus Logic (Call Logger, Raise Event)
[x] LOGSTAT-9 Refactor Callsites to use ReportStatus
[x] LOGSTAT-10 Remove Obsolete Members (Old methods, properties, fields)

# Remove Debug.WriteLine in favor of Logger Delegate
[x] LOGCLEAN-1 Define Log Method with Category and LogLevel Parameters
[x] LOGCLEAN-2 Create LogCategory Enum for Debug Areas (WebSocket, Tooling, etc.)
[x] LOGCLEAN-7 Remove ReportStatus Debug.WriteLine Fallback (Use Logger Only)
[x] LOGCLEAN-8 Add DefaultLogger to Provide Debug.WriteLine Fallback Only If No Logger Injected
[x] LOGCLEAN-3 Replace All WebSocket Debug.WriteLine with Logger
[x] LOGCLEAN-4 Replace All Audio/Session Debug.WriteLine with Logger
[x] LOGCLEAN-5 Replace All Tool-related Debug.WriteLine with Logger
[x] LOGCLEAN-6 Replace All Error and Exception Debug.WriteLine with Logger

# Linux Audio Hardware Abstraction Layer & Demo
[ ] LINUX-0 Define Naming and Namespace Conventions:
    - Hardware Project Namespace: `Ai.Tlbx.RealTimeAudio.Hardware.Linux`
    - Demo Project Namespace: `Ai.Tlbx.RealTimeAudio.Demo.Linux`
    - Project Directory Structure: `Hardware/Ai.Tlbx.RealTimeAudio.Hardware.Linux/` and `Demo/Ai.Tlbx.RealTimeAudio.Demo.Linux/`
    - Test Directory Structure: `tests/Hardware/` and `tests/Demo/` (following project naming)
[x] LINUX-1 Create new C# Class Library project: `Ai.Tlbx.RealTimeAudio.Hardware.Linux` in `Hardware/`.
[x] LINUX-2 Add project reference from the Linux Hardware project to `Ai.Tlbx.RealTimeAudio` (assuming it contains `IAudioHardwareAccess`).
[x] LINUX-3 Create class `LinuxAudioDevice` in `LinuxAudioDevice.cs` within the hardware project, implement `IAudioHardwareAccess`.
[x] LINUX-4 Determine ALSA interaction method: Use direct P/Invoke to `libasound.so.2` or find/add a suitable NuGet wrapper. Note: Direct file I/O on `/dev/snd/` devices is insufficient; ALSA configuration via API is required.
[x] LINUX-5 Implement `GetInputDevices` in `LinuxAudioDevice` (using ALSA API).
[x] LINUX-6 Implement `GetOutputDevices` in `LinuxAudioDevice` (using ALSA API).
[x] LINUX-7 Implement `InitializeInputDevice` in `LinuxAudioDevice` (PCM16, 16000 Hz or configurable, first available ALSA device, configuring via ALSA API).
[x] LINUX-8 Implement `InitializeOutputDevice` in `LinuxAudioDevice` (PCM16, 16000 Hz or configurable, first available ALSA device, configuring via ALSA API).
[x] LINUX-9 Implement `StartRecording` in `LinuxAudioDevice` (using ALSA API).
[x] LINUX-10 Implement `StopRecording` in `LinuxAudioDevice` (using ALSA API).
[x] LINUX-11 Implement `StartPlayback` in `LinuxAudioDevice` (using ALSA API).
[x] LINUX-12 Implement `StopPlayback` in `LinuxAudioDevice` (using ALSA API).
[x] LINUX-13 Implement `WriteToOutputDevice` in `LinuxAudioDevice` (using ALSA API).
[x] LINUX-14 Implement `DataAvailable` event logic in `LinuxAudioDevice` (based on ALSA capture).
[x] LINUX-15 Implement logging in `LinuxAudioDevice` using injected logger.
[x] LINUX-16 Configure the hardware project file (`.csproj`) for cross-platform/Linux (net9.0, potentially add Linux RID).
[x] LINUX-19 Create new C# Console App project: `Ai.Tlbx.RealTimeAudio.Demo.Linux` in `Demo/`.
[x] LINUX-20 Add project reference from the Linux Demo project to the Linux Hardware project.
[x] LINUX-21 Add project reference from the Linux Demo project to `Ai.Tlbx.RealTimeAudio`.
[x] LINUX-22 Instantiate `LinuxAudioDevice` in the demo's `Program.cs`.
[x] LINUX-23 Instantiate `OpenAiRealTimeApiAccess` in `Program.cs`, injecting the `LinuxAudioDevice` instance.
[x] LINUX-24 Implement basic command-line interaction (e.g., start/stop stream) in `Program.cs`.
[x] LINUX-25 Configure the demo project file (`.csproj`) for Linux execution (net9.0, specify Linux RID e.g., `linux-arm64` for RPi).
[x] LINUX-26 Set up basic logging in `Program.cs`, passing logger delegate to dependencies.
[x] LINUX-27 Implement configuration (e.g., `appsettings.json`, User Secrets) for API keys in the demo project.
[x] LINUX-28 Add `README.md` to the demo project directory with build/run instructions for Linux/Raspberry Pi.


