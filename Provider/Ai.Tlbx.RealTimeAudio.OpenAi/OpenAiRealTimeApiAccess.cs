using System.Text;
using Ai.Tlbx.RealTimeAudio.OpenAi.Events;
using Ai.Tlbx.RealTimeAudio.OpenAi.Internal;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;
using Ai.Tlbx.RealTimeAudio.OpenAi.Tools;

namespace Ai.Tlbx.RealTimeAudio.OpenAi
{
    /// <summary>
    /// Provides access to OpenAI's real-time API for audio processing and conversation.
    /// </summary>
    public sealed class OpenAiRealTimeApiAccess : IAsyncDisposable
    {
        private readonly IAudioHardwareAccess _hardwareAccess;
        private readonly string _apiKey;
        private readonly Action<LogLevel, string> _logAction;
        private readonly ICustomLogger _logger;
        private readonly StructuredLogger _structuredLogger;
        
        // Internal components
        private readonly WebSocketConnection _webSocketConnection;
        private readonly ChatHistoryManager _chatHistory;
        private readonly AudioStreamManager _audioManager;
        private readonly SessionConfigurator _sessionConfigurator;
        private MessageProcessor? _messageProcessor;
        
        // State management
        private bool _isInitialized = false;
        private bool _isConnecting = false;
        private bool _isDisposed = false;
        private bool _isMicrophoneTesting = false;
        private string? _lastErrorMessage = null;
        private string _connectionStatus = string.Empty;
        private OpenAiRealTimeSettings _settings = new OpenAiRealTimeSettings();
        private DateTime _sessionStartTime = DateTime.MinValue;
        
        // Events
        /// <summary>
        /// Event that fires when the connection status changes.
        /// </summary>
        public event EventHandler<string>? ConnectionStatusChanged;
        
        /// <summary>
        /// Event that fires when a new message is added to the chat history.
        /// </summary>
        public event EventHandler<OpenAiChatMessage>? MessageAdded;
        
        
        /// <summary>
        /// Event that fires when a tool result is available.
        /// </summary>
        public event EventHandler<(string ToolName, string Result)>? ToolResultAvailable;
        
        /// <summary>
        /// Event that fires when a tool call is requested.
        /// </summary>
        public event EventHandler<ToolCallEventArgs>? ToolCallRequested;
        
        /// <summary>
        /// Event that fires when the list of microphone devices changes.
        /// </summary>
        public event EventHandler<List<AudioDeviceInfo>>? MicrophoneDevicesChanged;
        
        /// <summary>
        /// Event that fires when the status is updated.
        /// </summary>
        public event EventHandler<StatusUpdateEventArgs>? StatusUpdated;

        // Public properties
        /// <summary>
        /// Gets a value indicating whether the API is initialized.
        /// </summary>
        public bool IsInitialized => _isInitialized;
        
        /// <summary>
        /// Gets a value indicating whether audio recording is active.
        /// </summary>
        public bool IsRecording => _audioManager.IsRecording;
        
        /// <summary>
        /// Gets a value indicating whether the API is currently connecting.
        /// </summary>
        public bool IsConnecting => _isConnecting;
        
        /// <summary>
        /// Gets a value indicating whether microphone testing is active.
        /// </summary>
        public bool IsMicrophoneTesting => _isMicrophoneTesting;
        
        /// <summary>
        /// Gets the last error message, if any.
        /// </summary>
        public string? LastErrorMessage => _lastErrorMessage;
        
        /// <summary>
        /// Gets the current connection status.
        /// </summary>
        public string ConnectionStatus => _connectionStatus;
        
        /// <summary>
        /// Gets the chat history as a read-only list.
        /// </summary>
        public IReadOnlyList<OpenAiChatMessage> ChatHistory => _chatHistory.GetMessages();
        
        /// <summary>
        /// Gets the current settings.
        /// </summary>
        public OpenAiRealTimeSettings Settings => _settings;
        
        /// <summary>
        /// Gets or sets the current voice setting.
        /// </summary>
        public AssistantVoice CurrentVoice
        {
            get => _settings.Voice;
            set => _settings.Voice = value;
        }

        /// <summary>
        /// Gets the turn detection settings (obsolete, use Settings.TurnDetection instead).
        /// </summary>
        [Obsolete("Use Settings.TurnDetection instead. This property will be removed in a future version.")]
        public TurnDetectionSettings TurnDetectionSettings => _settings.TurnDetection;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenAiRealTimeApiAccess"/> class.
        /// </summary>
        /// <param name="hardwareAccess">The audio hardware access implementation.</param>
        public OpenAiRealTimeApiAccess(IAudioHardwareAccess hardwareAccess)
            : this(hardwareAccess, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenAiRealTimeApiAccess"/> class.
        /// </summary>
        /// <param name="hardwareAccess">The audio hardware access implementation.</param>
        /// <param name="logAction">Optional logging action for compatibility.</param>
        public OpenAiRealTimeApiAccess(IAudioHardwareAccess hardwareAccess, Action<LogLevel, string>? logAction)
        {
            _hardwareAccess = hardwareAccess ?? throw new ArgumentNullException(nameof(hardwareAccess));
            
            // Get API key from environment
            _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
                ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");
            
            // Set up logging - no-op if no log action provided (user choice to not log)
            _logAction = logAction ?? ((level, message) => { /* no-op */ });
            
            // Create logger
            _logger = CreateLogger(logAction);
            _structuredLogger = new StructuredLogger(_logger, "OpenAiRealTimeApiAccess");
            
            // Generate session ID for correlation
            var sessionId = Guid.NewGuid().ToString("N")[..8];
            _structuredLogger.SetSessionId(sessionId);
            
            // Set up hardware logging
            _hardwareAccess.SetLogAction(_logAction);
            
            // Initialize internal components
            _webSocketConnection = new WebSocketConnection(_apiKey, _logger);
            _chatHistory = new ChatHistoryManager();
            _audioManager = new AudioStreamManager(_hardwareAccess, _logger, SendMessageAsync);
            _sessionConfigurator = new SessionConfigurator(_logger);
            
            // Wire up events
            WireUpEvents();
        }

        /// <summary>
        /// Starts the full lifecycle - initializes the connection if needed and starts recording audio.
        /// </summary>
        /// <returns>A task representing the start operation.</returns>
        public async Task Start()
        {
            await Start(null);
        }

        /// <summary>
        /// Starts the full lifecycle - initializes the connection if needed and starts recording audio.
        /// </summary>
        /// <param name="settings">Optional settings to configure the API behavior.</param>
        /// <returns>A task representing the start operation.</returns>
        public async Task Start(OpenAiRealTimeSettings? settings)
        {
            await Start(settings, CancellationToken.None);
        }

        /// <summary>
        /// Starts the full lifecycle - initializes the connection if needed and starts recording audio.
        /// </summary>
        /// <param name="settings">Optional settings to configure the API behavior.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>A task representing the start operation.</returns>
        public async Task Start(OpenAiRealTimeSettings? settings, CancellationToken cancellationToken)
        {
            try
            {
                _lastErrorMessage = null;
                
                if (settings != null)
                {
                    _settings = settings;
                }
                
                if (!_isInitialized || !_webSocketConnection.IsConnected)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _structuredLogger.LogStateTransition("NotInitialized", "Connecting", "Connection not initialized");
                    _isConnecting = true;
                    ReportStatus(StatusCategory.Connection, StatusCode.Connecting, "Connection not initialized, connecting first...");
                    await InitializeConnectionAsync();
                }
                
                cancellationToken.ThrowIfCancellationRequested();
                bool recordingStarted = await _audioManager.StartRecordingAsync();
                
                if (!recordingStarted)
                {
                    ReportStatus(StatusCategory.Error, StatusCode.RecordingFailed, "Failed to start audio recording. Check microphone permissions and device availability.");
                    throw new InvalidOperationException("Failed to start recording");
                }
                
                // Track session start time
                _sessionStartTime = DateTime.UtcNow;
                _structuredLogger.Log(LogLevel.Info, "Voice session started successfully", 
                    data: new { StartTime = _sessionStartTime, Voice = _settings.Voice });
            }
            catch (Exception ex)
            {
                _lastErrorMessage = ex.Message;
                _isConnecting = false;
                ReportStatus(StatusCategory.Error, StatusCode.RecordingFailed, $"Error starting: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Stops everything - clears audio queue, stops recording, and closes the connection.
        /// </summary>
        /// <returns>A task representing the stop operation.</returns>
        public async Task Stop()
        {
            if (!_audioManager.IsRecording)
            {
                ReportStatus(StatusCategory.Recording, StatusCode.RecordingStopped, "Not recording, cannot stop");
                return;
            }

            try
            {
                ReportStatus(StatusCategory.Recording, StatusCode.RecordingStopped, "Stopping recording...");
                
                await _audioManager.StopRecordingAsync();
                await _audioManager.ClearAudioQueueAsync();
                
                // Send session close message
                await SendMessageAsync(new { type = "session.close" });
                
                // Close the websocket connection
                await _webSocketConnection.DisconnectAsync();
                
                _isInitialized = false;
                
                // Log session statistics
                if (_sessionStartTime != DateTime.MinValue)
                {
                    var sessionDuration = DateTime.UtcNow - _sessionStartTime;
                    _structuredLogger.Log(LogLevel.Info, "Voice session ended", 
                        data: new { 
                            Duration = sessionDuration.ToString(@"mm\:ss\.fff"), 
                            TotalMessages = _chatHistory.GetMessages().Count,
                            EndTime = DateTime.UtcNow 
                        });
                }
                
                ReportStatus(StatusCategory.Recording, StatusCode.RecordingStopped, "Recording stopped and websocket closed");
            }
            catch (Exception ex)
            {
                ReportStatus(StatusCategory.Error, StatusCode.GeneralError, $"Error stopping recording: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Interrupts the current conversation and clears the audio queue.
        /// </summary>
        /// <returns>A task representing the interrupt operation.</returns>
        public async Task Interrupt()
        {
            try
            {
                ReportStatus(StatusCategory.Recording, StatusCode.Interrupting, "Interrupting conversation...");
                
                await SendMessageAsync(new { type = "response.cancel" });
                await _audioManager.ClearAudioQueueAsync();
                
                ReportStatus(StatusCategory.Recording, StatusCode.Interrupted, "Conversation interrupted");
            }
            catch (Exception ex)
            {
                ReportStatus(StatusCategory.Error, StatusCode.GeneralError, $"Error interrupting: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Tests the microphone by starting a brief recording session.
        /// </summary>
        /// <returns>A task that resolves to true if the test was successful, false otherwise.</returns>
        public async Task<bool> TestMicrophone()
        {
            if (_isMicrophoneTesting)
            {
                ReportStatus(StatusCategory.Recording, StatusCode.MicrophoneTestStarted, "Microphone test already in progress");
                return false;
            }

            try
            {
                _isMicrophoneTesting = true;
                await _audioManager.InitializeAsync();
                
                bool success = await _audioManager.StartMicrophoneTestAsync();
                
                if (success)
                {
                    ReportStatus(StatusCategory.Recording, StatusCode.MicrophoneTestStarted, "Microphone test started successfully");
                    
                    // Let it run for a few seconds
                    await Task.Delay(3000);
                    
                    await _audioManager.StopMicrophoneTestAsync();
                    ReportStatus(StatusCategory.Recording, StatusCode.MicrophoneTestCompleted, "Microphone test completed successfully");
                }
                else
                {
                    ReportStatus(StatusCategory.Error, StatusCode.MicrophoneTestFailed, "Failed to start microphone test");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                ReportStatus(StatusCategory.Error, StatusCode.MicrophoneTestFailed, $"Microphone test error: {ex.Message}", ex);
                return false;
            }
            finally
            {
                _isMicrophoneTesting = false;
            }
        }

        /// <summary>
        /// Gets the list of available microphone devices.
        /// </summary>
        /// <returns>A task that resolves to a list of available microphone devices.</returns>
        public async Task<List<AudioDeviceInfo>> GetAvailableMicrophones()
        {
            try
            {
                var devices = await _hardwareAccess.GetAvailableMicrophones();
                MicrophoneDevicesChanged?.Invoke(this, devices);
                return devices;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error getting available microphones: {ex.Message}", ex);
                return new List<AudioDeviceInfo>();
            }
        }

        /// <summary>
        /// Requests microphone permission from the user and gets a list of available microphone devices with labels.
        /// This method will explicitly request microphone permission and activate the microphone temporarily to get device labels.
        /// </summary>
        /// <returns>A task that resolves to a list of available microphone devices with proper labels.</returns>
        public async Task<List<AudioDeviceInfo>> RequestMicrophonePermissionAndGetDevices()
        {
            try
            {
                var devices = await _hardwareAccess.RequestMicrophonePermissionAndGetDevices();
                MicrophoneDevicesChanged?.Invoke(this, devices);
                return devices;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error requesting microphone permission and getting devices: {ex.Message}", ex);
                return new List<AudioDeviceInfo>();
            }
        }

        /// <summary>
        /// Sets the microphone device to use for recording.
        /// </summary>
        /// <param name="deviceId">The ID of the microphone device to use.</param>
        /// <returns>A task that resolves to true if the device was set successfully, false otherwise.</returns>
        public async Task<bool> SetMicrophoneDevice(string deviceId)
        {
            try
            {
                bool success = await _hardwareAccess.SetMicrophoneDevice(deviceId);
                if (success)
                {
                    ReportStatus(StatusCategory.Recording, StatusCode.MicrophoneChanged, $"Microphone device set to: {deviceId}");
                }
                else
                {
                    ReportStatus(StatusCategory.Error, StatusCode.AudioError, $"Failed to set microphone device: {deviceId}");
                }
                return success;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error setting microphone device: {ex.Message}", ex);
                ReportStatus(StatusCategory.Error, StatusCode.AudioError, $"Error setting microphone device: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Clears the chat history.
        /// </summary>
        public void ClearChatHistory()
        {
            _chatHistory.ClearHistory();
            ReportStatus(StatusCategory.Configuration, StatusCode.ConfigurationUpdated, "Chat history cleared");
        }

        /// <summary>
        /// Sets the diagnostic logging level for the audio hardware access.
        /// </summary>
        /// <param name="level">The diagnostic level to set.</param>
        /// <returns>A task that resolves to true if the level was set successfully, false otherwise.</returns>
        public async Task<bool> SetDiagnosticLevel(DiagnosticLevel level)
        {
            try
            {
                _settings.DiagnosticLevel = level;
                bool result = await _hardwareAccess.SetDiagnosticLevel(level);
                if (result)
                {
                    ReportStatus(StatusCategory.Configuration, StatusCode.ConfigurationUpdated, $"Diagnostic level set to: {level}");
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error setting diagnostic level: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the current diagnostic logging level.
        /// </summary>
        /// <returns>The current diagnostic level.</returns>
        public async Task<DiagnosticLevel> GetDiagnosticLevel()
        {
            try
            {
                return await _hardwareAccess.GetDiagnosticLevel();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error getting diagnostic level: {ex.Message}", ex);
                return _settings.DiagnosticLevel; // Fallback to settings value
            }
        }

        private async Task InitializeConnectionAsync()
        {
            _lastErrorMessage = null;
            
            try
            {
                ReportStatus(StatusCategory.Configuration, StatusCode.Connecting, "Initializing audio system...");
                await _audioManager.InitializeAsync();

                // Set diagnostic level on hardware access
                await _hardwareAccess.SetDiagnosticLevel(_settings.DiagnosticLevel);

                ReportStatus(StatusCategory.Connection, StatusCode.Connecting, "Connecting to OpenAI API...");
                await _webSocketConnection.ConnectAsync();

                ReportStatus(StatusCategory.Configuration, StatusCode.Connecting, "Configuring session...");
                await ConfigureSessionAsync();

                // Create message processor after connection is established
                CreateMessageProcessor();

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

        private async Task ConfigureSessionAsync()
        {
            try
            {
                var sessionConfig = _sessionConfigurator.BuildSessionConfiguration(_settings);
                string configJson = _sessionConfigurator.SerializeConfiguration(sessionConfig);
                
                var bytes = Encoding.UTF8.GetBytes(configJson);
                await _webSocketConnection.SendMessageAsync(configJson);
                
                string description = _sessionConfigurator.GetConfigurationDescription(_settings);
                ReportStatus(StatusCategory.Connection, StatusCode.Connected, description);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error configuring session: {ex.Message}", ex);
                ReportStatus(StatusCategory.Connection, StatusCode.Connected, $"Error configuring session: {ex.Message}");
                throw;
            }
        }

        private async Task SendMessageAsync(object message)
        {
            try
            {
                if (!_webSocketConnection.IsConnected)
                {
                    _logger.Log(LogLevel.Warn, "Cannot send message, socket not open");
                    return;
                }

                string json = MessageSerializer.Serialize(message);
                await _webSocketConnection.SendMessageAsync(json);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error sending message: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Logs a message with the specified log level.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        private void Log(LogLevel level, string message)
        {
            _logAction(level, $"[OpenAiRealTimeApiAccess] {message}");
        }

        /// <summary>
        /// Logs a message using the centralized logging system.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        public void LogMessage(LogLevel level, string message)
        {
            _logAction(level, message);
        }

        private void WireUpEvents()
        {
            // WebSocket events
            _webSocketConnection.MessageReceived += OnWebSocketMessageReceived;
            _webSocketConnection.ConnectionStatusChanged += OnWebSocketStatusChanged;
            
            // Audio manager events
            _audioManager.StatusChanged += OnAudioManagerStatusChanged;
        }

        private async void OnWebSocketMessageReceived(object? sender, string message)
        {
            if (_messageProcessor != null)
            {
                await _messageProcessor.ProcessMessageAsync(message);
            }
        }

        private void OnWebSocketStatusChanged(object? sender, string status)
        {
            _connectionStatus = status;
            ConnectionStatusChanged?.Invoke(this, status);
        }

        private void OnAudioManagerStatusChanged(object? sender, string status)
        {
            _connectionStatus = status;
            ConnectionStatusChanged?.Invoke(this, status);
        }

        private void ReportStatus(StatusCategory category, StatusCode code, string message, Exception? exception = null)
        {
            _lastErrorMessage = category == StatusCategory.Error ? message : _lastErrorMessage;
            _connectionStatus = message;
            
            // Map category to log level
            LogLevel logLevel = category switch
            {
                StatusCategory.Error => LogLevel.Error,
                StatusCategory.Connection => LogLevel.Info,
                StatusCategory.Recording => LogLevel.Info,
                StatusCategory.Processing => LogLevel.Info,
                StatusCategory.Configuration => LogLevel.Info,
                StatusCategory.Tool => LogLevel.Info,
                _ => LogLevel.Info
            };
            
            _logger.Log(logLevel, $"[{category}] {code}: {message}");
            
            if (exception != null && category == StatusCategory.Error)
            {
                _logger.Log(LogLevel.Error, $"Exception: {exception.Message}", exception);
            }
            
            ConnectionStatusChanged?.Invoke(this, message);
            StatusUpdated?.Invoke(this, new StatusUpdateEventArgs(category, code, message, exception));
        }

        private ICustomLogger CreateLogger(Action<LogLevel, string>? logAction)
        {
            // Always use ActionCustomLogger with the centralized log action
            // This ensures all logging goes through the same centralized path
            return new ActionCustomLogger(_logAction);
        }

        /// <summary>
        /// Releases all resources used by the OpenAiRealTimeApiAccess.
        /// </summary>
        /// <returns>A task representing the disposal operation.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            try
            {
                await _audioManager.StopRecordingAsync();
                await _webSocketConnection.DisconnectAsync();
                _audioManager.Dispose();
                _webSocketConnection.Dispose();
                await _hardwareAccess.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "Error during dispose", ex);
            }
        }

        // Create message processor after connection is established
        private void CreateMessageProcessor()
        {
            _messageProcessor = new MessageProcessor(
                _logger,
                _hardwareAccess,
                _chatHistory,
                _settings.Tools ?? new List<RealTimeTool>(),
                SendMessageAsync);
            
            // Wire up message processor events
            _messageProcessor.MessageAdded += (sender, message) => MessageAdded?.Invoke(this, message);
            _messageProcessor.ToolCallRequested += (sender, args) => ToolCallRequested?.Invoke(this, args);
            _messageProcessor.ToolResultAvailable += (sender, result) => ToolResultAvailable?.Invoke(this, result);
            _messageProcessor.StatusChanged += (sender, status) => {
                _connectionStatus = status;
                ConnectionStatusChanged?.Invoke(this, status);
            };
        }
    }
}