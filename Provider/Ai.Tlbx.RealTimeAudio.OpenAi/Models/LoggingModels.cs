using System;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Models
{
    // LOGSTAT-1: Core Enums
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    public enum StatusCategory
    {
        Connection,
        Recording,
        Processing,
        Configuration,
        Error,
        Tool
    }

    // LOGCLEAN-2: Create LogCategory Enum for Debug Areas
    public enum LogCategory
    {
        General,
        WebSocket,
        Audio,
        Session,
        Tooling,
        Microphone,
        Cleanup,
        Initialization,
        MessageProcessing,
        MicTest,
        OpenAiRealTimeApiAccess
    }

    // LOGSTAT-2: Status Detail Enum
    public enum StatusCode
    {
        // Connection related
        Connecting,
        Connected,
        Disconnected,
        ConnectionFailed,
        Reconnecting,
        
        // Recording related
        RecordingStarted,
        RecordingStopped,
        RecordingFailed,
        MicrophoneDetected,
        MicrophoneChanged,
        MicrophoneTestStarted,
        MicrophoneTestCompleted,
        MicrophoneTestFailed,
        Stopping,
        Stopped,
        Interrupting,
        Interrupted,
        
        // Processing related
        ProcessingStarted,
        ProcessingCompleted,
        ProcessingFailed,
        MessageReceived,
        MessageSent,
        
        // Configuration related
        Initialized,
        ConfigurationUpdated,
        VoiceChanged,
        SettingsApplied,
        
        // Error related
        GeneralError,
        AudioError,
        NetworkError,
        Error,
        
        // Tool related
        ToolCallReceived,
        ToolResultSent
    }

    // LOGSTAT-3: StatusUpdateEventArgs class
    public class StatusUpdateEventArgs : EventArgs
    {
        public StatusCategory Category { get; }
        public StatusCode Code { get; }
        public string Message { get; }
        public Exception? Exception { get; }
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        
        public StatusUpdateEventArgs(StatusCategory category, StatusCode code, string message, Exception? exception = null)
        {
            Category = category;
            Code = code;
            Message = message;
            Exception = exception;
        }
    }
} 