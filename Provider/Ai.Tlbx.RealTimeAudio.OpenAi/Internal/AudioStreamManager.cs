using Ai.Tlbx.RealTimeAudio.OpenAi.Events;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Manages audio streaming operations for recording and playback.
    /// </summary>
    internal sealed class AudioStreamManager
    {
        private readonly IAudioHardwareAccess _hardwareAccess;
        private readonly ICustomLogger _logger;
        private readonly StructuredLogger _structuredLogger;
        private readonly Func<object, Task> _sendMessageAsync;
        private bool _isRecording = false;
        private bool _disposed = false;

        /// <summary>
        /// Gets a value indicating whether audio recording is currently active.
        /// </summary>
        public bool IsRecording => _isRecording;

        /// <summary>
        /// Event that fires when the recording status changes.
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioStreamManager"/> class.
        /// </summary>
        /// <param name="hardwareAccess">The audio hardware access instance.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="sendMessageAsync">Function to send messages to the API.</param>
        public AudioStreamManager(
            IAudioHardwareAccess hardwareAccess,
            ICustomLogger logger,
            Func<object, Task> sendMessageAsync)
        {
            _hardwareAccess = hardwareAccess ?? throw new ArgumentNullException(nameof(hardwareAccess));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _structuredLogger = new StructuredLogger(logger, "AudioStreamManager");
            _sendMessageAsync = sendMessageAsync ?? throw new ArgumentNullException(nameof(sendMessageAsync));
        }

        /// <summary>
        /// Starts recording audio from the microphone.
        /// </summary>
        /// <returns>A task that resolves to true if recording started successfully, false otherwise.</returns>
        public async Task<bool> StartRecordingAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioStreamManager));

            if (_isRecording)
            {
                _logger.Log(LogLevel.Info, "Recording is already active");
                return true;
            }

            try
            {
                StatusChanged?.Invoke(this, "Starting audio recording...");
                bool success = await _hardwareAccess.StartRecordingAudio(OnAudioDataReceived);

                if (success)
                {
                    _isRecording = true;
                    StatusChanged?.Invoke(this, "Recording started successfully");
                    _logger.Log(LogLevel.Info, "Audio recording started successfully");
                }
                else
                {
                    StatusChanged?.Invoke(this, "Failed to start audio recording. Check microphone permissions and device availability.");
                    _logger.Log(LogLevel.Error, "Failed to start audio recording");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error starting audio recording: {ex.Message}", ex);
                StatusChanged?.Invoke(this, $"Error starting recording: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stops recording audio from the microphone.
        /// </summary>
        /// <returns>A task representing the stop operation.</returns>
        public async Task StopRecordingAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioStreamManager));

            if (!_isRecording)
            {
                _logger.Log(LogLevel.Info, "Recording is not active, cannot stop");
                return;
            }

            try
            {
                _isRecording = false;
                StatusChanged?.Invoke(this, "Stopping recording...");

                await _hardwareAccess.StopRecordingAudio();
                await _hardwareAccess.ClearAudioQueue();

                StatusChanged?.Invoke(this, "Recording stopped");
                _logger.Log(LogLevel.Info, "Audio recording stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error stopping recording: {ex.Message}", ex);
                StatusChanged?.Invoke(this, $"Error stopping recording: {ex.Message}");
            }
        }

        /// <summary>
        /// Plays audio data through the hardware.
        /// </summary>
        /// <param name="base64EncodedPcm16Audio">The base64-encoded PCM 16-bit audio data.</param>
        /// <param name="sampleRate">The sample rate of the audio.</param>
        /// <returns>A task representing the playback operation.</returns>
        public Task PlayAudioAsync(string base64EncodedPcm16Audio, int sampleRate = 24000)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioStreamManager));

            if (string.IsNullOrEmpty(base64EncodedPcm16Audio))
            {
                _logger.Log(LogLevel.Warn, "Cannot play empty audio data");
                return Task.CompletedTask;
            }

            try
            {
                _logger.Log(LogLevel.Info, $"Playing audio data, length: {base64EncodedPcm16Audio.Length}");
                _hardwareAccess.PlayAudio(base64EncodedPcm16Audio, sampleRate);
                _logger.Log(LogLevel.Info, "Audio playback started successfully");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error playing audio: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Clears the audio queue and stops any ongoing playback.
        /// </summary>
        /// <returns>A task representing the clear operation.</returns>
        public async Task ClearAudioQueueAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioStreamManager));

            try
            {
                await _hardwareAccess.ClearAudioQueue();
                _logger.Log(LogLevel.Info, "Audio queue cleared");
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error clearing audio queue: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Initializes the audio hardware.
        /// </summary>
        /// <returns>A task representing the initialization operation.</returns>
        public async Task InitializeAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioStreamManager));

            try
            {
                StatusChanged?.Invoke(this, "Initializing audio system...");
                await _hardwareAccess.InitAudio();
                StatusChanged?.Invoke(this, "Audio system initialized");
                _logger.Log(LogLevel.Info, "Audio hardware initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error initializing audio hardware: {ex.Message}", ex);
                StatusChanged?.Invoke(this, $"Error initializing audio: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Starts a microphone test session.
        /// </summary>
        /// <returns>A task that resolves to true if the test started successfully, false otherwise.</returns>
        public async Task<bool> StartMicrophoneTestAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioStreamManager));

            try
            {
                StatusChanged?.Invoke(this, "Starting microphone test...");
                bool success = await _hardwareAccess.StartRecordingAudio(OnMicTestAudioReceived);
                
                if (success)
                {
                    StatusChanged?.Invoke(this, "Microphone test started");
                    _logger.Log(LogLevel.Info, "Microphone test started successfully");
                }
                else
                {
                    StatusChanged?.Invoke(this, "Failed to start microphone test");
                    _logger.Log(LogLevel.Error, "Failed to start microphone test");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error starting microphone test: {ex.Message}", ex);
                StatusChanged?.Invoke(this, $"Error starting microphone test: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stops the microphone test session.
        /// </summary>
        /// <returns>A task representing the stop operation.</returns>
        public async Task StopMicrophoneTestAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioStreamManager));

            try
            {
                await _hardwareAccess.StopRecordingAudio();
                StatusChanged?.Invoke(this, "Microphone test stopped");
                _logger.Log(LogLevel.Info, "Microphone test stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error stopping microphone test: {ex.Message}", ex);
                StatusChanged?.Invoke(this, $"Error stopping microphone test: {ex.Message}");
            }
        }

        private async void OnAudioDataReceived(object sender, MicrophoneAudioReceivedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(e.Base64EncodedPcm16Audio))
                {
                    _logger.Log(LogLevel.Warn, "Received empty audio data");
                    return;
                }

                await _sendMessageAsync(new
                {
                    type = "input_audio_buffer.append",
                    audio = e.Base64EncodedPcm16Audio
                });
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error sending audio data: {ex.Message}", ex);
                StatusChanged?.Invoke(this, $"Error sending audio data: {ex.Message}");
            }
        }

        private async void OnMicTestAudioReceived(object sender, MicrophoneAudioReceivedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(e.Base64EncodedPcm16Audio))
                {
                    _logger.Log(LogLevel.Info, $"Microphone test: received audio data, length: {e.Base64EncodedPcm16Audio.Length}");
                    StatusChanged?.Invoke(this, $"Microphone test: receiving audio data (length: {e.Base64EncodedPcm16Audio.Length})");
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error in microphone test: {ex.Message}", ex);
                StatusChanged?.Invoke(this, $"Microphone test error: {ex.Message}");
            }
        }

        /// <summary>
        /// Releases all resources used by the AudioStreamManager.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            
            try
            {
                if (_isRecording)
                {
                    _hardwareAccess.StopRecordingAudio().Wait(1000);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "Error during dispose", ex);
            }
        }
    }
}