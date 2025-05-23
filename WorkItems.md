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
[x] LINUX-0 Define Naming and Namespace Conventions:
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

# Linux Audio Device Implementation Improvements

[x] LINUX-AUDIO-1 Implement proper ALSA initialization:
    - Define P/Invoke signatures for key ALSA functions (snd_pcm_open, snd_pcm_hw_params_malloc, snd_pcm_hw_params_set_*)
    - Properly initialize PCM devices with correct parameters (format=SND_PCM_FORMAT_S16_LE, channels=1, rate=16000)
    - Implement resource tracking for ALSA handles
    - Add detailed logging at each initialization step
    - Handle initialization errors with meaningful error messages

[x] LINUX-AUDIO-2 Implement actual device enumeration:
    - Define P/Invoke signatures for ALSA device enumeration (snd_device_name_hint, snd_device_name_get_hint)
    - Filter for capture-capable devices when listing microphones
    - Convert ALSA device info to AudioDeviceInfo objects with correct properties
    - Cache device list appropriately to avoid excessive native calls
    - Log device discovery process and report found devices

[x] LINUX-AUDIO-3 Implement real audio recording using ALSA:
    - Define P/Invoke signature for snd_pcm_readi
    - Implement a dedicated recording thread with proper buffer management
    - Add overrun detection and recovery
    - Convert raw PCM data to the expected Base64 format
    - Properly invoke the audio data event with accurate data
    - Log recording statistics (buffer sizes, packet counts)

[x] LINUX-AUDIO-4 Implement actual audio playback through ALSA:
    - Define P/Invoke signature for snd_pcm_writei
    - Implement proper Base64 to PCM16 conversion
    - Handle underrun conditions with snd_pcm_recover
    - Manage playback buffer correctly
    - Log playback statistics

[x] LINUX-AUDIO-5 Implement proper microphone device selection:
    - Safely close and reopen PCM devices when changing microphones
    - Persist device selection
    - Validate device availability before attempting to use it
    - Restart recording if active when device changes
    - Log device switch operations

[x] LINUX-AUDIO-6 Add real implementation for clearing audio queue:
    - Define P/Invoke signatures for snd_pcm_drop and snd_pcm_prepare
    - Implement proper buffer flushing
    - Handle concurrent access issues during queue clearing
    - Log queue state before and after clearing

[x] LINUX-AUDIO-7 Implement proper ALSA resource cleanup:
    - Define P/Invoke signature for snd_pcm_close
    - Implement proper resource disposal pattern
    - Ensure all native resources are released in DisposeAsync
    - Add safeguards against double-disposal
    - Log cleanup operations

[x] LINUX-AUDIO-8 Add error handling and recovery for ALSA operations:
    - Define error code mapping from ALSA to meaningful messages
    - Implement recovery strategies for common errors (device busy, disconnected, etc.)
    - Add global error handler for ALSA operations
    - Create appropriate custom exceptions for ALSA-specific errors
    - Log detailed error information including ALSA error codes

# Windows Demo Refactoring and Web UI Razor Class Library

[x] WIN-RCL-1 Create a new Razor Class Library project named `Ai.Tlbx.RealTime.WebUi` in `WebUi/` directory:
    - Define namespace as `Ai.Tlbx.RealTime.WebUi`
    - Configure for Blazor WebAssembly compatibility
    - Set up project file with net9.0 target framework

[x] WIN-RCL-2 Add project references to `Ai.Tlbx.RealTime.WebUi`:
    - Reference `Ai.Tlbx.RealTimeAudio` for core models and interfaces
    - Ensure access to necessary audio event types and status updates

[x] WIN-RCL-3 Create Chat Widget Razor component in `Ai.Tlbx.RealTime.WebUi`:
    - Extract chat UI from Windows demo
    - Name component `ChatWidget.razor`
    - Implement chat message display and input functionality
    - Add necessary CSS for styling

[x] WIN-RCL-4 Create AI Talk Control Widget in `Ai.Tlbx.RealTime.WebUi`:
    - Extract start/stop controls for AI interaction from Windows demo
    - Name component `AiTalkControl.razor`
    - Implement start/stop buttons and related events
    - Style with modern UX practices

[x] WIN-RCL-5 Create Microphone Test Widget in `Ai.Tlbx.RealTime.WebUi`:
    - Extract microphone test UI from Windows demo
    - Name component `MicTestWidget.razor`
    - Implement audio input testing with visual feedback
    - Ensure responsive design

[x] WIN-RCL-6 Create Error/State Display Widget in `Ai.Tlbx.RealTime.WebUi`:
    - Extract error and status display UI from Windows demo
    - Name component `StatusWidget.razor`
    - Implement status updates and error notifications
    - Add appropriate styling for error/warning/info states

[x] WIN-RCL-7 Define shared CSS styles in `Ai.Tlbx.RealTime.WebUi`:
    - Create a `wwwroot/css/` directory with shared styles
    - Ensure consistent look and feel across widgets
    - Optimize for responsiveness and accessibility

[x] WIN-RCL-8 Implement event handling in RCL widgets:
    - Define necessary event callbacks for user interactions
    - Ensure widgets can communicate with parent components
    - Use proper event delegation for performance

[x] WIN-RCL-9 Add documentation for RCL components:
    - Document usage of each widget with XML comments
    - Create a `README.md` in the WebUi directory with setup instructions
    - Include examples of widget integration

[x] WIN-RCL-10 Configure RCL project for static asset delivery:
    - Ensure CSS and potential JS files are bundled correctly
    - Set up project to be referenced as a static asset source
    - Test asset delivery in a consuming project

[x] WIN-DEMO-1 Add project reference to `Ai.Tlbx.RealTime.WebUi` from Windows demo (note: existing build error unrelated to reference):
    - Update `Ai.Tlbx.RealTimeAudio.Demo.Windows` project file
    - Ensure RCL is correctly linked for UI component access

[x] WIN-DEMO-2 Remove old UI code from Windows demo (manual removal required):
    - Delete chat widget code replaced by RCL component
    - Remove AI talk control code replaced by RCL
    - Remove mic test and status display code
    - Clean up any associated CSS/JS

[x] WIN-DEMO-3 Integrate `ChatWidget` into Windows demo (manual integration required):
    - Add component reference in main layout or page
    - Wire up necessary events and data binding
    - Test chat functionality with real-time audio

[x] WIN-DEMO-4 Integrate `AiTalkControl` into Windows demo (manual integration required):
    - Place component in appropriate UI location
    - Connect start/stop events to audio processing logic
    - Verify control over AI interaction flow

[x] WIN-DEMO-5 Integrate `MicTestWidget` into Windows demo (manual integration required):
    - Add component to UI for audio input testing
    - Ensure mic test data flows correctly from hardware layer
    - Validate visual feedback during testing

[x] WIN-DEMO-6 Integrate `StatusWidget` into Windows demo (manual integration required):
    - Position component for error/state visibility
    - Connect status update events from core logic
    - Test various status and error scenarios

[x] WIN-DEMO-7 Update Windows demo styling for RCL integration (manual styling required):
    - Adjust main CSS to accommodate RCL widget styles
    - Resolve any style conflicts between demo and RCL
    - Ensure consistent UI appearance

[x] WIN-DEMO-8 Test refactored Windows demo for functionality (manual testing required):
    - Verify chat interactions work as expected
    - Test AI talk start/stop functionality
    - Confirm mic testing and status display operate correctly

[x] WIN-DEMO-9 Update Windows demo documentation (manual update required):
    - Revise `README.md` to reflect RCL usage
    - Document any new setup requirements due to refactoring
    - Provide troubleshooting steps for UI issues

# Code Style Guide Violations
[x] CODESTYLE-1 Convert empty constructor in `OpenAiRealTimeSettings` (OpenAiRealTimeSettings.cs) to Allman brace style.
[x] CODESTYLE-2 Convert single-line braces in `AlsaException` constructors (AlsaException.cs) to Allman style.
[x] CODESTYLE-3 Reorder methods in `OpenAiRealTimeApiAccess.cs` to place public methods before private methods.
[x] CODESTYLE-4 Add explicit access modifier (`internal`) to `Program` class in Demo Linux `Program.cs`.
[x] CODESTYLE-5 Replace `Console.WriteLine` calls with provided logger or `Debug.WriteLine` in `WebAudioAccess.cs`.

# Immediate Audio Stop on Web Demo
[x] AUDIOSTOP-1 Identify and address buffered audio playback issue in web demo:
[x] AUDIOSTOP-2 Enhance IAudioHardwareAccess interface to support immediate stop:
[x] AUDIOSTOP-3 Implement buffer clearing in WindowsAudioDevice (Windows):
[x] AUDIOSTOP-4 Implement buffer clearing in LinuxAudioDevice (Linux):
[x] AUDIOSTOP-5 Implement buffer clearing in WebAudioAccess (Web):
[x] AUDIOSTOP-6 Update OpenAiRealTimeApiAccess to call buffer clearing on stop:
[x] AUDIOSTOP-7 Test immediate stop functionality across platforms:

# Web Audio Quality Improvements (Playback, Mixing, Artefact Reduction)
[x] WEB-AUDIO-1 Refactor playback pipeline to use a continuous buffer for gapless audio output; ensure chunks are scheduled with no gaps (webAudioAccess.js)
[x] WEB-AUDIO-2 Implement high-quality upsampling of PCM16 audio to 48kHz using linear or spline interpolation before playback (webAudioAccess.js)
[x] WEB-AUDIO-3 Add crossfade/overlap logic between audio chunks to mask discontinuities and reduce artefacts (webAudioAccess.js)
[x] WEB-AUDIO-4 Implement real-time mixing: allow overlapping playback of incoming audio chunks, mixing them into a single output stream (webAudioAccess.js)
[x] WEB-AUDIO-5 Add underrun detection and recovery: fill buffer underruns with silence or last good sample to avoid glitches (webAudioAccess.js)
[x] WEB-AUDIO-6 Add smoothing/anti-click windowing at chunk boundaries (fade-in/fade-out, window functions) (webAudioAccess.js)
[x] WEB-AUDIO-7 Refactor playback to use AudioWorklet for advanced scheduling, mixing, and low-latency output (webAudioAccess.js, audio-processor.js)
[ ] WEB-AUDIO-8 Add runtime configuration for buffer size, overlap, upsampling method, and diagnostics (webAudioAccess.js)

# Web Demo Refactor to Ai.Tlbx.RealTime.WebUi

[x] WEB-RCL-DEMO-1 Replace hard-coded chat list in `Home.razor` with `<ChatWidget />` from RCL.
[x] WEB-RCL-DEMO-2 Swap Start/Stop voice controls with `<AiTalkControl />` component; expose `OnStart` / `OnStop` callbacks.
[x] WEB-RCL-DEMO-3 Integrate <MicTestWidget /> for microphone loopback test; remove custom JS/HTML sections.
[x] WEB-RCL-DEMO-4 Use `<StatusWidget />` for connection / error / mic-test banners; bind to `OpenAiRealTimeApiAccess` status events.
[x] WEB-RCL-DEMO-5 Extract microphone selection UI into new RCL component `MicrophoneSelect.razor` (if missing) and consume it.

## Configuration & State

[x] WEB-RCL-DEMO-6 Provide cascading `OpenAiRealTimeApiAccess` service via DI so RCL widgets can subscribe directly.
[x] WEB-RCL-DEMO-7 Create shared `VoiceSelect.razor` in RCL (if absent) and replace local `<select>` implementation.
[x] WEB-RCL-DEMO-8 Add `SessionSettingsContext` (C# record + `CascadingValue`) to hold selected voice, enabled tools, mic-id etc.; wire widgets to it.

## Layout / Styling

[x] WEB-RCL-DEMO-9 Refactor page layout into reusable `TwoColumnLayout` component in RCL; update `MainLayout.razor` to use it.
[x] WEB-RCL-DEMO-10 Port Tailwind CSS classes referenced by RCL widgets into demo's `tailwind.config.js` (or ensure static asset inclusion).

## JS Interop Alignment

[x] WEB-RCL-DEMO-11 Remove direct calls to `audioInterop.*` from `Home.razor`; instead call helper services exposed by RCL components.
[x] WEB-RCL-DEMO-12 Add `IAudioInteropService` abstraction in RCL; implement default wrapper around existing JS module.

## Feature Parity / Enhancements

[x] WEB-RCL-DEMO-13 Ensure RCL `ChatWidget` supports tool-call message type display; extend component if required.
[x] WEB-RCL-DEMO-14 Add voice-select dropdown into `AiTalkControl` (optional param) OR rely on `VoiceSelect` widget; choose single UX.
[x] WEB-RCL-DEMO-15 Implement loading indicators inside RCL widgets for connecting / recording.
[x] WEB-RCL-DEMO-16 Add toast notification component in RCL for non-blocking status updates; integrate demo.
[x] WEB-RCL-DEMO-17 Expose diagnostic panel (buffer level, latency) via new `DiagnosticsWidget.razor` in RCL; optional toggle.

## Cleanup / Deprecation

[x] WEB-RCL-DEMO-18 Delete custom chat/message markup blocks from `Home.razor` once RCL widget lives.
[x] WEB-RCL-DEMO-19 Remove Tailwind classes duplicated with RCL styles; dedupe CSS files.
[x] WEB-RCL-DEMO-20 Drop unused JS interop helpers after abstraction layer added.

## Testing & Docs

[x] WEB-RCL-DEMO-21 Update unit/integration tests in `Ai.Tlbx.RealTime.WebUi.Test` to cover new widgets (Chat scroll, start/stop events).
[x] WEB-RCL-DEMO-23 Update `README.md` in demo folder: setup, env vars, local run with RCL.




