using System;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Custom logger interface for internal logging within the OpenAI provider.
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

    /// <summary>
    /// Logger implementation that uses Debug.WriteLine for output.
    /// </summary>
    internal sealed class DebugCustomLogger : ICustomLogger
    {
        /// <summary>
        /// Logs a message with the specified log level.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        public void Log(LogLevel level, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
        }

        /// <summary>
        /// Logs a message with the specified log level and exception.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="exception">The exception to log.</param>
        public void Log(LogLevel level, string message, Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
            System.Diagnostics.Debug.WriteLine($"Exception: {exception.Message}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {exception.StackTrace}");
        }
    }
}