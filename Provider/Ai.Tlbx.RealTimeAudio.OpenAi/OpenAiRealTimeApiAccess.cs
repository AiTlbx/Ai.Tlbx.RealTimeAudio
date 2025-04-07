using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;
using System.Collections.Concurrent;
using Ai.Tlbx.RealTimeAudio.OpenAi.Tools;
using Ai.Tlbx.RealTimeAudio.OpenAi.Helper;

namespace Ai.Tlbx.RealTimeAudio.OpenAi
{
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

        // LOGSTAT-5: Define LogAction Delegate
        private readonly Action<LogLevel, string>? _logger;

        // LOGCLEAN-8: Add DefaultLogger
        private static readonly Action<LogLevel, string> _defaultLogger = (level, message) => 
        {
            Debug.WriteLine($"[{level}] {message}");
        };

        public event EventHandler<string>? ConnectionStatusChanged;
        public event EventHandler<OpenAiChatMessage>? MessageAdded;
        public event EventHandler<string>? MicrophoneTestStatusChanged;
        public event EventHandler<(string ToolName, string Result)>? ToolResultAvailable;
        public event EventHandler<ToolCallEventArgs>? ToolCallRequested;
        public event EventHandler<List<AudioDeviceInfo>>? MicrophoneDevicesChanged;
        
        // LOGSTAT-4: Define Unified StatusUpdated Event
        public event EventHandler<StatusUpdateEventArgs>? StatusUpdated;

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
            : this(hardwareAccess, _defaultLogger)
        {
        }

        // LOGSTAT-6: Inject Logger via Constructor
        public OpenAiRealTimeApiAccess(IAudioHardwareAccess hardwareAccess, Action<LogLevel, string>? logger)
        {
            _hardwareAccess = hardwareAccess;
            _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                throw new InvalidOperationException("OPENAI_API_KEY not set");

            // Subscribe to audio error events through the interface
            _hardwareAccess.AudioError += OnAudioError;
            
            _logger = logger ?? _defaultLogger;
        }
        
        // LOGCLEAN-1: Define Log Method with Category and LogLevel Parameters
        private void Log(LogCategory category, LogLevel level, string message, Exception? exception = null)
        {
            string formattedMessage = $"[{category}] {message}";
            
            // Log the message
            _logger?.Invoke(level, formattedMessage);
            
            // If there's an exception and it's an error or higher, log the exception details too
            if (exception != null && (level == LogLevel.Error || level == LogLevel.Critical))
            {
                _logger?.Invoke(level, $"[{category}] Exception: {exception.Message}\n{exception.StackTrace}");
            }
        }

        public async Task InitializeConnection()
        {
            if (_isInitialized && _webSocket?.State == WebSocketState.Open)
            {
                ReportStatus(StatusCategory.Connection, StatusCode.Connected, "Already initialized");
                return;
            }

            _isConnecting = true;
            _lastErrorMessage = null;
            await Cleanup();

            try
            {
                ReportStatus(StatusCategory.Configuration, StatusCode.Connecting, "Initializing audio system...");
                await _hardwareAccess.InitAudio();

                ReportStatus(StatusCategory.Connection, StatusCode.Connecting, "Connecting to OpenAI API...");
                await Connect();

                ReportStatus(StatusCategory.Configuration, StatusCode.Connecting, "Configuring session...");
                await ConfigureSession();

                _isInitialized = true;
                _isConnecting = false;
                ReportStatus(StatusCategory.Configuration, StatusCode.Initialized, "Connection initialized, ready for voice selection or recording");
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                _isConnecting = false;
                _isInitialized = false;
                ReportStatus(StatusCategory.Error, StatusCode.ConnectionFailed, $"Initialization failed: {ex.Message}", ex);
                throw;
            }
        }

        // LOGSTAT-7: Implement Internal ReportStatus Method
        private void ReportStatus(StatusCategory category, StatusCode code, string message, Exception? exception = null)
        {
            _lastErrorMessage = category == StatusCategory.Error ? message : _lastErrorMessage;
            _connectionStatus = message;
            _lastRaisedStatus = message;
            
            // LOGSTAT-8: Implement ReportStatus Logic (Call Logger, Raise Event)
            // Map category to log level
            LogLevel logLevel = category switch
            {
                StatusCategory.Error => LogLevel.Error,
                StatusCategory.Connection => LogLevel.Info,
                StatusCategory.Recording => LogLevel.Info,
                StatusCategory.Processing => LogLevel.Debug,
                StatusCategory.Configuration => LogLevel.Info,
                StatusCategory.Tool => LogLevel.Debug,
                _ => LogLevel.Info
            };
            
            // Call logger if provided
            _logger?.Invoke(logLevel, $"[{category}] {code}: {message}");
            
            // If exception exists and is an error, log it as well
            if (exception != null && category == StatusCategory.Error)
            {
                _logger?.Invoke(LogLevel.Error, $"Exception: {exception.Message}\n{exception.StackTrace}");
            }
            
            // LOGCLEAN-7: Remove Debug.WriteLine fallback
            
            // Raise events
            ConnectionStatusChanged?.Invoke(this, message);
            StatusUpdated?.Invoke(this, new StatusUpdateEventArgs(category, code, message, exception));
            
            _lastStatusUpdate = DateTime.UtcNow;
        }

        // LOGSTAT-10: Remove Obsolete Members (Old methods, properties, fields)
        // Replacing RaiseStatus with ReportStatus
        private void RaiseStatus(string status)
        {
            // Check if we're throttling updates
            if ((DateTime.UtcNow - _lastStatusUpdate).TotalMilliseconds < STATUS_UPDATE_INTERVAL_MS && 
                status == _lastRaisedStatus)
            {
                return;
            }

            ReportStatus(StatusCategory.Connection, StatusCode.Connected, status);
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
                    ReportStatus(StatusCategory.Connection, StatusCode.Connecting, "Connection not initialized, connecting first...");
                    await InitializeConnection(); // ConfigureSession is called within InitializeConnection
                }

                ReportStatus(StatusCategory.Recording, StatusCode.ProcessingStarted, "Starting audio recording...");
                bool success = await _hardwareAccess.StartRecordingAudio(OnAudioDataReceived);

                if (success)
                {
                    _isRecording = true;
                    ReportStatus(StatusCategory.Recording, StatusCode.RecordingStarted, "Recording started successfully");
                }
                else
                {
                    ReportStatus(StatusCategory.Error, StatusCode.RecordingFailed, "Failed to start recording, attempting to reinitialize");
                    await Cleanup();
                    await Task.Delay(500);
                    _isConnecting = true;
                    await InitializeConnection();
                    success = await _hardwareAccess.StartRecordingAudio(OnAudioDataReceived);
                    
                    if (success)
                    {
                        _isRecording = true;
                        ReportStatus(StatusCategory.Recording, StatusCode.RecordingStarted, "Recording started after reconnection");
                    }
                    else
                    {
                        _lastErrorMessage = "Failed to start recording after reconnection";
                        ReportStatus(StatusCategory.Error, StatusCode.RecordingFailed, "Recording failed, please reload the page");
                    }
                }
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                _isRecording = false;
                _isConnecting = false;
                ReportStatus(StatusCategory.Error, StatusCode.RecordingFailed, $"Error starting: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Stops everything - clears audio queue, stops recording, and closes the connection
        /// </summary>
        public async Task Stop()
        {
            try
            {
                _isRecording = false;
                await _hardwareAccess.StopRecordingAudio();
                await Cleanup();
                ReportStatus(StatusCategory.Recording, StatusCode.RecordingStopped, "Stopped recording and closed connection");
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                ReportStatus(StatusCategory.Error, StatusCode.GeneralError, $"Error stopping: {ex.Message}", ex);
            }
        }

        private void SetVoice(AssistantVoice voice)
        {
            try
            {
                // If websocket is open, we can update the voice right now
                if (_isInitialized && _webSocket?.State == WebSocketState.Open)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await SendAsync(new
                            {
                                type = "speech_settings",
                                voice = voice.ToString().ToLowerInvariant()
                            });
                            ReportStatus(StatusCategory.Configuration, StatusCode.VoiceChanged, $"Voice updated to: {voice}");
                        }
                        catch (Exception ex)
                        {
                            ReportStatus(StatusCategory.Error, StatusCode.ConfigurationUpdated, $"Error updating voice: {ex.Message}", ex);
                        }
                    });
                }
                else
                {
                    // We'll apply it when connecting
                    ReportStatus(StatusCategory.Configuration, StatusCode.VoiceChanged, $"Voice set to: {voice} (will be applied when connected)");
                }
            }
            catch (Exception ex)
            {
                ReportStatus(StatusCategory.Error, StatusCode.ConfigurationUpdated, $"Error setting voice: {ex.Message}", ex);
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
                        if (toolDef?.Name == null) continue;
                        
                        // Create a tool object with the correct format according to OpenAI docs
                        toolsConfig.Add(new 
                        {
                            type = "function",
                            name = toolDef.Name,
                            description = toolDef.Description,
                            parameters = toolDef.Parameters
                        });
                    }
                }

                // Construct the session object more explicitly
                var sessionPayload = new {
                    model = "gpt-4o-realtime-preview-2024-12-17",
                    voice = _settings.GetVoiceString(),
                    modalities = _settings.Modalities.ToArray(),
                    temperature = 0.8,
                    tool_choice = "auto",
                    input_audio_format = _settings.GetAudioFormatString(_settings.InputAudioFormat),
                    //input_audio_noise_reduction = "near_field", // other option is "far_field"
                    output_audio_format = _settings.GetAudioFormatString(_settings.OutputAudioFormat),
                    input_audio_transcription = new { model = "gpt-4o-transcribe" },
                    instructions = _settings.Instructions,
                    turn_detection = turnDetectionConfig, 
                    tools = toolsConfig 
                };

                Log(LogCategory.Session, LogLevel.Debug, $"Session config: {sessionPayload.ToJson()}");

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
                Log(LogCategory.WebSocket, LogLevel.Debug, $"Sending session config: {configJson}");
                
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
                Log(LogCategory.Session, LogLevel.Error, $"Error configuring session: {ex.Message}", ex);
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
                    
                    Log(LogCategory.WebSocket, LogLevel.Info, $"Connecting to OpenAI API, attempt {i + 1} of {MAX_RETRY_ATTEMPTS}...");
                    RaiseStatus($"Connecting to OpenAI API ({i + 1}/{MAX_RETRY_ATTEMPTS})...");

                    using var cts = new CancellationTokenSource(CONNECTION_TIMEOUT_MS);
                    await _webSocket.ConnectAsync(
                        new Uri($"{REALTIME_WEBSOCKET_ENDPOINT}?model=gpt-4o-realtime-preview-2024-12-17"),
                        cts.Token);

                    Log(LogCategory.WebSocket, LogLevel.Info, "Connected successfully");
                    
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
                    
                    Log(LogCategory.WebSocket, LogLevel.Error, $"WebSocket error on connect attempt {i + 1}: {wsEx.Message}, WebSocketErrorCode: {wsEx.WebSocketErrorCode}", wsEx);
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
                    
                    Log(LogCategory.WebSocket, LogLevel.Error, $"Connection timeout on attempt {i + 1}");
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
                    
                    Log(LogCategory.WebSocket, LogLevel.Error, $"Connect attempt {i + 1} failed: {ex.Message}", ex);
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
                            Log(LogCategory.WebSocket, LogLevel.Info, $"Received close message with status: {result.CloseStatus}, description: {result.CloseStatusDescription}");
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
                        Log(LogCategory.WebSocket, LogLevel.Warning, "Connection closed prematurely by server");
                        RaiseStatus("Connection closed by server, will attempt to reconnect if needed");
                        break; // Exit the loop to allow reconnection logic to run
                    }
                    else
                    {
                        Log(LogCategory.WebSocket, LogLevel.Error, $"WebSocket error: {wsEx.Message}, ErrorCode: {wsEx.WebSocketErrorCode}", wsEx);
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
                    Log(LogCategory.WebSocket, LogLevel.Info, "Receive operation canceled");
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveErrorCount++;
                    Log(LogCategory.WebSocket, LogLevel.Error, $"Receive error: {ex.Message}", ex);
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
                Log(LogCategory.WebSocket, LogLevel.Info, "WebSocket loop exited, no reconnect attempt will be made");
                
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
                    Log(LogCategory.WebSocket, LogLevel.Warning, $"Received message without a valid 'type' property: {json.Substring(0, Math.Min(100, json.Length))}...");
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

                        Log(LogCategory.WebSocket, LogLevel.Error, errorDetails);
                        RaiseStatus($"OpenAI API Error: {errorMessage}");
                        break;

                    case "rate_limits.updated":
                        Log(LogCategory.WebSocket, LogLevel.Info, $"Rate Limit Update: {json}");
                        break;

                    case "response.audio.delta":
                        var audio = root.GetProperty("delta").GetString();
                        Log(LogCategory.WebSocket, LogLevel.Info, $"Audio delta received, length: {audio?.Length ?? 0}");
                        if (!string.IsNullOrEmpty(audio))
                        {
                            try
                            {
                                Log(LogCategory.WebSocket, LogLevel.Info, "[WebSocket] Attempting to play audio...");
                                _hardwareAccess.PlayAudio(audio, 24000);
                                Log(LogCategory.WebSocket, LogLevel.Info, "[WebSocket] PlayAudio called successfully");
                            }
                            catch (Exception ex)
                            {
                                Log(LogCategory.WebSocket, LogLevel.Error, $"[WebSocket] ERROR playing audio: {ex.Message}", ex);
                                Log(LogCategory.WebSocket, LogLevel.Error, $"[WebSocket] Stack trace: {ex.StackTrace}", ex);
                            }
                        }
                        break;

                    case "response.audio_transcript.done":
                        if (root.TryGetProperty("transcript", out var spokenText))
                        {
                            Log(LogCategory.WebSocket, LogLevel.Info, $"[WebSocket] Transcript received: {spokenText}");
                        }
                        break;

                    case "response.done":
                        Log(LogCategory.WebSocket, LogLevel.Info, "[WebSocket] Full response completed");
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
                                                Log(LogCategory.WebSocket, LogLevel.Info, $"[WebSocket] Extracted text from response.done: {text}");
                                            }
                                            // Handle audio transcript
                                            else if (contentTypeStr == "audio" && content.TryGetProperty("transcript", out var transcriptElement))
                                            {
                                                string transcript = transcriptElement.GetString() ?? string.Empty;
                                                fullText.Append(transcript);
                                                Log(LogCategory.WebSocket, LogLevel.Info, $"[WebSocket] Extracted audio transcript from response.done: {transcript}");
                                            }
                                        }
                                    }
                                    
                                    string completeMessage = fullText.ToString();
                                    if (!string.IsNullOrWhiteSpace(completeMessage))
                                    {
                                        Log(LogCategory.WebSocket, LogLevel.Info, $"[WebSocket] Final extracted message from response.done: {completeMessage}");
                                        
                                        // Add to chat history if new or different from last message
                                        if (_chatHistory.Count == 0 || 
                                           _chatHistory[_chatHistory.Count - 1].Role == "user" || 
                                           _chatHistory[_chatHistory.Count - 1].Content != completeMessage)
                                        {
                                            var message = new OpenAiChatMessage(completeMessage, "assistant");
                                            _chatHistory.Add(message);
                                            MessageAdded?.Invoke(this, message);
                                            Log(LogCategory.WebSocket, LogLevel.Info, "[WebSocket] Added message to chat history via response.done");
                                        }
                                        
                                        // Clear the AI message buffer since we've got the complete message
                                        _currentAiMessage.Clear();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log(LogCategory.WebSocket, LogLevel.Error, $"[WebSocket] Error processing response.done message: {ex.Message}", ex);
                        }
                        break;

                    case "response.text.delta":
                        if (root.TryGetProperty("delta", out var deltaElem) && 
                            deltaElem.TryGetProperty("text", out var textElem))
                        {
                            string deltaText = textElem.GetString() ?? string.Empty;
                            _currentAiMessage.Append(deltaText);
                            Log(LogCategory.WebSocket, LogLevel.Info, $"[WebSocket] Text delta received: '{deltaText}'");
                        }
                        break;

                    case "response.text.done":
                        if (_currentAiMessage.Length > 0)
                        {
                            string messageText = _currentAiMessage.ToString();
                            Log(LogCategory.WebSocket, LogLevel.Info, $"[WebSocket] Text done received, message: '{messageText}'");
                            
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
                                Log(LogCategory.WebSocket, LogLevel.Info, "[WebSocket] Added message to chat history via text.done");
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
                        Log(LogCategory.WebSocket, LogLevel.Info, "[WebSocket] New conversation item started");
                        if (root.TryGetProperty("role", out var roleElem))
                        {
                            string role = roleElem.GetString() ?? string.Empty;
                            Log(LogCategory.WebSocket, LogLevel.Info, $"[WebSocket] Item role: {role}");
                            if (role == "assistant")
                            {
                                // Reset AI message for new response
                                _currentAiMessage.Clear();
                            }
                        }
                        break;

                    case "conversation.item.end":
                        Log(LogCategory.WebSocket, LogLevel.Info, "[WebSocket] Conversation item ended");
                        break;

                    case "response.output_item.done":
                        Log(LogCategory.WebSocket, LogLevel.Info, "[WebSocket] Received complete message from assistant");
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
                                Log(LogCategory.WebSocket, LogLevel.Info, $"[WebSocket] Complete message text: {messageText}");
                                
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
                            Log(LogCategory.WebSocket, LogLevel.Error, $"[WebSocket] Error processing complete message: {ex.Message}", ex);
                        }
                        break;

                    case "response.function_call_arguments.delta":
                        // Streaming mode for function call arguments - we could accumulate these if needed
                        Log(LogCategory.WebSocket, LogLevel.Info, "[WebSocket] Received function call arguments delta");
                        break;

                    case "response.function_call_arguments.done":
                        Log(LogCategory.WebSocket, LogLevel.Info, $"[WebSocket] Received complete function call arguments: {json.Substring(0, Math.Min(200, json.Length))}...");
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
                                Log(LogCategory.Tooling, LogLevel.Info, $"[Tooling] Executing tool: {tool.Name} (ID: {callId})");
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
                                    Log(LogCategory.Tooling, LogLevel.Error, $"[Tooling] Error executing tool '{tool.Name}' (ID: {callId}): {ex.Message}", ex);
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
                                Log(LogCategory.Tooling, LogLevel.Info, $"[Tooling] No tool implementation for call ID: {callId}");
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
                        Log(LogCategory.WebSocket, LogLevel.Warning, $"[WebSocket] Unhandled message type: {type} - Content: {json.Substring(0, Math.Min(100, json.Length))}...");
                        break;
                }
            }
            catch (JsonException jsonEx)
            {
                 RaiseStatus($"Error parsing JSON message: {jsonEx.Message}. JSON: {json.Substring(0, Math.Min(100, json.Length))}...");
                 Log(LogCategory.WebSocket, LogLevel.Error, $"[WebSocket] JSON Parse Error: {jsonEx.Message} for JSON: {json}", jsonEx);
            }
            catch (Exception ex)
            {
                // Catching generic Exception should be done carefully. Ensure specific exceptions are handled above.
                RaiseStatus($"Error handling message: {ex.Message}. JSON: {json.Substring(0, Math.Min(100, json.Length))}...");
                Log(LogCategory.WebSocket, LogLevel.Error, $"[WebSocket] General Error Handling Message: {ex.Message} for JSON: {json}", ex);
                Log(LogCategory.WebSocket, LogLevel.Error, $"[WebSocket] StackTrace: {ex.StackTrace}", ex);
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
                Log(LogCategory.Audio, LogLevel.Error, $"Error sending audio data: {ex.Message}", ex);
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
                    Log(LogCategory.WebSocket, LogLevel.Warning, "Cannot send message, socket not open");
                    return;
                }

                try
                {
                    // Separate serialization from send to catch and log serialization errors
                    json = JsonSerializer.Serialize(message, _jsonOptions);
                    Log(LogCategory.WebSocket, LogLevel.Debug, $"Sending message type: {message.GetType().Name}, Length: {json.Length}");
                }
                catch (JsonException jsonEx)
                {
                    Log(LogCategory.WebSocket, LogLevel.Error, $"JSON serialization error: {jsonEx.Message}", jsonEx);
                    RaiseStatus($"Error serializing message: {jsonEx.Message}");
                    throw; // Re-throw to be caught by outer catch
                }

                var bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log(LogCategory.WebSocket, LogLevel.Error, $"Error sending message: {ex.Message}\nJSON: {json}", ex);
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
                    Log(LogCategory.WebSocket, LogLevel.Warning, $"[WebSocket] Error stopping recording during cleanup: {ex.Message}", ex);
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
                                Log(LogCategory.WebSocket, LogLevel.Warning, $"[WebSocket] Error waiting for receive task to complete: {ex.Message}", ex);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(LogCategory.WebSocket, LogLevel.Warning, $"[WebSocket] Error canceling receive task: {ex.Message}", ex);
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
                                    Log(LogCategory.WebSocket, LogLevel.Warning, "[WebSocket] Close operation timed out");
                                }
                            }
                            catch (WebSocketException wsEx)
                            {
                                Log(LogCategory.WebSocket, LogLevel.Warning, $"[WebSocket] WebSocket error during close: {wsEx.Message}", wsEx);
                            }
                            catch (Exception ex)
                            {
                                Log(LogCategory.WebSocket, LogLevel.Warning, $"[WebSocket] Error closing WebSocket: {ex.Message}", ex);
                            }
                        }
                        
                        // Dispose WebSocket regardless of close success
                        _webSocket.Dispose();
                    }
                    catch (Exception ex) 
                    {
                        Log(LogCategory.WebSocket, LogLevel.Warning, $"[WebSocket] Error disposing WebSocket: {ex.Message}", ex);
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
                    Log(LogCategory.WebSocket, LogLevel.Warning, $"[WebSocket] Error disposing CTS: {ex.Message}", ex);
                }
                
                _cts = null;
                _receiveTask = null;
            }
            catch (Exception ex)
            {
                Log(LogCategory.WebSocket, LogLevel.Warning, $"[WebSocket] Error during cleanup: {ex.Message}", ex);
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
                Log(LogCategory.WebSocket, LogLevel.Error, $"Error during disposal: {ex.Message}", ex);
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
                Log(LogCategory.Audio, LogLevel.Error, $"Error during microphone test: {ex.Message}", ex);
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
                    Log(LogCategory.MicTest, LogLevel.Info, $"[MicTest] Recorded audio chunk: {e.Base64EncodedPcm16Audio.Length} chars");
                }
            }
        }
        
        private void RaiseMicTestStatus(string status)
        {
            Log(LogCategory.MicTest, LogLevel.Info, $"[MicTest] {status}");
            MicrophoneTestStatusChanged?.Invoke(this, status);
        }

        /// <summary>
        /// Finds a tool implementation based on function name or argument content.
        /// </summary>
        /// <param name="functionName">The name of the function if available</param>
        /// <param name="argumentsJson">The JSON string containing the arguments</param>
        /// <returns>The matching tool or null if not found</returns>
        private RealTimeTool? FindToolForArguments(string functionName, string argumentsJson)
        {        
            return _settings.Tools.FirstOrDefault(t => t.Name == functionName);            
        }

        /// <summary>
        /// Sends the result of a tool execution back to OpenAI using the correct message format.
        /// </summary>
        /// <param name="callId">The ID of the function call to respond to.</param>
        /// <param name="result">The result of the function execution.</param>
        public async Task SendToolResultAsync(string callId, object result)
        {
            if (_webSocket?.State != WebSocketState.Open)
            {
                RaiseStatus("Cannot send function result: WebSocket is not open.");
                Log(LogCategory.WebSocket, LogLevel.Warning, "[WebSocket] Attempted to send function result, but socket is not open.");
                return;
            }

            if (string.IsNullOrEmpty(callId))
            {
                 Log(LogCategory.WebSocket, LogLevel.Warning, "[WebSocket] Attempted to send function result with empty callId.");
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

            Log(LogCategory.WebSocket, LogLevel.Info, $"[WebSocket] Sending function result for call ID: {callId}, Result: {result.ToJson()}");
            await SendAsync(functionResultMessage);
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
                Log(LogCategory.Tooling, LogLevel.Warning, "[Tooling] Tool call ID or name is empty. Cannot handle result.");
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
                Log(LogCategory.Tooling, LogLevel.Error, $"[Tooling] Error handling tool result: {ex.Message}", ex);
                RaiseStatus($"Error handling tool result: {ex.Message}");
            }
        }

        /// <summary>
        /// Get a list of available microphone devices
        /// </summary>
        /// <returns>A list of available microphone devices</returns>
        public async Task<List<AudioDeviceInfo>> GetAvailableMicrophones()
        {
            try
            {
                // Initialize audio if not already initialized
                if (!_isInitialized)
                {
                    try
                    {
                        await _hardwareAccess.InitAudio();
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop calls cannot be issued at this time"))
                    {
                        // This happens during prerendering in Blazor
                        Log(LogCategory.OpenAiRealTimeApiAccess, LogLevel.Warning, "Microphone access not available during prerendering, returning empty list");
                        return new List<AudioDeviceInfo>();
                    }
                }

                // Get available microphones from hardware
                var microphones = await _hardwareAccess.GetAvailableMicrophones();
                
                // Raise event to notify subscribers
                MicrophoneDevicesChanged?.Invoke(this, microphones);
                
                return microphones;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop calls cannot be issued at this time"))
            {
                // This happens during prerendering in Blazor
                Log(LogCategory.OpenAiRealTimeApiAccess, LogLevel.Warning, "Microphone access not available during prerendering, returning empty list");
                return new List<AudioDeviceInfo>();
            }
            catch (Exception ex)
            {
                Log(LogCategory.OpenAiRealTimeApiAccess, LogLevel.Error, $"[OpenAiRealTimeApiAccess] Error getting microphones: {ex.Message}", ex);
                _lastErrorMessage = $"Error getting microphones: {ex.Message}";
                return new List<AudioDeviceInfo>();
            }
        }

        /// <summary>
        /// Set the microphone device to use for recording
        /// </summary>
        /// <param name="deviceId">The ID of the microphone device to use</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> SetMicrophoneDevice(string deviceId)
        {
            try
            {
                // Don't allow changing while recording
                if (_isRecording)
                {
                    Log(LogCategory.OpenAiRealTimeApiAccess, LogLevel.Warning, "[OpenAiRealTimeApiAccess] Cannot change microphone while recording");
                    return false;
                }

                // Initialize audio if not already initialized
                if (!_isInitialized)
                {
                    await _hardwareAccess.InitAudio();
                }

                // Set the microphone device
                bool success = await _hardwareAccess.SetMicrophoneDevice(deviceId);
                if (success)
                {
                    RaiseStatus($"Microphone device set successfully");
                }
                else
                {
                    _lastErrorMessage = "Failed to set microphone device";
                    RaiseStatus(_lastErrorMessage);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Log(LogCategory.OpenAiRealTimeApiAccess, LogLevel.Error, $"[OpenAiRealTimeApiAccess] Error setting microphone: {ex.Message}", ex);
                _lastErrorMessage = $"Error setting microphone: {ex.Message}";
                RaiseStatus(_lastErrorMessage);
                return false;
            }
        }

        /// <summary>
        /// Get the currently selected microphone device
        /// </summary>
        /// <returns>The ID of the currently selected microphone device, or null if none is selected</returns>
        public async Task<string?> GetCurrentMicrophoneDevice()
        {
            try
            {
                return await _hardwareAccess.GetCurrentMicrophoneDevice();
            }
            catch (Exception ex)
            {
                Log(LogCategory.OpenAiRealTimeApiAccess, LogLevel.Warning, $"[OpenAiRealTimeApiAccess] Error getting current microphone: {ex.Message}", ex);
                return null;
            }
        }
    }    
}
