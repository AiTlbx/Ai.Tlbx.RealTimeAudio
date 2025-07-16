using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Ai.Tlbx.RealTimeAudio.OpenAi;
using Ai.Tlbx.RealTimeAudio.OpenAi.Events;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;
using NAudio.Wave;
using System.Collections.Generic;
using System.Linq;

namespace Ai.Tlbx.RealTimeAudio.Hardware.Windows
{
    public class WindowsAudioHardware : IAudioHardwareAccess
    {
        private WaveInEvent? _waveIn;
        private bool _isRecording;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly int _sampleRate;
        private readonly int _channelCount;
        private readonly int _bitsPerSample;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _bufferedWaveProvider;
        private MicrophoneAudioReceivedEventHandler? _audioDataReceivedHandler;
        private bool _isInitialized = false;
        private int _selectedDeviceNumber = 0; // Default to first device
        private DiagnosticLevel _diagnosticLevel = DiagnosticLevel.Normal;

        public event EventHandler<string>? AudioError;

        public WindowsAudioHardware(int sampleRate = 24000, int channelCount = 1, int bitsPerSample = 16)
        {
            _sampleRate = sampleRate;
            _channelCount = channelCount;
            _bitsPerSample = bitsPerSample;
            _isRecording = false;
        }

        public Task InitAudio()
        {
            if (_isInitialized)
            {
                return Task.CompletedTask;
            }

            try
            {
                Debug.WriteLine("Initializing Windows audio hardware...");

                int deviceCount = WaveInEvent.DeviceCount;
                if (deviceCount == 0)
                {
                    string error = "No audio input devices detected";
                    Debug.WriteLine(error);
                    AudioError?.Invoke(this, error);
                    return Task.CompletedTask;
                }

                Debug.WriteLine($"Found {deviceCount} input devices:");
                for (int i = 0; i < deviceCount; i++)
                {
                    var capabilities = WaveInEvent.GetCapabilities(i);
                    Debug.WriteLine($"Device {i}: {capabilities.ProductName}");
                }

                _waveOut = new WaveOutEvent();
                _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(_sampleRate, _bitsPerSample, _channelCount))
                {
                    DiscardOnBufferOverflow = true
                };
                _waveOut.Init(_bufferedWaveProvider);

                _isInitialized = true;
                Debug.WriteLine("Windows audio hardware initialized successfully");
            }
            catch (Exception ex)
            {
                string error = $"Error initializing audio: {ex.Message}";
                Debug.WriteLine($"{error}\nStackTrace: {ex.StackTrace}");
                AudioError?.Invoke(this, error);
            }
            return Task.CompletedTask;
        }

        public async Task<bool> StartRecordingAudio(MicrophoneAudioReceivedEventHandler audioDataReceivedHandler)
        {
            if (_isRecording)
            {
                await Task.CompletedTask;
                return true;
            }

            try
            {
                if (!_isInitialized)
                {
                    await InitAudio();
                    if (!_isInitialized)
                    {
                        return false;
                    }
                }

                Debug.WriteLine("Starting audio recording with parameters:");
                Debug.WriteLine($"  Sample rate: {_sampleRate}");
                Debug.WriteLine($"  Channel count: {_channelCount}");
                Debug.WriteLine($"  Bits per sample: {_bitsPerSample}");
                Debug.WriteLine($"  Device number: {_selectedDeviceNumber}");

                _audioDataReceivedHandler = audioDataReceivedHandler;
                _cancellationTokenSource = new CancellationTokenSource();

                _waveIn = new WaveInEvent
                {
                    DeviceNumber = _selectedDeviceNumber,
                    WaveFormat = new WaveFormat(_sampleRate, _bitsPerSample, _channelCount),
                    BufferMilliseconds = 50
                };

                _waveIn.RecordingStopped += (s, e) =>
                {
                    if (e?.Exception != null)
                    {
                        Debug.WriteLine($"Recording stopped with error: {e.Exception.Message}");
                        AudioError?.Invoke(this, $"Recording error: {e.Exception.Message}");
                    }
                };

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.StartRecording();
                _isRecording = true;

                Debug.WriteLine("Recording started successfully");
                return true;
            }
            catch (Exception ex)
            {
                string error = $"Error starting recording: {ex.Message}";
                Debug.WriteLine($"{error}\nStackTrace: {ex.StackTrace}");
                AudioError?.Invoke(this, error);
                return false;
            }
        }

        public Task<bool> StopRecordingAudio()
        {
            if (!_isRecording)
            {
                return Task.FromResult(true);
            }

            try
            {
                Debug.WriteLine("Stopping audio recording...");

                _waveIn?.StopRecording();
                if (_waveIn != null)
                {
                    _waveIn.DataAvailable -= OnDataAvailable;
                    _waveIn.Dispose();
                    _waveIn = null;
                }

                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                _isRecording = false;
                _audioDataReceivedHandler = null;

                Debug.WriteLine("Recording stopped successfully");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                string error = $"Error stopping recording: {ex.Message}";
                Debug.WriteLine($"{error}\nStackTrace: {ex.StackTrace}");
                AudioError?.Invoke(this, error);
                return Task.FromResult(false);
            }
        }

        public bool PlayAudio(string base64EncodedPcm16Audio, int sampleRate)
        {
            try
            {
                if (string.IsNullOrEmpty(base64EncodedPcm16Audio))
                {
                    Debug.WriteLine("Warning: Attempted to play empty audio data");
                    return false;
                }

                byte[] audioData = Convert.FromBase64String(base64EncodedPcm16Audio);
                if (_bufferedWaveProvider != null)
                {
                    _bufferedWaveProvider.AddSamples(audioData, 0, audioData.Length);
                }

                if (_waveOut?.PlaybackState != PlaybackState.Playing)
                {
                    _waveOut?.Play();
                }

                return true;
            }
            catch (Exception ex)
            {
                string error = $"Error playing audio: {ex.Message}";
                Debug.WriteLine($"{error}\nStackTrace: {ex.StackTrace}");
                AudioError?.Invoke(this, error);
                return false;
            }
        }

        public async Task ClearAudioQueue()
        {
            try
            {
                Debug.WriteLine("Clearing audio buffer...");                
                int bufferSizeBeforeClear = _bufferedWaveProvider?.BufferedBytes ?? 0;
                _bufferedWaveProvider?.ClearBuffer();
                _waveOut?.Stop();
                Debug.WriteLine($"Audio buffer cleared. Bytes before clear: {bufferSizeBeforeClear}");
            }
            catch (Exception ex)
            {
                string error = $"Error clearing audio buffer: {ex.Message}";
                Debug.WriteLine($"{error}\nStackTrace: {ex.StackTrace}");
                AudioError?.Invoke(this, error);
            }
            await Task.CompletedTask;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs? e)
        {
            try
            {
                if (e?.BytesRecorded > 0 && _audioDataReceivedHandler != null)
                {
                    var buffer = new byte[e.BytesRecorded];
                    if (e.Buffer != null)
                    {
                        Array.Copy(e.Buffer, buffer, e.BytesRecorded);
                    }

                    string base64Audio = Convert.ToBase64String(buffer);
                    Debug.WriteLine($"Audio data recorded: {e.BytesRecorded} bytes, base64 length: {base64Audio.Length}");

                    _audioDataReceivedHandler?.Invoke(this, new MicrophoneAudioReceivedEventArgs(base64Audio));
                }
            }
            catch (Exception ex)
            {
                string error = $"Error processing audio data: {ex.Message}";
                Debug.WriteLine($"{error}\nStackTrace: {ex.StackTrace}");
                AudioError?.Invoke(this, error);
            }
        }

        public async Task<List<AudioDeviceInfo>> GetAvailableMicrophones()
        {
            var result = new List<AudioDeviceInfo>();

            try
            {
                if (!_isInitialized)
                {
                    await InitAudio();
                }

                int deviceCount = WaveInEvent.DeviceCount;
                Debug.WriteLine($"Getting available microphones. Found {deviceCount} devices.");

                for (int i = 0; i < deviceCount; i++)
                {
                    var capabilities = WaveInEvent.GetCapabilities(i);
                    result.Add(new AudioDeviceInfo
                    {
                        Id = i.ToString(),
                        Name = capabilities.ProductName,
                        IsDefault = i == 0 // Assume first device is default
                    });
                    Debug.WriteLine($"Device {i}: {capabilities.ProductName}");
                }
            }
            catch (Exception ex)
            {
                string error = $"Error getting available microphones: {ex.Message}";
                Debug.WriteLine($"{error}\nStackTrace: {ex.StackTrace}");
                AudioError?.Invoke(this, error);
            }
            await Task.CompletedTask;
            return result;
        }

        public async Task<bool> SetMicrophoneDevice(string deviceId)
        {
            try
            {
                if (!_isInitialized)
                {
                    await InitAudio();
                }

                if (int.TryParse(deviceId, out int deviceNumber))
                {
                    if (deviceNumber >= 0 && deviceNumber < WaveInEvent.DeviceCount)
                    {
                        _selectedDeviceNumber = deviceNumber;
                        Debug.WriteLine($"Microphone device set to: {deviceNumber}");
                        return true;
                    }
                    else
                    {
                        string error = $"Invalid device number: {deviceNumber}. Must be between 0 and {WaveInEvent.DeviceCount - 1}";
                        Debug.WriteLine(error);
                        AudioError?.Invoke(this, error);
                    }
                }
                else
                {
                    string error = $"Invalid device ID format: {deviceId}. Must be a number.";
                    Debug.WriteLine(error);
                    AudioError?.Invoke(this, error);
                }
            }
            catch (Exception ex)
            {
                string error = $"Error setting microphone device: {ex.Message}";
                Debug.WriteLine($"{error}\nStackTrace: {ex.StackTrace}");
                AudioError?.Invoke(this, error);
            }
            await Task.CompletedTask;
            return false;
        }

        public Task<string?> GetCurrentMicrophoneDevice()
        {
            return Task.FromResult<string?>(_selectedDeviceNumber.ToString());
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                Debug.WriteLine("Disposing Windows audio hardware...");
                await StopRecordingAudio();

                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                    Debug.WriteLine("Wave out player disposed");
                }

                _bufferedWaveProvider = null;
                _isInitialized = false;
                Debug.WriteLine("Windows audio hardware disposed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during disposal: {ex}");
            }
        }

        public void StartAsync()
        {
            try
            {
                _waveIn?.StartRecording();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting audio client: {ex.Message}");
                AudioError?.Invoke(this, $"Error starting audio: {ex.Message}");
            }
        }

        public void StopAsync()
        {
            try
            {
                _waveIn?.StopRecording();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping audio client: {ex.Message}");
                AudioError?.Invoke(this, $"Error stopping audio: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the diagnostic logging level for the Windows audio device.
        /// </summary>
        /// <param name="level">The diagnostic level to set.</param>
        /// <returns>A task that resolves to true if the level was set successfully, false otherwise.</returns>
        public async Task<bool> SetDiagnosticLevel(DiagnosticLevel level)
        {
            await Task.CompletedTask;
            _diagnosticLevel = level;
            LogDiagnostic($"Diagnostic level set to: {level}");
            return true;
        }

        /// <summary>
        /// Gets the current diagnostic logging level.
        /// </summary>
        /// <returns>The current diagnostic level.</returns>
        public async Task<DiagnosticLevel> GetDiagnosticLevel()
        {
            await Task.CompletedTask;
            return _diagnosticLevel;
        }

        /// <summary>
        /// Logs a diagnostic message respecting the diagnostic level setting.
        /// </summary>
        private void LogDiagnostic(string message)
        {
            if (_diagnosticLevel == DiagnosticLevel.Off) return;
            
            // Windows implementation uses Debug.WriteLine for all diagnostics
            Debug.WriteLine($"[WindowsAudioHardware] {message}");
        }
    }
}