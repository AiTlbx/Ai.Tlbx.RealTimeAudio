# Current Features – Ai.Tlbx.RealTimeAudio.Demo.Web

The list below captures the functional and visual features presently implemented in the Blazor Web demo prior to refactoring to the shared Razor Class Library (RCL) `Ai.Tlbx.RealTime.WebUi`.

## UI Panels & Widgets

- **Voice Controls Panel**
  - Start button (begins real-time recording + OpenAI session)
  - Stop button (terminates recording/session)
- **Assistant Voice Select**
  - `<select>` list populated with all 11 OpenAI voice options
- **Microphone Select & Permission Helper**
  - Drop-down of available microphones (dynamic enumeration)
  - Refresh/permission button to request mic access
  - Inline hints for permission state, selected device, loading status
- **Feature Toggles**
  - `Enable Current Time Tool` checkbox (toggles usage of `CurrentTimeTool`)
- **Utility Buttons**
  - `Clear Chat` (purges chat history UI + model)
  - `Test Microphone` (starts/stops loopback test via JS interop)
- **Status / Notification Blocks**
  - *Connecting* spinner while WebSocket connects
  - *Connected* success banner (shows selected voice)
  - *Error* banner (red) with last error text
  - *Microphone Test* status panel with cancel button
- **Conversation Panel**
  - Scrollable chat list (`#chat-messages` div)
  - Message bubbles for:
    - User messages (right-aligned, blue)
    - Assistant messages (left-aligned, gray)
    - Tool call messages (centered, purple, collapsible `<details>`)
  - Auto-scroll to newest message via `@ref` + JS

## Functional Behaviours

- **Session Lifecycle**
  - Handles `OpenAiRealTimeApiAccess` connection, streaming, stop/clear
- **Microphone Management**
  - Enumerates devices via `navigator.mediaDevices.enumerateDevices()`
  - Runtime permission requests & state feedback
- **JS ↔ . NET Interop**
  - `audioInterop` functions for init, recording, playback, mic-test
  - Receives `OnAudioDataAvailable`, status updates, errors
- **Voice & Tool Configuration**
  - Passes selected voice + toggled tools into request payloads
- **Responsive Layout**
  - Tailwind-based responsive flex layout (two-column desktop / stacked mobile)
- **Dark-mode friendly colours** (via Tailwind defaults)

> This document serves as the authoritative feature baseline before migrating the UI to RCL widgets. Update if new capabilities are added before refactor work begins. 