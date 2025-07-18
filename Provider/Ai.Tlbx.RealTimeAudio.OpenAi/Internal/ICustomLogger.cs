using Ai.Tlbx.RealTimeAudio.OpenAi.Models;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Custom logger interface for internal logging within the OpenAI provider.
    /// This interface supports the centralized logging strategy where all logging
    /// flows up through Action&lt;LogLevel, string&gt; delegates to OpenAiRealTimeApiAccess.
    /// </summary>
    internal interface ICustomLogger
    {
        /// <summary>
        /// Logs a message with the specified log level.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        void Log(LogLevel level, string message);

        /// <summary>
        /// Logs a message with the specified log level and exception.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">The exception to log.</param>
        void Log(LogLevel level, string message, Exception exception);
    }

    /// <summary>
    /// Logger implementation that uses an action delegate.
    /// This is the primary implementation that forwards all logging through the centralized
    /// Action&lt;LogLevel, string&gt; delegate, ensuring all logs flow to the user-configured
    /// logging system in OpenAiRealTimeApiAccess.
    /// </summary>
    internal sealed class ActionCustomLogger : ICustomLogger
    {
        private readonly Action<LogLevel, string> _logAction;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCustomLogger"/> class.
        /// </summary>
        /// <param name="logAction">The logging action delegate.</param>
        public ActionCustomLogger(Action<LogLevel, string> logAction)
        {
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
        }

        /// <summary>
        /// Logs a message with the specified log level.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        public void Log(LogLevel level, string message)
        {
            _logAction(level, message);
        }

        /// <summary>
        /// Logs a message with the specified log level and exception.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">The exception to log.</param>
        public void Log(LogLevel level, string message, Exception exception)
        {
            _logAction(level, $"{message}\nException: {exception.Message}\nStackTrace: {exception.StackTrace}");
        }
    }

}