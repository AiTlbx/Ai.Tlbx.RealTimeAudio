using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Structured logger with timestamps, context, and noise reduction.
    /// </summary>
    internal sealed class StructuredLogger
    {
        private readonly ICustomLogger _logger;
        private readonly string _component;
        private readonly Dictionary<string, int> _messageCounters = new();
        private readonly Dictionary<string, DateTime> _lastMessageTime = new();
        private readonly object _lock = new();
        private string? _sessionId;
        private string? _currentState;

        public StructuredLogger(ICustomLogger logger, string component)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _component = component ?? throw new ArgumentNullException(nameof(component));
        }

        /// <summary>
        /// Sets the current session ID for correlation.
        /// </summary>
        public void SetSessionId(string sessionId)
        {
            _sessionId = sessionId;
        }

        /// <summary>
        /// Sets the current state for context.
        /// </summary>
        public void SetState(string state)
        {
            _currentState = state;
        }

        /// <summary>
        /// Logs a message with full context and timestamps.
        /// </summary>
        public void Log(LogLevel level, string message, Exception? exception = null, object? data = null)
        {
            var timestamp = DateTime.UtcNow;
            var contextualMessage = BuildContextualMessage(timestamp, message, data);
            
            if (exception != null)
            {
                _logger.Log(level, contextualMessage, exception);
            }
            else
            {
                _logger.Log(level, contextualMessage);
            }
        }

        /// <summary>
        /// Logs a message with noise reduction (throttling for repetitive messages).
        /// </summary>
        public void LogThrottled(LogLevel level, string messageKey, string message, TimeSpan throttleDuration)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                
                if (_lastMessageTime.TryGetValue(messageKey, out var lastTime) && 
                    now - lastTime < throttleDuration)
                {
                    // Increment counter but don't log
                    _messageCounters[messageKey] = _messageCounters.GetValueOrDefault(messageKey, 0) + 1;
                    return;
                }

                // Log the message
                var count = _messageCounters.GetValueOrDefault(messageKey, 0);
                var finalMessage = count > 0 ? $"{message} (repeated {count} times)" : message;
                
                Log(level, finalMessage);
                
                // Reset counters
                _messageCounters[messageKey] = 0;
                _lastMessageTime[messageKey] = now;
            }
        }

        /// <summary>
        /// Logs OpenAI API messages with smart categorization.
        /// </summary>
        public void LogApiMessage(string messageType, string eventId, object? payload = null)
        {
            var level = CategorizeApiMessage(messageType);
            var message = $"API: {messageType}";
            
            if (ShouldLogApiPayload(messageType) && payload != null)
            {
                message += $" | {JsonSerializer.Serialize(payload)}";
            }
            
            Log(level, message, data: new { EventId = eventId, Type = messageType });
        }

        /// <summary>
        /// Logs state transitions with context.
        /// </summary>
        public void LogStateTransition(string fromState, string toState, string reason = "")
        {
            var message = $"State: {fromState} → {toState}";
            if (!string.IsNullOrEmpty(reason))
            {
                message += $" | Reason: {reason}";
            }
            
            SetState(toState);
            Log(LogLevel.Info, message);
        }

        /// <summary>
        /// Logs audio operations with size information.
        /// </summary>
        public void LogAudioOperation(string operation, int dataSize, string? additionalInfo = null)
        {
            var message = $"Audio: {operation} | Size: {dataSize:N0} bytes";
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                message += $" | {additionalInfo}";
            }
            
            // Throttle repetitive audio operations
            LogThrottled(LogLevel.Info, $"audio_{operation}", message, TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Logs errors with enhanced context and stack traces.
        /// </summary>
        public void LogError(string operation, Exception exception, object? context = null)
        {
            var message = $"Error in {operation}: {exception.Message}";
            Log(LogLevel.Error, message, exception, context);
        }

        /// <summary>
        /// Logs performance metrics.
        /// </summary>
        public void LogPerformance(string operation, TimeSpan duration, object? metrics = null)
        {
            var message = $"Performance: {operation} took {duration.TotalMilliseconds:F2}ms";
            Log(LogLevel.Info, message, data: metrics);
        }

        private string BuildContextualMessage(DateTime timestamp, string message, object? data)
        {
            var contextParts = new List<string>
            {
                timestamp.ToString("HH:mm:ss.fff"),
                _component
            };

            if (!string.IsNullOrEmpty(_sessionId))
            {
                contextParts.Add($"Session:{_sessionId[^8..]}"); // Last 8 chars
            }

            if (!string.IsNullOrEmpty(_currentState))
            {
                contextParts.Add($"State:{_currentState}");
            }

            var context = string.Join(" | ", contextParts);
            var finalMessage = $"[{context}] {message}";

            if (data != null)
            {
                try
                {
                    var dataJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
                    finalMessage += $" | Data: {dataJson}";
                }
                catch
                {
                    finalMessage += $" | Data: {data}";
                }
            }

            return finalMessage;
        }

        private LogLevel CategorizeApiMessage(string messageType)
        {
            return messageType switch
            {
                "error" => LogLevel.Error,
                "session.created" => LogLevel.Info,
                "session.updated" => LogLevel.Info,
                "input_audio_buffer.speech_started" => LogLevel.Info,
                "input_audio_buffer.speech_stopped" => LogLevel.Info,
                "input_audio_buffer.committed" => LogLevel.Info,
                "conversation.item.created" => LogLevel.Info,
                "response.created" => LogLevel.Info,
                "response.done" => LogLevel.Info,
                "response.audio.delta" => LogLevel.Info,
                "response.audio.done" => LogLevel.Info,
                "response.audio_transcript.delta" => LogLevel.Info,
                "response.audio_transcript.done" => LogLevel.Info,
                "conversation.item.input_audio_transcription.delta" => LogLevel.Info,
                "conversation.item.input_audio_transcription.done" => LogLevel.Info,
                "response.output_item.added" => LogLevel.Info,
                "response.content_part.added" => LogLevel.Info,
                "response.content_part.done" => LogLevel.Info,
                "rate_limits.updated" => LogLevel.Info,
                _ => LogLevel.Warn
            };
        }

        private bool ShouldLogApiPayload(string messageType)
        {
            return messageType switch
            {
                "error" => true,
                "session.updated" => false, // Too verbose
                "response.created" => false,
                "response.done" => true,
                "rate_limits.updated" => false,
                _ => false
            };
        }
    }
}