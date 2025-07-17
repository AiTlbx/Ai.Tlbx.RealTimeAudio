using System;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Provides contextual logging with component-specific prefixes.
    /// </summary>
    internal sealed class LogContext
    {
        private readonly ICustomLogger _logger;
        private readonly string _component;

        /// <summary>
        /// Initializes a new instance of the <see cref="LogContext"/> class.
        /// </summary>
        /// <param name="logger">The underlying logger instance.</param>
        /// <param name="component">The component name for logging context.</param>
        public LogContext(ICustomLogger logger, string component)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _component = component ?? throw new ArgumentNullException(nameof(component));
        }

        /// <summary>
        /// Logs a debug message with component context.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void LogDebug(string message)
        {
            _logger.Log(LogLevel.Info, $"[{_component}] {message}");
        }

        /// <summary>
        /// Logs an informational message with component context.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void LogInformation(string message)
        {
            _logger.Log(LogLevel.Info, $"[{_component}] {message}");
        }

        /// <summary>
        /// Logs a warning message with component context.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void LogWarning(string message)
        {
            _logger.Log(LogLevel.Warn, $"[{_component}] {message}");
        }

        /// <summary>
        /// Logs an error message with component context.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void LogError(string message)
        {
            _logger.Log(LogLevel.Error, $"[{_component}] {message}");
        }

        /// <summary>
        /// Logs an error message with exception and component context.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        public void LogError(Exception exception, string message)
        {
            _logger.Log(LogLevel.Error, $"[{_component}] {message}", exception);
        }

        /// <summary>
        /// Logs a critical message with component context.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public void LogCritical(string message)
        {
            _logger.Log(LogLevel.Error, $"[{_component}] {message}");
        }

        /// <summary>
        /// Logs a critical message with exception and component context.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        public void LogCritical(Exception exception, string message)
        {
            _logger.Log(LogLevel.Error, $"[{_component}] {message}", exception);
        }

        /// <summary>
        /// Creates a new log context with a sub-component name.
        /// </summary>
        /// <param name="subComponent">The sub-component name.</param>
        /// <returns>A new log context for the sub-component.</returns>
        public LogContext CreateSubContext(string subComponent)
        {
            return new LogContext(_logger, $"{_component}.{subComponent}");
        }
    }
}