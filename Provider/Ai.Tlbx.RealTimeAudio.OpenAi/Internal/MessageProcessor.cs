using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Ai.Tlbx.RealTimeAudio.OpenAi.Events;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;
using Ai.Tlbx.RealTimeAudio.OpenAi.Tools;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Processes messages received from OpenAI's real-time API.
    /// </summary>
    internal sealed class MessageProcessor
    {
        private readonly ICustomLogger _logger;
        private readonly StructuredLogger _structuredLogger;
        private readonly IAudioHardwareAccess _hardwareAccess;
        private readonly ChatHistoryManager _chatHistory;
        private readonly List<RealTimeTool> _tools;
        private readonly StringBuilder _currentAiMessage;
        private readonly StringBuilder _currentUserMessage;
        private readonly Func<object, Task> _sendMessageAsync;
        private readonly Dictionary<string, int> _audioDeltaCount = new();
        private readonly Dictionary<string, int> _transcriptDeltaCount = new();
        private bool _hasActiveResponse = false;

        /// <summary>
        /// Event that fires when a new message is added to the chat history.
        /// </summary>
        public event EventHandler<OpenAiChatMessage>? MessageAdded;

        /// <summary>
        /// Event that fires when a tool call is requested.
        /// </summary>
        public event EventHandler<ToolCallEventArgs>? ToolCallRequested;

        /// <summary>
        /// Event that fires when a tool result is available.
        /// </summary>
        public event EventHandler<(string ToolName, string Result)>? ToolResultAvailable;

        /// <summary>
        /// Event that fires when the connection status changes.
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageProcessor"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="hardwareAccess">The audio hardware access instance.</param>
        /// <param name="chatHistory">The chat history manager.</param>
        /// <param name="tools">The list of available tools.</param>
        /// <param name="sendMessageAsync">Function to send messages back to the API.</param>
        public MessageProcessor(
            ICustomLogger logger,
            IAudioHardwareAccess hardwareAccess,
            ChatHistoryManager chatHistory,
            List<RealTimeTool> tools,
            Func<object, Task> sendMessageAsync)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _structuredLogger = new StructuredLogger(logger, "MessageProcessor");
            _hardwareAccess = hardwareAccess ?? throw new ArgumentNullException(nameof(hardwareAccess));
            _chatHistory = chatHistory ?? throw new ArgumentNullException(nameof(chatHistory));
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _sendMessageAsync = sendMessageAsync ?? throw new ArgumentNullException(nameof(sendMessageAsync));
            _currentAiMessage = new StringBuilder();
            _currentUserMessage = new StringBuilder();
        }

        /// <summary>
        /// Processes a message received from the OpenAI API.
        /// </summary>
        /// <param name="json">The JSON message to process.</param>
        /// <returns>A task representing the processing operation.</returns>
        public async Task ProcessMessageAsync(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                {
                    _structuredLogger.Log(LogLevel.Warn, $"Message without valid 'type' property: {json.Substring(0, Math.Min(100, json.Length))}...");
                    return;
                }

                var type = typeElement.GetString();
                var eventId = root.TryGetProperty("event_id", out var eventIdElement) ? eventIdElement.GetString() : "unknown";
                
                // Log API message with smart categorization
                _structuredLogger.LogApiMessage(type ?? "unknown", eventId ?? "unknown");
                
                await ProcessMessageByTypeAsync(type, root, json);
            }
            catch (JsonException jsonEx)
            {
                StatusChanged?.Invoke(this, $"Error parsing JSON message: {jsonEx.Message}");
                _structuredLogger.LogError("JSON parsing", jsonEx, new { JsonPreview = json.Substring(0, Math.Min(100, json.Length)) });
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error handling message: {ex.Message}");
                _structuredLogger.LogError("Message processing", ex, new { JsonPreview = json.Substring(0, Math.Min(100, json.Length)) });
            }
        }

        private async Task ProcessMessageByTypeAsync(string? type, JsonElement root, string json)
        {
            switch (type)
            {
                case "error":
                    await HandleErrorAsync(root);
                    break;

                case "rate_limits.updated":
                    HandleRateLimitsUpdated(json);
                    break;

                case "response.audio.delta":
                    await HandleAudioDeltaAsync(root);
                    break;

                case "response.audio_transcript.done":
                    HandleAudioTranscriptDone(root);
                    break;

                case "response.done":
                    await HandleResponseDoneAsync(root);
                    break;

                case "response.text.delta":
                    HandleTextDelta(root);
                    break;

                case "response.text.done":
                    await HandleTextDoneAsync();
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    await HandleInputAudioTranscriptionAsync(root);
                    break;

                case "input_audio_buffer.speech_started":
                    await HandleSpeechStartedAsync();
                    break;

                case "input_audio_buffer.speech_stopped":
                    HandleSpeechStopped();
                    break;

                case "conversation.item.start":
                    HandleConversationItemStart(root);
                    break;

                case "conversation.item.end":
                    HandleConversationItemEnd();
                    break;

                case "response.output_item.done":
                    await HandleOutputItemDoneAsync(root);
                    break;

                case "response.function_call_arguments.delta":
                    HandleFunctionCallArgumentsDelta();
                    break;

                case "response.function_call_arguments.done":
                    await HandleFunctionCallArgumentsDoneAsync(root);
                    break;

                // Handle known message types that we don't need to process but shouldn't warn about
                case "session.updated":
                case "input_audio_buffer.committed":
                case "conversation.item.created":
                    // These are expected messages that we don't need to process
                    break;
                
                case "response.created":
                    HandleResponseCreated();
                    break;
                    
                case "response.output_item.added":
                case "response.content_part.added":
                case "response.content_part.done":
                case "response.audio.done":
                case "response.audio_transcript.delta":
                case "conversation.item.input_audio_transcription.delta":
                case "conversation.item.input_audio_transcription.done":
                    // These are expected messages that we don't need to process
                    break;

                default:
                    _structuredLogger.Log(LogLevel.Info, $"Unhandled message type: {type}", data: new { MessageType = type });
                    break;
            }
        }

        private Task HandleErrorAsync(JsonElement root)
        {
            string errorMessage = "Unknown error";
            string errorType = "unknown";
            string errorCode = "unknown";

            if (root.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("message", out var msgElement))
                    errorMessage = msgElement.GetString() ?? errorMessage;

                if (errorObj.TryGetProperty("type", out var errorTypeElement))
                    errorType = errorTypeElement.GetString() ?? errorType;

                if (errorObj.TryGetProperty("code", out var codeElement))
                    errorCode = codeElement.GetString() ?? errorCode;
            }

            string errorDetails = $"Error: {errorType}, Code: {errorCode}, Message: {errorMessage}";
            _logger.Log(LogLevel.Error, errorDetails);
            StatusChanged?.Invoke(this, $"OpenAI API Error: {errorMessage}");
            return Task.CompletedTask;
        }

        private void HandleRateLimitsUpdated(string json)
        {
            _logger.Log(LogLevel.Info, $"Rate Limit Update: {json}");
        }

        private Task HandleAudioDeltaAsync(JsonElement root)
        {
            var audio = root.GetProperty("delta").GetString();
            
            // Use aggregated logging to reduce noise
            var eventId = root.TryGetProperty("event_id", out var eventIdElem) ? eventIdElem.GetString() : "unknown";
            var key = $"audio_delta_{eventId}";
            _audioDeltaCount[key] = _audioDeltaCount.GetValueOrDefault(key, 0) + 1;
            
            // Only log every 10th audio delta to reduce spam
            if (_audioDeltaCount[key] % 10 == 1)
            {
                _structuredLogger.LogAudioOperation("response.audio.delta", audio?.Length ?? 0, 
                    $"EventId: {eventId}, Count: {_audioDeltaCount[key]}");
            }
            
            if (!string.IsNullOrEmpty(audio))
            {
                try
                {
                    _hardwareAccess.PlayAudio(audio, 24000);
                    
                    // Only log first and every 10th successful playback
                    if (_audioDeltaCount[key] % 10 == 1)
                    {
                        _structuredLogger.LogAudioOperation("PlayAudio", audio.Length, "Successfully queued for playback");
                    }
                }
                catch (Exception ex)
                {
                    _structuredLogger.LogError("Audio playback", ex, new { AudioLength = audio.Length, EventId = eventId });
                }
            }
            return Task.CompletedTask;
        }

        private void HandleAudioTranscriptDone(JsonElement root)
        {
            if (root.TryGetProperty("transcript", out var spokenText))
            {
                var transcript = spokenText.GetString() ?? "";
                _structuredLogger.Log(LogLevel.Info, $"AI Transcript: {transcript}");
            }
        }

        private void HandleResponseCreated()
        {
            _hasActiveResponse = true;
            _structuredLogger.Log(LogLevel.Info, "AI response started");
        }

        private Task HandleResponseDoneAsync(JsonElement root)
        {
            _hasActiveResponse = false;
            _logger.Log(LogLevel.Info, "Full response completed");
            
            try
            {
                if (root.TryGetProperty("response", out var responseObj) &&
                    responseObj.TryGetProperty("output", out var outputArray) &&
                    outputArray.GetArrayLength() > 0)
                {
                    var firstOutput = outputArray[0];
                    if (firstOutput.TryGetProperty("content", out var contentArray) &&
                        contentArray.GetArrayLength() > 0)
                    {
                        StringBuilder fullText = new StringBuilder();
                        
                        foreach (var content in contentArray.EnumerateArray())
                        {
                            if (content.TryGetProperty("type", out var contentType))
                            {
                                string contentTypeStr = contentType.GetString() ?? string.Empty;
                                
                                if (contentTypeStr == "text" && content.TryGetProperty("text", out var textElement))
                                {
                                    string text = textElement.GetString() ?? string.Empty;
                                    fullText.Append(text);
                                    _logger.Log(LogLevel.Info, $"Extracted text from response.done: {text}");
                                }
                                else if (contentTypeStr == "audio" && content.TryGetProperty("transcript", out var transcriptElement))
                                {
                                    string transcript = transcriptElement.GetString() ?? string.Empty;
                                    fullText.Append(transcript);
                                    _logger.Log(LogLevel.Info, $"Extracted audio transcript from response.done: {transcript}");
                                }
                            }
                        }
                        
                        string completeMessage = fullText.ToString();
                        if (!string.IsNullOrWhiteSpace(completeMessage))
                        {
                            _logger.Log(LogLevel.Info, $"Final extracted message from response.done: {completeMessage}");
                            
                            if (ShouldAddMessage(completeMessage, "assistant"))
                            {
                                var message = new OpenAiChatMessage(completeMessage, "assistant");
                                _chatHistory.AddMessage(message);
                                MessageAdded?.Invoke(this, message);
                                _logger.Log(LogLevel.Info, "Added message to chat history via response.done");
                            }
                            
                            _currentAiMessage.Clear();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error processing response.done message: {ex.Message}", ex);
            }
            return Task.CompletedTask;
        }

        private void HandleTextDelta(JsonElement root)
        {
            if (root.TryGetProperty("delta", out var deltaElem) && 
                deltaElem.TryGetProperty("text", out var textElem))
            {
                string deltaText = textElem.GetString() ?? string.Empty;
                _currentAiMessage.Append(deltaText);
                
                // Aggregate text deltas - only log summary periodically
                var eventId = root.TryGetProperty("event_id", out var eventIdElem) ? eventIdElem.GetString() : "unknown";
                var key = $"text_delta_{eventId}";
                _transcriptDeltaCount[key] = _transcriptDeltaCount.GetValueOrDefault(key, 0) + 1;
                
                // Only log every 20th text delta to reduce spam
                if (_transcriptDeltaCount[key] % 20 == 1)
                {
                    _structuredLogger.Log(LogLevel.Info, $"Text delta progress: {_transcriptDeltaCount[key]} chunks received", 
                        data: new { CurrentLength = _currentAiMessage.Length, EventId = eventId });
                }
            }
        }

        private Task HandleTextDoneAsync()
        {
            if (_currentAiMessage.Length > 0)
            {
                string messageText = _currentAiMessage.ToString();
                _structuredLogger.Log(LogLevel.Info, $"AI Text Complete: {messageText}");
                
                if (ShouldAddMessage(messageText, "assistant"))
                {
                    var message = new OpenAiChatMessage(messageText, "assistant");
                    _chatHistory.AddMessage(message);
                    MessageAdded?.Invoke(this, message);
                    _structuredLogger.Log(LogLevel.Info, "Added text message to chat history");
                }
                
                _currentAiMessage.Clear();
            }
            return Task.CompletedTask;
        }

        private Task HandleInputAudioTranscriptionAsync(JsonElement root)
        {
            if (root.TryGetProperty("transcript", out var transcriptElem))
            {
                string transcript = transcriptElem.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    _structuredLogger.Log(LogLevel.Info, $"User Transcript: {transcript}");
                    
                    var message = new OpenAiChatMessage(transcript, "user");
                    _chatHistory.AddMessage(message);
                    MessageAdded?.Invoke(this, message);
                    _currentUserMessage.Clear();
                    StatusChanged?.Invoke(this, "User said: " + transcript);
                }
            }
            return Task.CompletedTask;
        }

        private async Task HandleSpeechStartedAsync()
        {
            _structuredLogger.Log(LogLevel.Info, "Speech detected");
            StatusChanged?.Invoke(this, "Speech detected");
            
            // Only cancel if there's an active response
            if (_hasActiveResponse)
            {
                _structuredLogger.Log(LogLevel.Info, "Interrupting active AI response");
                await _sendMessageAsync(new { type = "response.cancel" });
                // Set to false immediately since we're cancelling
                _hasActiveResponse = false;
            }
            else
            {
                _structuredLogger.Log(LogLevel.Info, "No active response to interrupt");
            }
            
            await _hardwareAccess.ClearAudioQueue();
        }

        private void HandleSpeechStopped()
        {
            _structuredLogger.Log(LogLevel.Info, "Speech ended - processing user input");
            StatusChanged?.Invoke(this, "Speech ended");
        }

        private void HandleConversationItemStart(JsonElement root)
        {
            _logger.Log(LogLevel.Info, "New conversation item started");
            if (root.TryGetProperty("role", out var roleElem))
            {
                string role = roleElem.GetString() ?? string.Empty;
                _logger.Log(LogLevel.Info, $"Item role: {role}");
                if (role == "assistant")
                {
                    _currentAiMessage.Clear();
                }
            }
        }

        private void HandleConversationItemEnd()
        {
            _logger.Log(LogLevel.Info, "Conversation item ended");
        }

        private Task HandleOutputItemDoneAsync(JsonElement root)
        {
            _logger.Log(LogLevel.Info, "Received complete message from assistant");
            
            try
            {
                if (root.TryGetProperty("item", out var itemElem) && 
                    itemElem.TryGetProperty("content", out var contentArray))
                {
                    StringBuilder completeMessage = new StringBuilder();
                    
                    foreach (var content in contentArray.EnumerateArray())
                    {
                        if (content.TryGetProperty("type", out var contentTypeElem) && 
                            contentTypeElem.GetString() == "text" &&
                            content.TryGetProperty("text", out var contentTextElem))
                        {
                            string text = contentTextElem.GetString() ?? string.Empty;
                            completeMessage.Append(text);
                        }
                    }
                    
                    string messageText = completeMessage.ToString();
                    _logger.Log(LogLevel.Info, $"Complete message text: {messageText}");
                    
                    if (!string.IsNullOrWhiteSpace(messageText) && ShouldAddMessage(messageText, "assistant"))
                    {
                        var message = new OpenAiChatMessage(messageText, "assistant");
                        _chatHistory.AddMessage(message);
                        MessageAdded?.Invoke(this, message);
                        _currentAiMessage.Clear();
                        StatusChanged?.Invoke(this, "Received complete message from assistant");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error processing complete message: {ex.Message}", ex);
            }
            return Task.CompletedTask;
        }

        private void HandleFunctionCallArgumentsDelta()
        {
            _logger.Log(LogLevel.Info, "Received function call arguments delta");
        }

        private async Task HandleFunctionCallArgumentsDoneAsync(JsonElement root)
        {
            _logger.Log(LogLevel.Info, "Received complete function call arguments");
            
            if (root.TryGetProperty("arguments", out var argsElement) && 
                root.TryGetProperty("name", out var callName) &&
                root.TryGetProperty("call_id", out var callIdProp))
            {
                string? callId = callIdProp.GetString() ?? string.Empty;
                string functionName = callName.GetString() ?? string.Empty;
                string argumentsJson = argsElement.GetString() ?? "{}";
                
                // Add Tool Call message to history
                var toolCallMessage = OpenAiChatMessage.CreateToolCallMessage(functionName, argumentsJson);
                _chatHistory.AddMessage(toolCallMessage);
                MessageAdded?.Invoke(this, toolCallMessage);
                
                // Find the tool
                var tool = FindToolForArguments(functionName, argumentsJson);
                
                if (tool != null)
                {
                    _logger.Log(LogLevel.Info, $"Executing tool: {tool.Name} (ID: {callId})");
                    try
                    {
                        string result = await tool.ExecuteAsync(argumentsJson);
                        
                        var toolResultMessage = OpenAiChatMessage.CreateToolResultMessage(tool.Name ?? "unknown_tool", result);
                        _chatHistory.AddMessage(toolResultMessage);
                        MessageAdded?.Invoke(this, toolResultMessage);
                        
                        await SendToolResultAsync(callId, result);
                        ToolResultAvailable?.Invoke(this, (tool.Name ?? "unknown_tool", result));
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(LogLevel.Error, $"Error executing tool '{tool.Name}' (ID: {callId}): {ex.Message}", ex);
                        string errorResult = JsonSerializer.Serialize(new { error = $"Failed to execute tool: {ex.Message}" });
                        
                        var toolErrorMessage = OpenAiChatMessage.CreateToolResultMessage(tool.Name ?? "unknown_tool", errorResult);
                        _chatHistory.AddMessage(toolErrorMessage);
                        MessageAdded?.Invoke(this, toolErrorMessage);
                        
                        await SendToolResultAsync(callId, errorResult);
                    }
                }
                else if (ToolCallRequested != null)
                {
                    ToolCallRequested?.Invoke(this, new ToolCallEventArgs(callId, functionName, argumentsJson));
                }
                else
                {
                    _logger.Log(LogLevel.Info, $"No tool implementation for call ID: {callId}");
                    string errorResult = JsonSerializer.Serialize(new { error = "No tool implementation available." });
                    
                    var toolNotFoundMessage = OpenAiChatMessage.CreateToolResultMessage("unknown_tool", errorResult);
                    _chatHistory.AddMessage(toolNotFoundMessage);
                    MessageAdded?.Invoke(this, toolNotFoundMessage);
                    
                    await SendToolResultAsync(callId, errorResult);
                }
            }
        }

        private bool ShouldAddMessage(string messageText, string role)
        {
            var messages = _chatHistory.GetMessages();
            return messages.Count == 0 || 
                   messages[messages.Count - 1].Role != role || 
                   messages[messages.Count - 1].Content != messageText;
        }

        private RealTimeTool? FindToolForArguments(string functionName, string argumentsJson)
        {
            return _tools.FirstOrDefault(t => string.Equals(t.Name, functionName, StringComparison.OrdinalIgnoreCase));
        }

        private async Task SendToolResultAsync(string callId, string result)
        {
            // Send the tool result
            await _sendMessageAsync(new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = result
                }
            });
            
            // Request a new response from the AI
            await _sendMessageAsync(new
            {
                type = "response.create"
            });
        }
    }
}