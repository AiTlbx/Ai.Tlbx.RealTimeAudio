using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;
using System.Collections.Concurrent;
using Ai.Tlbx.RealTimeAudio.OpenAi.Tools;

namespace Ai.Tlbx.RealTimeAudio.OpenAi
{
    /// <summary>
    /// Event arguments for when OpenAI requests a tool call.
    /// </summary>
    public class ToolCallEventArgs : EventArgs
    {
        public string ToolCallId { get; } // Unique ID for this specific call
        public string FunctionName { get; } // Name of the function/tool requested
        public string ArgumentsJson { get; } // Arguments as a JSON string

        public ToolCallEventArgs(string toolCallId, string functionName, string argumentsJson)
        {
            ToolCallId = toolCallId;
            FunctionName = functionName;
            ArgumentsJson = argumentsJson;
        }
    }

    public class OpenAiRealTimeApiAccess : IAsyncDisposable
    {
        private const string REALTIME_WEBSOCKET_ENDPOINT = "wss://api.openai.com/v1/realtime";
        private const int CONNECTION_TIMEOUT_MS = 10000;
        private const int MAX_RETRY_ATTEMPTS = 3;

        private readonly IAudioHardwareAccess _hardwareAccess;
        private readonly string _apiKey;
        private bool _isInitialized = false;
        private bool _isRecording = false;
        private bool _isConnecting = false;
        private bool _isDisposed = false;
        private string? _lastErrorMessage = null;
        private string _connectionStatus = string.Empty;
        private OpenAiRealTimeSettings _settings = new OpenAiRealTimeSettings();

        private ClientWebSocket? _webSocket;
        private Task? _receiveTask;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private DateTime _lastStatusUpdate = DateTime.MinValue;
        private const int STATUS_UPDATE_INTERVAL_MS = 500;
        private string _lastRaisedStatus = string.Empty;

        private List<OpenAiChatMessage> _chatHistory = new List<OpenAiChatMessage>();
        private StringBuilder _currentAiMessage = new StringBuilder();
        private StringBuilder _currentUserMessage = new StringBuilder();

        // Add these fields for microphone testing
        private bool _isMicrophoneTesting = false;
        private List<string> _micTestAudioChunks = new List<string>();
        private readonly object _micTestLock = new object();
        private CancellationTokenSource? _micTestCancellation;

        public event EventHandler<string>? ConnectionStatusChanged;
        public event EventHandler<OpenAiChatMessage>? MessageAdded;
        public event EventHandler<string>? MicrophoneTestStatusChanged;
        public event EventHandler<(string ToolName, string Result)>? ToolResultAvailable;
        public event EventHandler<ToolCallEventArgs>? ToolCallRequested;

        // Public readonly properties to expose internal state
        public bool IsInitialized => _isInitialized;
        public bool IsRecording => _isRecording;
        public bool IsConnecting => _isConnecting;
        public string? LastErrorMessage => _lastErrorMessage;
        public string ConnectionStatus => _connectionStatus;
        public IReadOnlyList<OpenAiChatMessage> ChatHistory => _chatHistory.AsReadOnly();
        
        public OpenAiRealTimeSettings Settings => _settings;

        // For backward compatibility
        public TurnDetectionSettings TurnDetectionSettings
        {
            get => _settings.TurnDetection;
        }

        public AssistantVoice CurrentVoice
        {
            get => _settings.Voice;
            set
            {
                _settings.Voice = value;
                SetVoice(value);
            }
        }

        public bool IsMicrophoneTesting => _isMicrophoneTesting;

        /// <summary>
        /// Initializes a new instance of the OpenAiRealTimeApiAccess class.
        /// </summary>
        /// <param name="hardwareAccess">The audio hardware access implementation.</param>
        public OpenAiRealTimeApiAccess(IAudioHardwareAccess hardwareAccess)
        {
            _hardwareAccess = hardwareAccess;
            _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                throw new InvalidOperationException("OPENAI_API_KEY not set");

            // Subscribe to audio error events through the interface
            _hardwareAccess.AudioError += OnAudioError;
        }

        public async Task InitializeConnection()
        {
            if (_isInitialized && _webSocket?.State == WebSocketState.Open)
            {
                RaiseStatus("Already initialized");
                return;
            }

            _isConnecting = true;
            _lastErrorMessage = null;
            await Cleanup();

            try
            {
                RaiseStatus("Initializing audio system...");
                await _hardwareAccess.InitAudio();

                RaiseStatus("Connecting to OpenAI API...");
                await Connect();

                RaiseStatus("Configuring session...");
                await ConfigureSession();

                _isInitialized = true;
                _isConnecting = false;
                RaiseStatus("Connection initialized, ready for voice selection or recording");
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                _isConnecting = false;
                _isInitialized = false;
                RaiseStatus($"Initialization failed: {ex.Message}");
                throw;
            }
        }        

        /// <summary>
        /// Starts the full lifecycle - initializes the connection if needed and starts recording audio
        /// </summary>
        /// <param name="settings">Optional settings to configure the API behavior</param>
        public async Task Start(OpenAiRealTimeSettings? settings = null)
        {
            try
            {
                _lastErrorMessage = null;
                
                // If settings are provided, store them for this session
                if (settings != null)
                {
                    _settings = settings;
                }
                
                if (!_isInitialized || _webSocket?.State != WebSocketState.Open)
                {
                    _isConnecting = true;
                    RaiseStatus("Connection not initialized, connecting first...");
                    await InitializeConnection(); // ConfigureSession is called within InitializeConnection
                }

                RaiseStatus("Starting audio recording...");
                bool success = await _hardwareAccess.StartRecordingAudio(OnAudioDataReceived);

                if (success)
                {
                    _isRecording = true;
                    RaiseStatus("Recording started successfully");
                }
                else
                {
                    RaiseStatus("Failed to start recording, attempting to reinitialize");
                    await Cleanup();
                    await Task.Delay(500);
                    _isConnecting = true;
                    await InitializeConnection();
                    success = await _hardwareAccess.StartRecordingAudio(OnAudioDataReceived);
                    
                    if (success)
                    {
                        _isRecording = true;
                        RaiseStatus("Recording started after reconnection");
                    }
                    else
                    {
                        _lastErrorMessage = "Failed to start recording after reconnection";
                        RaiseStatus("Recording failed, please reload the page");
                    }
                }
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                _isRecording = false;
                _isConnecting = false;
                RaiseStatus($"Error starting: {ex.Message}");
            }
        }

        /// <summary>
        /// For backward compatibility - starts the session with just turn detection settings
        /// </summary>
        public async Task Start(TurnDetectionSettings? turnDetectionSettings)
        {
            if (turnDetectionSettings != null)
            {
                _settings.TurnDetection = turnDetectionSettings;
            }
            await Start();
        }

        /// <summary>
        /// Stops everything - clears audio queue, stops recording, and closes the connection
        /// </summary>
        public async Task Stop()
        {
            try
            {
                // First clear any playing audio
                await _hardwareAccess.ClearAudioQueue();
                
                // Stop recording
                if (_isRecording)
                {
                    await _hardwareAccess.StopRecordingAudio();
                    _isRecording = false;
                }
                
                // Close connection
                if (_isInitialized)
                {
                    await Cleanup();
                }
                
                RaiseStatus("Stopped recording and closed connection");
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                RaiseStatus($"Error stopping: {ex.Message}");
            }
        }

        private void SetVoice(AssistantVoice voice)
        {
            try
            {
                _lastErrorMessage = null;                
                
                if (_isInitialized && _webSocket?.State == WebSocketState.Open)
                {
                    _ = SendAsync(new
                    {
                        type = "session.update",
                        session = new { voice = _settings.GetVoiceString() }
                    });
                    RaiseStatus($"Voice updated to: {voice}");
                }
                else
                {
                    RaiseStatus($"Voice set to: {voice} (will be applied when connected)");
                }
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                RaiseStatus($"Error setting voice: {ex.Message}");
            }
        }

        private async Task ConfigureSession()
        {
            try
            {
                // Build the session part dynamically
                object? turnDetectionConfig = BuildTurnDetectionConfig();
                
                // Convert the tools from settings to the format OpenAI expects
                List<object>? toolsConfig = null;
                if (_settings.Tools != null && _settings.Tools.Count > 0)
                {
                    // Create tools array using anonymous objects with explicit lowercase property names
                    toolsConfig = new List<object>();
                    foreach (var toolDef in _settings.Tools)
                    {
                        if (toolDef?.Function?.Name == null) continue;
                        
                        // Create a tool object with the correct format according to OpenAI docs
                        toolsConfig.Add(new 
                        {
                            type = "function",
                            name = toolDef.Function.Name,
                            description = toolDef.Function.Description,
                            parameters = toolDef.Function.Parameters
                        });
                    }
                }

                // Send the current serialized JSON to the debug log to help diagnose issues
                var debugOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                string debugTools = JsonSerializer.Serialize(toolsConfig, debugOptions);
                System.Diagnostics.Debug.WriteLine($"[ConfigureSession] Tool definitions: {debugTools}");

                // Construct the session object more explicitly
                var sessionPayload = new {
                    model = "gpt-4o-realtime-preview-2024-12-17", 
                    voice = _settings.GetVoiceString(),
                    modalities = _settings.Modalities.ToArray(),
                    input_audio_format = _settings.GetAudioFormatString(_settings.InputAudioFormat),
                    output_audio_format = _settings.GetAudioFormatString(_settings.OutputAudioFormat),
                    input_audio_transcription = new { model = _settings.GetTranscriptionModelString() },
                    instructions = _settings.Instructions,
                    turn_detection = turnDetectionConfig, 
                    tools = toolsConfig 
                };

                var sessionConfigMessage = new
                {
                    type = "session.update",
                    session = sessionPayload
                };

                // Force camelCase serialization for all properties
                var camelCaseOptions = new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                
                // Log the exact JSON being sent
                string configJson = JsonSerializer.Serialize(sessionConfigMessage, camelCaseOptions);
                System.Diagnostics.Debug.WriteLine($"[WebSocket] Sending session config: {configJson}");
                
                // Send the configuration JSON as a string to prevent any serialization issues
                var bytes = Encoding.UTF8.GetBytes(configJson);
                await _webSocket!.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                
                string turnTypeDesc = _settings.TurnDetection.Type switch 
                {
                    TurnDetectionType.SemanticVad => "semantic VAD",
                    TurnDetectionType.ServerVad => "server VAD",
                    TurnDetectionType.None => "no turn detection",
                    _ => "unknown turn detection"
                };

                string toolsDesc = (toolsConfig != null && toolsConfig.Count > 0) ? $" with {toolsConfig.Count} tool(s)" : "";

                RaiseStatus($"Session configured with voice: {_settings.GetVoiceString()}, {turnTypeDesc}{toolsDesc}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigureSession] Error: {ex.Message}");
                RaiseStatus($"Error configuring session: {ex.Message}");
                throw;
            }
        }
        
        private object? BuildTurnDetectionConfig()
        {
            // If turn detection is disabled, return null
            if (_settings.TurnDetection.Type == TurnDetectionType.None)
            {
                return null;
            }
            
            // For semantic VAD
            if (_settings.TurnDetection.Type == TurnDetectionType.SemanticVad)
            {
                return new 
                {
                    // Use "semantic_vad" as defined by JsonPropertyName attribute
                    type = GetJsonPropertyName(_settings.TurnDetection.Type) ?? "semantic_vad",
                    // Use value from JsonPropertyName attribute
                    eagerness = GetJsonPropertyName(_settings.TurnDetection.Eagerness) ?? "auto",
                    create_response = _settings.TurnDetection.CreateResponse,
                    interrupt_response = _settings.TurnDetection.InterruptResponse
                };
            }
            
            // For server VAD
            return new
            {
                // Use "server_vad" as defined by JsonPropertyName attribute
                type = GetJsonPropertyName(_settings.TurnDetection.Type) ?? "server_vad",
                threshold = _settings.TurnDetection.Threshold ?? 0.5f,
                prefix_padding_ms = _settings.TurnDetection.PrefixPaddingMs ?? 300,
                silence_duration_ms = _settings.TurnDetection.SilenceDurationMs ?? 500
            };
        }
        
        /// <summary>
        /// Get the JsonPropertyName attribute value for an enum
        /// </summary>
        private string? GetJsonPropertyName<T>(T enumValue) where T : Enum
        {
            var enumType = typeof(T);
            var memberInfo = enumType.GetMember(enumValue.ToString());
            
            if (memberInfo.Length > 0)
            {
                var attributes = memberInfo[0].GetCustomAttributes(typeof(JsonPropertyNameAttribute), false);
                if (attributes.Length > 0)
                {
                    return ((JsonPropertyNameAttribute)attributes[0]).Name;
                }
            }
            
            return null;
        }

        private async Task Connect()
        {
            int delayMs = 1000;  // Start with 1 second delay between retries
            
            for (int i = 0; i < MAX_RETRY_ATTEMPTS; i++)
            {
                try
                {
                    // Dispose of any existing WebSocket
                    if (_webSocket != null)
                    {
                        try 
                        {
                            _webSocket.Dispose();
                        }
                        catch { /* Ignore any errors during disposal */ }
                        _webSocket = null;
                    }
                    
                    // Create a new WebSocket
                    _webSocket = new ClientWebSocket();
                    _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    _webSocket.Options.SetRequestHeader("openai-beta", "realtime=v1");
                    
                    // Set sensible timeouts
                    _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                    
                    Debug.WriteLine($"[WebSocket] Connecting to OpenAI API, attempt {i + 1} of {MAX_RETRY_ATTEMPTS}...");
                    RaiseStatus($"Connecting to OpenAI API ({i + 1}/{MAX_RETRY_ATTEMPTS})...");

                    using var cts = new CancellationTokenSource(CONNECTION_TIMEOUT_MS);
                    await _webSocket.ConnectAsync(
                        new Uri($"{REALTIME_WEBSOCKET_ENDPOINT}?model=gpt-4o-realtime-preview-2024-12-17"),
                        cts.Token);

                    Debug.WriteLine("[WebSocket] Connected successfully");
                    
                    // Create a new cancellation token source for the receive task
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                    
                    // Start the receive task
                    _receiveTask = ReceiveAsync(_cts.Token);
                    return;
                }
                catch (WebSocketException wsEx)
                {
                    // Handle WebSocket specific exceptions
                    _webSocket?.Dispose();
                    _webSocket = null;
                    
                    Debug.WriteLine($"[WebSocket] WebSocket error on connect attempt {i + 1}: {wsEx.Message}, WebSocketErrorCode: {wsEx.WebSocketErrorCode}");
                    RaiseStatus($"Connection error: {wsEx.Message}");
                    
                    if (i < MAX_RETRY_ATTEMPTS - 1) 
                    {
                        await Task.Delay(delayMs);
                        delayMs = Math.Min(delayMs * 2, 10000); // Exponential backoff, max 10 seconds
                    }
                }
                catch (TaskCanceledException)
                {
                    // Connection timeout
                    _webSocket?.Dispose();
                    _webSocket = null;
                    
                    Debug.WriteLine($"[WebSocket] Connection timeout on attempt {i + 1}");
                    RaiseStatus($"Connection timeout");
                    
                    if (i < MAX_RETRY_ATTEMPTS - 1) 
                    {
                        await Task.Delay(delayMs);
                        delayMs = Math.Min(delayMs * 2, 10000);
                    }
                }
                catch (Exception ex)
                {
                    // Handle other exceptions
                    _webSocket?.Dispose();
                    _webSocket = null;
                    
                    Debug.WriteLine($"[WebSocket] Connect attempt {i + 1} failed: {ex.Message}");
                    RaiseStatus($"Connection error: {ex.Message}");
                    
                    if (i < MAX_RETRY_ATTEMPTS - 1) 
                    {
                        await Task.Delay(delayMs);
                        delayMs = Math.Min(delayMs * 2, 10000);
                    }
                }
            }
            
            throw new InvalidOperationException("Connection failed after maximum retry attempts");
        }

        private async Task ReceiveAsync(CancellationToken ct)
        {
            var buffer = new byte[32384];
            int consecutiveErrorCount = 0;
            
            while (_webSocket?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close) 
                        {
                            Debug.WriteLine($"[WebSocket] Received close message with status: {result.CloseStatus}, description: {result.CloseStatusDescription}");
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage && _webSocket?.State == WebSocketState.Open);

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    await HandleMessageAsync(json);
                    
                    // Reset error counter on successful message
                    consecutiveErrorCount = 0;
                }
                catch (WebSocketException wsEx)
                {
                    consecutiveErrorCount++;
                    
                    // Log but don't treat as critical if it's a normal closure
                    if (wsEx.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        Debug.WriteLine("[WebSocket] Connection closed prematurely by server");
                        RaiseStatus("Connection closed by server, will attempt to reconnect if needed");
                        break; // Exit the loop to allow reconnection logic to run
                    }
                    else
                    {
                        Debug.WriteLine($"[WebSocket] WebSocket error: {wsEx.Message}, ErrorCode: {wsEx.WebSocketErrorCode}");
                        RaiseStatus($"WebSocket error: {wsEx.Message}");
                        
                        if (consecutiveErrorCount > 3)
                        {
                            RaiseStatus("Too many consecutive WebSocket errors, reconnecting...");
                            // Force a reconnection
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[WebSocket] Receive operation canceled");
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveErrorCount++;
                    Debug.WriteLine($"[WebSocket] Receive error: {ex.Message}");
                    RaiseStatus($"Receive error: {ex.Message}");
                    
                    if (consecutiveErrorCount > 3)
                    {
                        RaiseStatus("Too many consecutive receive errors, reconnecting...");
                        break;
                    }
                    
                    // Add a small delay before trying again to avoid hammering the server
                    await Task.Delay(500, CancellationToken.None);
                }
            }
            
            // If we exited the loop and the connection is still active, try to restart it
            if (!ct.IsCancellationRequested && _isInitialized && _webSocket != null)
            {
                Debug.WriteLine("[WebSocket] WebSocket loop exited, attempting to reconnect...");
                _ = Task.Run(async () => 
                {
                    await Task.Delay(1000); // Wait a moment before reconnecting
                    await ReconnectAsync();
                }, CancellationToken.None);
            }
        }

        private async Task HandleMessageAsync(string json)
        {
            try
            {
                // Using JsonDocument which is disposable
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Basic check for "type" property
                if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                {
                    Debug.WriteLine($"[WebSocket] Received message without a valid 'type' property: {json.Substring(0, Math.Min(100, json.Length))}...");
                    return;
                }
                
                var type = typeElement.GetString();

                switch (type)
                {
                    case "error":
                        // Extract and log detailed error information
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

                        Debug.WriteLine($"[WebSocket] {errorDetails}");
                        RaiseStatus($"OpenAI API Error: {errorMessage}");
                        break;

                    case "rate_limits.updated":
                        Debug.WriteLine($"[WebSocket] Rate Limit Update: {json}");
                        break;

                    case "response.audio.delta":
                        var audio = root.GetProperty("delta").GetString();
                        Debug.WriteLine($"[WebSocket] Audio delta received, length: {audio?.Length ?? 0}");
                        if (!string.IsNullOrEmpty(audio))
                        {
                            try
                            {
                                Debug.WriteLine("[WebSocket] Attempting to play audio...");
                                _hardwareAccess.PlayAudio(audio, 24000);
                                Debug.WriteLine("[WebSocket] PlayAudio called successfully");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[WebSocket] ERROR playing audio: {ex.Message}");
                                Debug.WriteLine($"[WebSocket] Stack trace: {ex.StackTrace}");
                            }
                        }
                        break;

                    case "response.audio_transcript.done":
                        if (root.TryGetProperty("transcript", out var spokenText))
                        {
                            Debug.WriteLine($"[WebSocket] Transcript received: {spokenText}");
                        }
                        break;

                    case "response.done":
                        Debug.WriteLine("[WebSocket] Full response completed");
                        try
                        {
                            // Extract server response text from the 'response.done' message
                            if (root.TryGetProperty("response", out var responseObj) &&
                                responseObj.TryGetProperty("output", out var outputArray) &&
                                outputArray.GetArrayLength() > 0)
                            {
                                var firstOutput = outputArray[0];
                                if (firstOutput.TryGetProperty("content", out var contentArray) &&
                                    contentArray.GetArrayLength() > 0)
                                {
                                    StringBuilder fullText = new StringBuilder();
                                    
                                    // Process all content items
                                    foreach (var content in contentArray.EnumerateArray())
                                    {
                                        // Handle text content
                                        if (content.TryGetProperty("type", out var contentType))
                                        {
                                            string contentTypeStr = contentType.GetString() ?? string.Empty;
                                            
                                            if (contentTypeStr == "text" && content.TryGetProperty("text", out var textElement))
                                            {
                                                string text = textElement.GetString() ?? string.Empty;
                                                fullText.Append(text);
                                                Debug.WriteLine($"[WebSocket] Extracted text from response.done: {text}");
                                            }
                                            // Handle audio transcript
                                            else if (contentTypeStr == "audio" && content.TryGetProperty("transcript", out var transcriptElement))
                                            {
                                                string transcript = transcriptElement.GetString() ?? string.Empty;
                                                fullText.Append(transcript);
                                                Debug.WriteLine($"[WebSocket] Extracted audio transcript from response.done: {transcript}");
                                            }
                                        }
                                    }
                                    
                                    string completeMessage = fullText.ToString();
                                    if (!string.IsNullOrWhiteSpace(completeMessage))
                                    {
                                        Debug.WriteLine($"[WebSocket] Final extracted message from response.done: {completeMessage}");
                                        
                                        // Add to chat history if new or different from last message
                                        if (_chatHistory.Count == 0 || 
                                           _chatHistory[_chatHistory.Count - 1].Role == "user" || 
                                           _chatHistory[_chatHistory.Count - 1].Content != completeMessage)
                                        {
                                            var message = new OpenAiChatMessage(completeMessage, "assistant");
                                            _chatHistory.Add(message);
                                            MessageAdded?.Invoke(this, message);
                                            Debug.WriteLine("[WebSocket] Added message to chat history via response.done");
                                        }
                                        
                                        // Clear the AI message buffer since we've got the complete message
                                        _currentAiMessage.Clear();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[WebSocket] Error processing response.done message: {ex.Message}");
                        }
                        break;

                    case "response.text.delta":
                        if (root.TryGetProperty("delta", out var deltaElem) && 
                            deltaElem.TryGetProperty("text", out var textElem))
                        {
                            string deltaText = textElem.GetString() ?? string.Empty;
                            _currentAiMessage.Append(deltaText);
                            Debug.WriteLine($"[WebSocket] Text delta received: '{deltaText}'");
                        }
                        break;

                    case "response.text.done":
                        if (_currentAiMessage.Length > 0)
                        {
                            string messageText = _currentAiMessage.ToString();
                            Debug.WriteLine($"[WebSocket] Text done received, message: '{messageText}'");
                            
                            // Check if we should add this message to the chat history
                            // We might get both response.text.done and response.output_item.done,
                            // so we need to check if we've already added a message with this text
                            if (_chatHistory.Count == 0 || 
                               _chatHistory[_chatHistory.Count - 1].Role == "user" || 
                               _chatHistory[_chatHistory.Count - 1].Content != messageText)
                            {
                                var message = new OpenAiChatMessage(messageText, "assistant");
                                _chatHistory.Add(message);
                                MessageAdded?.Invoke(this, message);
                                Debug.WriteLine("[WebSocket] Added message to chat history via text.done");
                            }
                            
                            _currentAiMessage.Clear();
                        }
                        break;

                    case "conversation.item.input_audio_transcription.completed":
                        if (root.TryGetProperty("transcript", out var transcriptElem))
                        {
                            string transcript = transcriptElem.GetString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(transcript))
                            {
                                var message = new OpenAiChatMessage(transcript, "user");
                                _chatHistory.Add(message);
                                MessageAdded?.Invoke(this, message);
                                _currentUserMessage.Clear();
                                RaiseStatus("User said: " + transcript);
                            }
                        }
                        break;

                    case "input_audio_buffer.speech_started":
                        RaiseStatus("Speech detected");
                        await SendAsync(new
                        {
                            type = "response.cancel",                            
                        });
                        await _hardwareAccess.ClearAudioQueue(); // when user speaks, open ai needs to shut up
                        break;

                    case "input_audio_buffer.speech_stopped":
                        RaiseStatus("Speech ended");
                        break;

                    case "conversation.item.start":
                        Debug.WriteLine("[WebSocket] New conversation item started");
                        if (root.TryGetProperty("role", out var roleElem))
                        {
                            string role = roleElem.GetString() ?? string.Empty;
                            Debug.WriteLine($"[WebSocket] Item role: {role}");
                            if (role == "assistant")
                            {
                                // Reset AI message for new response
                                _currentAiMessage.Clear();
                            }
                        }
                        break;

                    case "conversation.item.end":
                        Debug.WriteLine("[WebSocket] Conversation item ended");
                        break;

                    case "response.output_item.done":
                        Debug.WriteLine("[WebSocket] Received complete message from assistant");
                        try
                        {
                            if (root.TryGetProperty("item", out var itemElem) && 
                                itemElem.TryGetProperty("content", out var contentArray))
                            {
                                StringBuilder completeMessage = new StringBuilder();
                                
                                // Content is an array of content parts
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
                                Debug.WriteLine($"[WebSocket] Complete message text: {messageText}");
                                
                                // Only add if we have content and haven't already added via deltas
                                if (!string.IsNullOrWhiteSpace(messageText) && 
                                    (_chatHistory.Count == 0 || 
                                     _chatHistory[_chatHistory.Count - 1].Role == "user" || 
                                     _chatHistory[_chatHistory.Count - 1].Content != messageText))
                                {
                                    var message = new OpenAiChatMessage(messageText, "assistant");
                                    _chatHistory.Add(message);
                                    MessageAdded?.Invoke(this, message);
                                    
                                    // Clear the current message since we've now got the complete version
                                    _currentAiMessage.Clear();
                                    
                                    RaiseStatus("Received complete message from assistant");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[WebSocket] Error processing complete message: {ex.Message}");
                        }
                        break;

                    case "response.function_call_arguments.delta":
                        // Streaming mode for function call arguments - we could accumulate these if needed
                        Debug.WriteLine($"[WebSocket] Received function call arguments delta");
                        break;

                    case "response.function_call_arguments.done":
                        Debug.WriteLine($"[WebSocket] Received complete function call arguments: {json.Substring(0, Math.Min(200, json.Length))}...");
                        if (root.TryGetProperty("arguments", out var argsElement) && 
                            root.TryGetProperty("call_id", out var callIdElement))
                        {
                            string callId = callIdElement.GetString() ?? string.Empty;
                            string argumentsJson = argsElement.GetString() ?? "{}";
                            string functionName = string.Empty;
                            
                            // Try to extract function name if available
                            if (root.TryGetProperty("item_id", out var itemIdElement))
                            {
                                // Some implementations may send function name differently
                                // We might need to extract it from arguments or other sources
                                functionName = itemIdElement.GetString() ?? string.Empty;
                            }
                            
                            // Add Tool Call message to history
                            var toolCallMessage = OpenAiChatMessage.CreateToolCallMessage(callId, functionName, argumentsJson);
                            _chatHistory.Add(toolCallMessage);
                            MessageAdded?.Invoke(this, toolCallMessage); // Notify UI
                            
                            // Find the tool in the registered tools - by name or try to determine from arguments
                            var tool = FindToolForArguments(functionName, argumentsJson);
                            
                            if (tool != null)
                            {
                                // Execute the tool directly
                                Debug.WriteLine($"[Tooling] Executing tool: {tool.Name} (ID: {callId})");
                                try
                                {
                                    // Execute the tool
                                    string result = await tool.ExecuteAsync(argumentsJson);
                                    
                                    // Add Tool Result message to history
                                    var toolResultMessage = OpenAiChatMessage.CreateToolResultMessage(callId, tool.Name, result);
                                    _chatHistory.Add(toolResultMessage);
                                    MessageAdded?.Invoke(this, toolResultMessage); // Notify UI
                                    
                                    // Send the result back to OpenAI using the correct format
                                    await SendToolResultAsync(callId, result);
                                    
                                    // Notify any listeners about the tool result
                                    ToolResultAvailable?.Invoke(this, (tool.Name, result));
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[Tooling] Error executing tool '{tool.Name}' (ID: {callId}): {ex.Message}");
                                    string errorResult = JsonSerializer.Serialize(new { error = $"Failed to execute tool: {ex.Message}" });
                                    
                                    // Add a failure message
                                    var toolErrorMessage = OpenAiChatMessage.CreateToolResultMessage(callId, tool.Name, errorResult);
                                    _chatHistory.Add(toolErrorMessage);
                                    MessageAdded?.Invoke(this, toolErrorMessage);
                                    
                                    // Send the error back to OpenAI
                                    await SendToolResultAsync(callId, errorResult);
                                }
                            }
                            else if (ToolCallRequested != null)
                            {
                                // If we don't have the tool implementation but external handlers are subscribed
                                // Notify external subscribers 
                                ToolCallRequested?.Invoke(this, new ToolCallEventArgs(callId, functionName, argumentsJson));
                            }
                            else
                            {
                                // No tool handler available
                                Debug.WriteLine($"[Tooling] No tool implementation for call ID: {callId}");
                                string errorResult = JsonSerializer.Serialize(new { error = "No tool implementation available." });
                                
                                // Add a result message indicating failure
                                var toolNotFoundMessage = OpenAiChatMessage.CreateToolResultMessage(callId, "unknown_tool", errorResult);
                                _chatHistory.Add(toolNotFoundMessage);
                                MessageAdded?.Invoke(this, toolNotFoundMessage);
                                
                                // Send the error result back to OpenAI
                                await SendToolResultAsync(callId, errorResult);
                            }
                        }
                        break;

                    default:
                        // Log all unhandled message types to debug output
                        Debug.WriteLine($"[WebSocket] Unhandled message type: {type} - Content: {json.Substring(0, Math.Min(100, json.Length))}...");
                        break;
                }
            }
            catch (JsonException jsonEx)
            {
                 RaiseStatus($"Error parsing JSON message: {jsonEx.Message}. JSON: {json.Substring(0, Math.Min(100, json.Length))}...");
                 Debug.WriteLine($"[WebSocket] JSON Parse Error: {jsonEx.Message} for JSON: {json}");
            }
            catch (Exception ex)
            {
                // Catching generic Exception should be done carefully. Ensure specific exceptions are handled above.
                RaiseStatus($"Error handling message: {ex.Message}. JSON: {json.Substring(0, Math.Min(100, json.Length))}...");
                Debug.WriteLine($"[WebSocket] General Error Handling Message: {ex.Message} for JSON: {json}");
                Debug.WriteLine($"[WebSocket] StackTrace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Helper method to parse a JsonElement representing a single tool call.
        /// </summary>
        private bool TryParseToolCall(JsonElement toolCallElement, out string toolCallId, out string functionName, out string argumentsJson)
        {
             toolCallId = string.Empty;
             functionName = string.Empty;
             argumentsJson = string.Empty;

             if (toolCallElement.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String &&
                 toolCallElement.TryGetProperty("type", out var toolTypeElement) && toolTypeElement.GetString() == "function" &&
                 toolCallElement.TryGetProperty("function", out var functionElement) && functionElement.ValueKind == JsonValueKind.Object &&
                 functionElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String &&
                 functionElement.TryGetProperty("arguments", out var argsElement) && argsElement.ValueKind == JsonValueKind.String)
             {
                 toolCallId = idElement.GetString() ?? string.Empty;
                 functionName = nameElement.GetString() ?? string.Empty;
                 argumentsJson = argsElement.GetString() ?? string.Empty;

                 if (!string.IsNullOrEmpty(toolCallId) && !string.IsNullOrEmpty(functionName))
                 {
                    return true;
                 }
                 else
                 {
                     Debug.WriteLine($"[WebSocket] Failed to parse tool call details: ID or Function Name missing.");
                     return false;
                 }
             }
             else
             {
                 Debug.WriteLine($"[WebSocket] Could not parse tool call structure in message element: {toolCallElement.GetRawText()}");
                 return false;
             }
        }

        private async void OnAudioDataReceived(object sender, MicrophoneAudioReceivedEvenArgs e)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open)
                {
                    RaiseStatus("Warning: Received audio data but WebSocket is not open");
                    return;
                }

                if (string.IsNullOrEmpty(e.Base64EncodedPcm16Audio))
                {
                    RaiseStatus("Warning: Received empty audio data");
                    return;
                }

                // Send audio data to OpenAI
                await SendAsync(new
                {
                    type = "input_audio_buffer.append",
                    audio = e.Base64EncodedPcm16Audio
                });
            }
            catch (Exception ex)
            {
                RaiseStatus($"Error sending audio data: {ex.Message}");
            }
        }

        private async Task SendAsync(object message)
        {
            string? json = null;

            try
            {
                if (_webSocket?.State != WebSocketState.Open) 
                {
                    System.Diagnostics.Debug.WriteLine("[WebSocket] Cannot send message, socket not open");
                    return;
                }

                try
                {
                    // Separate serialization from send to catch and log serialization errors
                    json = JsonSerializer.Serialize(message, _jsonOptions);
                    System.Diagnostics.Debug.WriteLine($"[WebSocket] Sending message type: {message.GetType().Name}, Length: {json.Length}");
                }
                catch (JsonException jsonEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebSocket] JSON serialization error: {jsonEx.Message}");
                    RaiseStatus($"Error serializing message: {jsonEx.Message}");
                    throw; // Re-throw to be caught by outer catch
                }

                var bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebSocket] Error sending message: {ex.Message}\nJSON: {json}");
                RaiseStatus($"Error sending message to OpenAI: {ex.Message}");
            }
        }

        private async Task Cleanup()
        {
            try
            {
                // Mark as not connected first
                _isInitialized = false;
                _isRecording = false;
                
                // First stop any ongoing recording
                try 
                {
                    await _hardwareAccess.StopRecordingAudio();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebSocket] Error stopping recording during cleanup: {ex.Message}");
                }

                // Cancel the receive task if it exists
                if (_cts != null)
                {
                    try
                    {
                        _cts.Cancel();
                        
                        if (_receiveTask != null)
                        {
                            try
                            {
                                // Give the receive task a chance to complete gracefully
                                await Task.WhenAny(_receiveTask, Task.Delay(2000));
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[WebSocket] Error waiting for receive task to complete: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WebSocket] Error canceling receive task: {ex.Message}");
                    }
                }

                // Close the WebSocket connection if it's open
                if (_webSocket != null)
                {
                    try
                    {
                        if (_webSocket.State == WebSocketState.Open)
                        {
                            try
                            {
                                var closeTask = _webSocket.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure, 
                                    "Cleanup", 
                                    CancellationToken.None);
                                
                                // Add a timeout to the close operation
                                await Task.WhenAny(closeTask, Task.Delay(3000));
                                
                                if (!closeTask.IsCompleted)
                                {
                                    Debug.WriteLine("[WebSocket] Close operation timed out");
                                }
                            }
                            catch (WebSocketException wsEx)
                            {
                                Debug.WriteLine($"[WebSocket] WebSocket error during close: {wsEx.Message}");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[WebSocket] Error closing WebSocket: {ex.Message}");
                            }
                        }
                        
                        // Dispose WebSocket regardless of close success
                        _webSocket.Dispose();
                    }
                    catch (Exception ex) 
                    {
                        Debug.WriteLine($"[WebSocket] Error disposing WebSocket: {ex.Message}");
                    }
                    finally
                    {
                        _webSocket = null;
                    }
                }

                // Dispose of other resources
                try
                {
                    _cts?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebSocket] Error disposing CTS: {ex.Message}");
                }
                
                _cts = null;
                _receiveTask = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] Error during cleanup: {ex.Message}");
                RaiseStatus($"Error during cleanup: {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;
            
            try
            {
                // Unsubscribe from audio error events
                _hardwareAccess.AudioError -= OnAudioError;
                
                // Close the WebSocket connection if open
                await StopWebSocketReceive();
                
                _webSocket?.Dispose();
                _webSocket = null;
                
                // Cancel any operations
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                
                _isDisposed = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during disposal: {ex.Message}");
            }
        }

        public void Dispose()
        {
            // Use ConfigureAwait(false) to prevent deadlocks
            Cleanup().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private void RaiseStatus(string status)
        {
            _connectionStatus = status;

            bool isError = status.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                          status.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                          status.Contains("Exception", StringComparison.OrdinalIgnoreCase);
                          
            bool isHighPriorityInfo = status.Contains("initialized", StringComparison.OrdinalIgnoreCase) ||
                                    status.Contains("Recording started", StringComparison.OrdinalIgnoreCase) ||
                                    status.Contains("Recording stopped", StringComparison.OrdinalIgnoreCase);

            bool shouldUpdate = isError || isHighPriorityInfo ||
                (DateTime.Now - _lastStatusUpdate).TotalMilliseconds >= STATUS_UPDATE_INTERVAL_MS && status != _lastRaisedStatus;

            if (shouldUpdate)
            {
                if (isError)
                {
                    _lastErrorMessage = status;
                    _isConnecting = false;
                }

                ConnectionStatusChanged?.Invoke(this, status);
                _lastStatusUpdate = DateTime.Now;
                _lastRaisedStatus = status;
            }
        }

        /// <summary>
        /// Interrupts the AI's speech without stopping the session
        /// </summary>
        public async Task InterruptSpeech()
        {
            try
            {
                if (!_isInitialized || _webSocket?.State != WebSocketState.Open)
                {
                    RaiseStatus("Cannot interrupt: Connection not initialized");
                    return;
                }
                
                RaiseStatus("Manually interrupting AI speech...");
                
                // Send the response.cancel message to the server
                await SendAsync(new
                {
                    type = "response.cancel"
                });
                
                // Clear the audio queue to stop current playback
                await _hardwareAccess.ClearAudioQueue();
                
                RaiseStatus("AI speech interrupted");
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                RaiseStatus($"Error interrupting speech: {ex.Message}");
            }
        }

        public void ClearChatHistory()
        {
            _chatHistory.Clear();
            _currentAiMessage.Clear();
            _currentUserMessage.Clear();
        }

        // Handle audio hardware errors
        private void OnAudioError(object? sender, string errorMessage)
        {
            _lastErrorMessage = errorMessage;
            
            // If we're in the middle of recording, stop it to avoid further issues
            if (IsRecording)
            {
                Task.Run(async () => await Stop()).ConfigureAwait(false);
            }
        }

        private async Task StopWebSocketReceive()
        {
            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }

                if (_receiveTask != null)
                {
                    try
                    {
                        await Task.WhenAny(_receiveTask, Task.Delay(2000));
                    }
                    catch (Exception ex)
                    {
                        RaiseStatus($"Error waiting for receive task to complete: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseStatus($"Error stopping WebSocket receive: {ex.Message}");
            }
        }

        // Added to handle automatic reconnection when the WebSocket connection drops
        private async Task ReconnectAsync()
        {
            try
            {
                Debug.WriteLine("[WebSocket] Attempting to reconnect...");
                RaiseStatus("Connection lost, attempting to reconnect...");
                
                // Clean up existing resources
                await Cleanup();
                
                // Give a short delay before reconnecting
                await Task.Delay(1000);
                
                // Try to reconnect
                await Connect();
                
                // Reconfigure the session with the saved turn detection settings
                await ConfigureSession();
                
                RaiseStatus("Successfully reconnected");
                Debug.WriteLine("[WebSocket] Reconnection successful");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] Reconnection failed: {ex.Message}");
                RaiseStatus($"Reconnection failed: {ex.Message}");
                
                // Try again with exponential backoff if we're still initialized
                if (_isInitialized)
                {
                    _ = Task.Run(async () => 
                    {
                        await Task.Delay(5000); // Wait longer before the next attempt
                        await ReconnectAsync();
                    });
                }
            }
        }

        /// <summary>
        /// Updates the turn detection settings without starting recording
        /// </summary>
        /// <param name="settings">The new turn detection settings to use</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task UpdateTurnDetectionSettings(TurnDetectionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            _settings.TurnDetection = settings;
            
            if (_isInitialized && _webSocket?.State == WebSocketState.Open)
            {
                // If we're already connected, update the session immediately
                await SendAsync(new
                {
                    type = "session.update",
                    session = new { turn_detection = BuildTurnDetectionConfig() }
                });
                
                string turnTypeDesc = _settings.TurnDetection.Type switch 
                {
                    TurnDetectionType.SemanticVad => "semantic VAD",
                    TurnDetectionType.ServerVad => "server VAD",
                    TurnDetectionType.None => "no turn detection",
                    _ => "unknown turn detection"
                };
                
                RaiseStatus($"Turn detection updated to {turnTypeDesc}");
            }
        }
        
        /// <summary>
        /// Updates all settings for the API
        /// </summary>
        /// <param name="settings">The new settings to use</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task UpdateSettings(OpenAiRealTimeSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            // Store the settings
            _settings = settings;
                        
            // If we're already connected, update the session with all new settings
            if (_isInitialized && _webSocket?.State == WebSocketState.Open)
            {
                await ConfigureSession();
                RaiseStatus("Settings updated and applied");
            }
            else
            {
                RaiseStatus("Settings updated, will be applied when connected");
            }
        }

        /// <summary>
        /// Tests the microphone by recording 5 seconds of audio and playing it back
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task<bool> TestMicrophone()
        {
            // Can't test while recording or already testing
            if (_isRecording || _isMicrophoneTesting)
            {
                RaiseMicTestStatus("Cannot test microphone while recording or already testing");
                return false;
            }

            try
            {
                _isMicrophoneTesting = true;
                _micTestCancellation = new CancellationTokenSource();
                
                // Clear any previous test data
                lock (_micTestLock)
                {
                    _micTestAudioChunks.Clear();
                }
                
                // Initialize audio system if needed
                await _hardwareAccess.InitAudio();
                
                // Start recording audio
                RaiseMicTestStatus("Recording 5 seconds of audio...");
                
                bool success = await _hardwareAccess.StartRecordingAudio(OnMicTestAudioReceived);
                if (!success)
                {
                    _isMicrophoneTesting = false;
                    RaiseMicTestStatus("Failed to start recording for microphone test");
                    return false;
                }
                
                // Record for 5 seconds
                try
                {
                    await Task.Delay(5000, _micTestCancellation.Token);
                }
                catch (TaskCanceledException)
                {
                    // Test was canceled
                    RaiseMicTestStatus("Microphone test canceled");
                    await _hardwareAccess.StopRecordingAudio();
                    _isMicrophoneTesting = false;
                    return false;
                }
                
                // Stop recording
                await _hardwareAccess.StopRecordingAudio();
                
                // Play back the recorded audio
                RaiseMicTestStatus("Playing back recorded audio...");
                
                List<string> audioChunks;
                lock (_micTestLock)
                {
                    audioChunks = new List<string>(_micTestAudioChunks);
                }
                
                if (audioChunks.Count == 0)
                {
                    RaiseMicTestStatus("No audio was recorded. Check your microphone settings.");
                    _isMicrophoneTesting = false;
                    return false;
                }
                
                // Play back each chunk
                foreach (var chunk in audioChunks)
                {
                    _hardwareAccess.PlayAudio(chunk, 24000);
                    
                    // Check if canceled
                    if (_micTestCancellation.Token.IsCancellationRequested)
                    {
                        break;
                    }
                }
                
                RaiseMicTestStatus("Microphone test completed");
                _isMicrophoneTesting = false;
                return true;
            }
            catch (Exception ex)
            {
                RaiseMicTestStatus($"Error during microphone test: {ex.Message}");
                _isMicrophoneTesting = false;
                return false;
            }
            finally
            {
                _micTestCancellation?.Dispose();
                _micTestCancellation = null;
            }
        }
        
        /// <summary>
        /// Cancels an in-progress microphone test
        /// </summary>
        public void CancelMicrophoneTest()
        {
            if (_isMicrophoneTesting)
            {
                _micTestCancellation?.Cancel();
                RaiseMicTestStatus("Microphone test canceled");
                
                // Clear the audio queue to stop playback
                _ = _hardwareAccess.ClearAudioQueue();
            }
        }
        
        private void OnMicTestAudioReceived(object sender, MicrophoneAudioReceivedEvenArgs e)
        {
            if (_isMicrophoneTesting && !string.IsNullOrEmpty(e.Base64EncodedPcm16Audio))
            {
                lock (_micTestLock)
                {
                    _micTestAudioChunks.Add(e.Base64EncodedPcm16Audio);
                    Debug.WriteLine($"[MicTest] Recorded audio chunk: {e.Base64EncodedPcm16Audio.Length} chars");
                }
            }
        }
        
        private void RaiseMicTestStatus(string status)
        {
            Debug.WriteLine($"[MicTest] {status}");
            MicrophoneTestStatusChanged?.Invoke(this, status);
        }

        /// <summary>
        /// Finds a tool implementation based on function name or argument content.
        /// </summary>
        /// <param name="functionName">The name of the function if available</param>
        /// <param name="argumentsJson">The JSON string containing the arguments</param>
        /// <returns>The matching tool or null if not found</returns>
        private BaseTool? FindToolForArguments(string functionName, string argumentsJson)
        {
            // First try to find by name if available
            if (!string.IsNullOrEmpty(functionName))
            {
                var tool = _settings.RegisteredTools.FirstOrDefault(t => t.Name == functionName);
                if (tool != null)
                {
                    return tool;
                }
            }

            // If no name or not found by name, try to determine from arguments
            // This is a fallback mechanism in case the name isn't provided
            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                // Here you could check for specific properties in the arguments
                // that might indicate which tool to use
                
                // This approach is very implementation-specific and may not be reliable
                // Future versions might enhance this with more sophisticated matching
                
                // For now, if we have only one registered tool, assume it's the one we want
                if (_settings.RegisteredTools.Count == 1)
                {
                    return _settings.RegisteredTools[0];
                }
            }
            catch (JsonException)
            {
                // If we can't parse the arguments, we can't determine the tool
            }
            
            // No matching tool found
            return null;
        }

        /// <summary>
        /// Sends the result of a tool execution back to OpenAI using the correct message format.
        /// </summary>
        /// <param name="callId">The ID of the function call to respond to.</param>
        /// <param name="result">The result of the function execution.</param>
        public async Task SendToolResultAsync(string callId, string result)
        {
            if (_webSocket?.State != WebSocketState.Open)
            {
                RaiseStatus("Cannot send function result: WebSocket is not open.");
                Debug.WriteLine("[WebSocket] Attempted to send function result, but socket is not open.");
                return;
            }

            if (string.IsNullOrEmpty(callId))
            {
                 Debug.WriteLine("[WebSocket] Attempted to send function result with empty callId.");
                 return;
            }

            // Using the correct message format for the Realtime API
            var functionResultMessage = new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = result // The actual output from the function
                }
            };

            Debug.WriteLine($"[WebSocket] Sending function result for call ID: {callId}, Result: {result.Substring(0, Math.Min(100, result.Length))}...");
            await SendAsync(functionResultMessage);
        }

        /// <summary>
        /// Adds a tool to the current settings.
        /// </summary>
        /// <param name="tool">The tool to add.</param>
        public void AddTool(BaseTool tool)
        {
            if (tool == null)
                throw new ArgumentNullException(nameof(tool));

            // Add to RegisteredTools list if not already present
            if (!_settings.RegisteredTools.Any(t => t.Name == tool.Name))
            {
                _settings.RegisteredTools.Add(tool);
                
                // Also add the tool definition to the Tools list
                var toolDefinition = tool.GetToolDefinition();
                if (!_settings.Tools.Any(t => t.Function.Name == toolDefinition.Function.Name))
                {
                    _settings.Tools.Add(toolDefinition);
                }
                
                Debug.WriteLine($"[Tooling] Added tool to settings: {tool.Name}");
            }
        }
        
        /// <summary>
        /// Adds multiple tools to the current settings.
        /// </summary>
        /// <param name="tools">The collection of tools to add.</param>
        public void AddTools(IEnumerable<BaseTool> tools)
        {
            if (tools == null)
                throw new ArgumentNullException(nameof(tools));
                
            foreach (var tool in tools)
            {
                AddTool(tool);
            }
        }

        /// <summary>
        /// Validates that the settings are correct and complete
        /// </summary>
        /// <returns>True if settings are valid</returns>
        public bool ValidateSettings()
        {
            if (_settings == null)
                return false;
                
            // Check modalities
            if (_settings.Modalities == null || _settings.Modalities.Count == 0)
                return false;
                
            // Check instructions
            if (string.IsNullOrWhiteSpace(_settings.Instructions))
                return false;
                
            // Settings are valid
            return true;
        }

        /// <summary>
        /// Handles the result of a tool execution by adding it to the chat history and sending it to OpenAI.
        /// </summary>
        /// <param name="toolCallId">The unique ID of the tool call this result corresponds to.</param>
        /// <param name="toolName">The name of the tool that was called.</param>
        /// <param name="result">The result of the tool execution (often a JSON string).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleToolResultAsync(string toolCallId, string toolName, string result)
        {
            if (string.IsNullOrEmpty(toolCallId) || string.IsNullOrEmpty(toolName))
            {
                Debug.WriteLine("[Tooling] Tool call ID or name is empty. Cannot handle result.");
                return;
            }

            try
            {
                // Add a message to chat history for the tool result
                var toolResultMessage = OpenAiChatMessage.CreateToolResultMessage(toolCallId, toolName, result);
                _chatHistory.Add(toolResultMessage);
                MessageAdded?.Invoke(this, toolResultMessage);

                // Send the result back to OpenAI
                await SendToolResultAsync(toolCallId, result);

                // Notify any listeners about the tool result
                ToolResultAvailable?.Invoke(this, (toolName, result));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Tooling] Error handling tool result: {ex.Message}");
                RaiseStatus($"Error handling tool result: {ex.Message}");
            }
        }
    }    
}
