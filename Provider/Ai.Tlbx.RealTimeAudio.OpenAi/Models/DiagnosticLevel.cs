namespace Ai.Tlbx.RealTimeAudio.OpenAi.Models
{
    /// <summary>
    /// Defines the available diagnostic logging levels for audio hardware access.
    /// </summary>
    public enum DiagnosticLevel
    {
        /// <summary>
        /// No diagnostics - complete bypass for maximum performance.
        /// </summary>
        Off = 0,

        /// <summary>
        /// Only critical errors and session start/end events.
        /// </summary>
        Minimal = 1,

        /// <summary>
        /// Key events, state changes, and errors (default level).
        /// </summary>
        Normal = 2,

        /// <summary>
        /// All diagnostic information including detailed data flows.
        /// </summary>
        Verbose = 3
    }
}