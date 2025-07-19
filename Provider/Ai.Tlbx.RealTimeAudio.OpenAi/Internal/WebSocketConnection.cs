using System.Net.WebSockets;
using System.Text;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Manages the WebSocket connection to OpenAI's real-time API.
    /// </summary>
    internal sealed class WebSocketConnection : IDisposable
    {
        private const string REALTIME_WEBSOCKET_ENDPOINT = "wss://api.openai.com/v1/realtime";
        private const string MODEL_GPT4O_REALTIME = "gpt-4o-realtime-preview-2025-06-03";
        private const int CONNECTION_TIMEOUT_MS = 10000;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int AUDIO_BUFFER_SIZE = 32384;

        private readonly ICustomLogger _logger;
        private readonly string _apiKey;
        private ClientWebSocket? _webSocket;
        private Task? _receiveTask;
        private CancellationTokenSource? _cts;
        private bool _disposed = false;

        /// <summary>
        /// Gets a value indicating whether the WebSocket connection is open.
        /// </summary>
        public bool IsConnected => _webSocket?.State == WebSocketState.Open;

        /// <summary>
        /// Event that fires when a message is received from the WebSocket.
        /// </summary>
        public event EventHandler<string>? MessageReceived;

        /// <summary>
        /// Callback that fires when the connection status changes.
        /// </summary>
        public Action<string>? OnConnectionStatusChanged { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WebSocketConnection"/> class.
        /// </summary>
        /// <param name="apiKey">The OpenAI API key.</param>
        /// <param name="logger">The logger instance.</param>
        public WebSocketConnection(string apiKey, ICustomLogger logger)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Connects to the OpenAI real-time API WebSocket endpoint.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the connection operation.</returns>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WebSocketConnection));

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
                    
                    _logger.Log(LogLevel.Info, $"Connecting to OpenAI API, attempt {i + 1} of {MAX_RETRY_ATTEMPTS}...");
                    OnConnectionStatusChanged?.Invoke($"Connecting to OpenAI API ({i + 1}/{MAX_RETRY_ATTEMPTS})...");

                    using var cts = new CancellationTokenSource(CONNECTION_TIMEOUT_MS);
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                    
                    await _webSocket.ConnectAsync(
                        new Uri($"{REALTIME_WEBSOCKET_ENDPOINT}?model={MODEL_GPT4O_REALTIME}"),
                        combinedCts.Token);

                    _logger.Log(LogLevel.Info, "Connected successfully");
                    
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
                    
                    _logger.Log(LogLevel.Error, $"WebSocket error on connect attempt {i + 1}: {wsEx.Message}, WebSocketErrorCode: {wsEx.WebSocketErrorCode}", wsEx);
                    OnConnectionStatusChanged?.Invoke($"Connection error: {wsEx.Message}");
                    
                    if (i < MAX_RETRY_ATTEMPTS - 1) 
                    {
                        await Task.Delay(delayMs, cancellationToken);
                        delayMs = Math.Min(delayMs * 2, 10000); // Exponential backoff, max 10 seconds
                    }
                }
                catch (TaskCanceledException)
                {
                    // Connection timeout
                    _webSocket?.Dispose();
                    _webSocket = null;
                    
                    _logger.Log(LogLevel.Error, $"Connection timeout on attempt {i + 1}");
                    OnConnectionStatusChanged?.Invoke($"Connection timeout");
                    
                    if (i < MAX_RETRY_ATTEMPTS - 1) 
                    {
                        await Task.Delay(delayMs, cancellationToken);
                        delayMs = Math.Min(delayMs * 2, 10000);
                    }
                }
                catch (Exception ex)
                {
                    // Handle other exceptions
                    _webSocket?.Dispose();
                    _webSocket = null;
                    
                    _logger.Log(LogLevel.Error, $"Connect attempt {i + 1} failed: {ex.Message}", ex);
                    OnConnectionStatusChanged?.Invoke($"Connection error: {ex.Message}");
                    
                    if (i < MAX_RETRY_ATTEMPTS - 1) 
                    {
                        await Task.Delay(delayMs, cancellationToken);
                        delayMs = Math.Min(delayMs * 2, 10000);
                    }
                }
            }
            
            throw new InvalidOperationException("Connection failed after maximum retry attempts");
        }

        /// <summary>
        /// Sends a message to the WebSocket connection.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the send operation.</returns>
        public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WebSocketConnection));

            if (_webSocket?.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not connected");

            var bytes = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes), 
                WebSocketMessageType.Text, 
                true, 
                cancellationToken);
        }

        /// <summary>
        /// Disconnects from the WebSocket connection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task representing the disconnect operation.</returns>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return;

            try
            {
                _cts?.Cancel();
                
                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "Error during disconnect", ex);
            }
            finally
            {
                _webSocket?.Dispose();
                _webSocket = null;
                
                // Only dispose task if it's in a completion state
                if (_receiveTask != null && _receiveTask.IsCompleted)
                {
                    _receiveTask.Dispose();
                }
                _receiveTask = null;
                
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task ReceiveAsync(CancellationToken ct)
        {
            var buffer = AudioBufferPool.Rent(AUDIO_BUFFER_SIZE);
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
                            _logger.Log(LogLevel.Info, $"Received close message with status: {result.CloseStatus}, description: {result.CloseStatusDescription}");
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage && _webSocket?.State == WebSocketState.Open);

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    MessageReceived?.Invoke(this, json);
                    
                    // Reset error counter on successful message
                    consecutiveErrorCount = 0;
                }
                catch (WebSocketException wsEx)
                {
                    consecutiveErrorCount++;
                    
                    // Log but don't treat as critical if it's a normal closure
                    if (wsEx.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        _logger.Log(LogLevel.Warn, "Connection closed prematurely by server");
                        OnConnectionStatusChanged?.Invoke("Connection closed by server, will attempt to reconnect if needed");
                        break; // Exit the loop to allow reconnection logic to run
                    }
                    else
                    {
                        _logger.Log(LogLevel.Error, $"WebSocket error: {wsEx.Message}, ErrorCode: {wsEx.WebSocketErrorCode}", wsEx);
                        OnConnectionStatusChanged?.Invoke($"WebSocket error: {wsEx.Message}");
                        
                        if (consecutiveErrorCount > 3)
                        {
                            OnConnectionStatusChanged?.Invoke("Too many consecutive WebSocket errors, reconnecting...");
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.Log(LogLevel.Info, "Receive operation canceled");
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveErrorCount++;
                    _logger.Log(LogLevel.Error, $"Receive error: {ex.Message}", ex);
                    OnConnectionStatusChanged?.Invoke($"Receive error: {ex.Message}");
                    
                    if (consecutiveErrorCount > 3)
                    {
                        OnConnectionStatusChanged?.Invoke("Too many consecutive receive errors, reconnecting...");
                        break;
                    }
                    
                    // Add a small delay before trying again to avoid hammering the server
                    await Task.Delay(500, CancellationToken.None);
                }
            }
            
            // If we exited the loop and the connection is still active, log it
            if (!ct.IsCancellationRequested && _webSocket != null)
            {
                _logger.Log(LogLevel.Info, "WebSocket loop exited, no reconnect attempt will be made");
            }
            
            // Return buffer to pool
            AudioBufferPool.Return(buffer, true);
        }

        /// <summary>
        /// Releases all resources used by the WebSocket connection.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                
                // Only dispose task if it's in a completion state
                if (_receiveTask != null && _receiveTask.IsCompleted)
                {
                    _receiveTask.Dispose();
                }
                
                _webSocket?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "Error during dispose", ex);
            }
        }
    }
}