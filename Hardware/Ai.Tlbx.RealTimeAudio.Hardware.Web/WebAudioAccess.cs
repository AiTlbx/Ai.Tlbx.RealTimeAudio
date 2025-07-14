// WebAudioAccess.cs
using Ai.Tlbx.RealTimeAudio.OpenAi;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;

namespace Ai.Tlbx.RealTimeAudio.Hardware.Web
{
    public class WebAudioAccess : IAudioHardwareAccess
    {
        private readonly IJSRuntime _jsRuntime;
        private IJSObjectReference? _audioModule;
        private readonly Queue<string> _audioQueue = new Queue<string>();
        private readonly object _audioLock = new object();
        private bool _isPlaying = false;
        private bool _isRecording = false;
        
        // Add these fields for recording        
        private MicrophoneAudioReceivedEventHandler? _audioDataReceivedHandler;
        
        // Store a reference to the DotNetObjectReference to prevent it from being garbage collected
        private DotNetObjectReference<WebAudioAccess>? _dotNetReference;

        // Store the selected microphone device id
        private string? _selectedMicrophoneId = null;
        
        // Event for audio errors
        public event EventHandler<string>? AudioError;

        public WebAudioAccess(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task InitAudio()
        {
            try
            {
                if (_audioModule == null)
                {
                    // Try multiple approaches to get the audio module
                    try
                    {
                        // First try to import as a module
                        _audioModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/webAudioAccess.js");
                        Debug.WriteLine("[WebAudioAccess] Successfully imported webAudioAccess.js as a module");
                    }
                    catch (Exception importEx)
                    {
                        Debug.WriteLine($"[WebAudioAccess] Module import failed: {importEx.Message}");
                        
                        try
                        {
                            // Then try accessing via global window.audioInterop
                            Debug.WriteLine("[WebAudioAccess] Trying window.audioInterop");
                            _audioModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("eval", "window.audioInterop");
                            
                            if (_audioModule == null)
                            {
                                throw new InvalidOperationException("window.audioInterop is null or undefined");
                            }
                            Debug.WriteLine("[WebAudioAccess] Successfully accessed window.audioInterop");
                        }
                        catch (Exception windowEx)
                        {
                            Debug.WriteLine($"[WebAudioAccess] window.audioInterop access failed: {windowEx.Message}");
                            throw new InvalidOperationException($"Failed to access audio module: Module import error: {importEx.Message}, Global access error: {windowEx.Message}");
                        }
                    }
                    
                    // Create the .NET reference for JS callbacks before initializing
                    if (_dotNetReference == null)
                    {
                        _dotNetReference = DotNetObjectReference.Create(this);
                    }
                    
                    // Set the DotNet reference first
                    try
                    {
                        await _audioModule.InvokeVoidAsync("setDotNetReference", _dotNetReference);
                        Debug.WriteLine("[WebAudioAccess] Successfully set DotNet reference");
                    }
                    catch (Exception setRefEx)
                    {
                        Debug.WriteLine($"[WebAudioAccess] Error setting DotNet reference: {setRefEx.Message}");
                        throw new InvalidOperationException($"Failed to set DotNet reference: {setRefEx.Message}");
                    }
                    
                    // Make sure audio permissions are properly requested and the AudioContext is activated
                    try
                    {
                        var permissionResult = await _audioModule.InvokeAsync<bool>("initAudioWithUserInteraction");
                        if (!permissionResult)
                        {
                            throw new InvalidOperationException("Failed to initialize audio system. Microphone permission might be denied.");
                        }
                        Debug.WriteLine("[WebAudioAccess] Successfully initialized audio with user interaction");
                    }
                    catch (Exception initEx)
                    {
                        Debug.WriteLine($"[WebAudioAccess] Error initializing audio with user interaction: {initEx.Message}");
                        throw new InvalidOperationException($"Failed to initialize audio: {initEx.Message}");
                    }
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop calls cannot be issued at this time"))
            {
                // This happens during prerendering in Blazor
                // Just silently fail and let the caller handle it
                throw new InvalidOperationException("JavaScript interop calls cannot be issued during prerendering");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebAudioAccess] Error initializing audio: {ex.Message}");
                await OnAudioError($"Audio initialization failed: {ex.Message}");
                throw;
            }
        }

        [JSInvokable]
        public Task OnAudioError(string errorMessage)
        {
            Debug.WriteLine($"[WebAudioAccess] Audio error from JavaScript: {errorMessage}");
            AudioError?.Invoke(this, errorMessage);
            return Task.CompletedTask;
        }

        [JSInvokable]
        public Task OnAudioDataAvailable(string base64EncodedPcm16Audio)
        {
            Debug.WriteLine($"[WebAudioAccess] OnAudioDataAvailable called, data length: {base64EncodedPcm16Audio?.Length ?? 0}");

            if (_audioDataReceivedHandler == null)
            {
                Debug.WriteLine("[WebAudioAccess] _audioDataReceivedHandler is null");
                return Task.CompletedTask;
            }

            if (string.IsNullOrEmpty(base64EncodedPcm16Audio))
            {
                Debug.WriteLine("[WebAudioAccess] Received empty audio data");
                return Task.CompletedTask;
            }

            try
            {
                _audioDataReceivedHandler.Invoke(this, new MicrophoneAudioReceivedEventArgs(base64EncodedPcm16Audio));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebAudioAccess] Error invoking _audioDataReceivedHandler: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        [JSInvokable]
        public Task OnRecordingStateChanged(bool isRecording)
        {
            Debug.WriteLine($"[WebAudioAccess] OnRecordingStateChanged: {isRecording}");
            _isRecording = isRecording;
            return Task.CompletedTask;
        }

        public bool PlayAudio(string base64EncodedPcm16Audio, int sampleRate = 24000)
        {   
            if (_audioModule == null)
            {                
                return false;
            }

            if (string.IsNullOrEmpty(base64EncodedPcm16Audio))
            {                
                return false;
            }

            // Always enqueue the audio data
            lock (_audioLock)
            {
                // Store both the audio data and sample rate
                _audioQueue.Enqueue($"{base64EncodedPcm16Audio}|{sampleRate}");                    
                Debug.WriteLine($"[WebAudioAccess] Audio chunk queued. Queue size: {_audioQueue.Count} items");
                
                // If nothing is currently playing, start the audio processing
                if (!_isPlaying)
                {
                    _isPlaying = true;
                    // Start audio processing in background without awaiting
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            Debug.WriteLine("[WebAudioAccess] Starting audio playback pipeline in background...");
                            await ProcessAudioQueue();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[WebAudioAccess] Error in background playback: {ex.Message}");
                        }
                        finally
                        {
                            lock (_audioLock)
                            {
                                _isPlaying = false;
                                Debug.WriteLine("[WebAudioAccess] Playback finished, isPlaying set to false");
                            }
                        }
                    });
                }
            }

            // Return immediately, audio will play asynchronously
            return true;
        }

        private async Task PlayAudioChunk(string base64EncodedPcm16Audio, int sampleRate = 24000)
        {
            if (_audioModule == null) return;

            try
            {
                Debug.WriteLine($"[WebAudioAccess] Sending audio chunk to browser for playback. Size: {base64EncodedPcm16Audio.Length} chars");                 
                await _audioModule.InvokeVoidAsync("playAudio", base64EncodedPcm16Audio, sampleRate);
                Debug.WriteLine("[WebAudioAccess] Audio playback started in browser");
            }
            catch (JSDisconnectedException jsEx)
            {
                // Circuit has disconnected
                Debug.WriteLine($"[WebAudioAccess] JSDisconnectedException: {jsEx.Message}");
                lock (_audioLock)
                {
                    _audioQueue.Clear();
                    _isPlaying = false;
                }
                throw; // Rethrow to inform caller
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebAudioAccess] Error in PlayAudioChunk: {ex.Message}");
                Debug.WriteLine($"[WebAudioAccess] Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        // Helper to validate base64 strings
        private bool IsValidBase64(string base64)
        {
            try
            {
                // Try to decode a small sample to validate
                if (string.IsNullOrEmpty(base64) || base64.Length < 4)
                    return false;
                    
                Convert.FromBase64String(base64);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Try to fix base64 padding
        private string FixBase64Padding(string base64)
        {
            try
            {
                // Base64 strings should have a length that is a multiple of 4
                var remainder = base64.Length % 4;
                if (remainder > 0)
                {
                    // Add padding to make length a multiple of 4
                    base64 += new string('=', 4 - remainder);
                    Debug.WriteLine("[WebAudioAccess] Fixed base64 padding");
                }
                
                // Test if it's valid now
                Convert.FromBase64String(base64);
                return base64;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebAudioAccess] Error fixing base64: {ex.Message}");
                return base64; // Return original if fix fails
            }
        }

        private async Task ProcessAudioQueue()
        {
            while (true)
            {
                string? nextChunk = null;

                lock (_audioLock)
                {
                    if (_audioQueue.Count > 0)
                    {
                        nextChunk = _audioQueue.Dequeue();
                        Debug.WriteLine($"[WebAudioAccess] Audio chunk dequeued. Remaining queue size: {_audioQueue.Count} items");
                    }
                }

                if (nextChunk == null)
                {
                    // Nothing to play right now, small delay then continue
                    await Task.Delay(20);
                    lock (_audioLock)
                    {
                        if (_audioQueue.Count == 0)
                        {
                            _isPlaying = false;
                            Debug.WriteLine("[WebAudioAccess] Playback finished, isPlaying set to false");
                            return;
                        }
                    }
                    continue;
                }

                // Parse the audio data and sample rate
                string[] parts = nextChunk.Split('|');
                string audioData = parts[0];
                int sampleRate = parts.Length > 1 && int.TryParse(parts[1], out int rate) ? rate : 24000;

                try
                {
                    await PlayAudioChunk(audioData, sampleRate);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebAudioAccess] Error processing audio chunk: {ex.Message}");
                    AudioError?.Invoke(this, $"Error playing audio: {ex.Message}");
                }
            }
        }

        public async Task<bool> StartRecordingAudio(MicrophoneAudioReceivedEventHandler audioDataReceivedHandler)
        {
            try
            {
                // Don't start if already recording
                if (_isRecording)
                {
                    Debug.WriteLine("[WebAudioAccess] Already recording, ignoring start request");
                    return true;
                }
                
                if (_audioModule == null)
                {
                    await InitAudio();
                }
                
                if (_audioModule == null)
                {
                    throw new InvalidOperationException("Audio module couldn't be initialized");
                }
                
                // Set handler
                _audioDataReceivedHandler = audioDataReceivedHandler;
                
                // Create the .NET reference if not already done
                if (_dotNetReference == null)
                {
                    _dotNetReference = DotNetObjectReference.Create(this);
                }
                
                // Start recording in JavaScript with the selected device if available
                try
                {
                    Debug.WriteLine("[WebAudioAccess] Starting recording with JS using dotNetReference");
                    Debug.WriteLine($"[WebAudioAccess] Using device ID: {_selectedMicrophoneId ?? "default"}");
                    var result = await _audioModule.InvokeAsync<bool>("startRecording", _dotNetReference, 500, _selectedMicrophoneId); // 500ms upload interval, selected device ID
                    
                    if (result)
                    {
                        Debug.WriteLine("[WebAudioAccess] Recording started successfully");
                        _isRecording = true;
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine("[WebAudioAccess] Failed to start recording from JS");
                        CleanupRecording();
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebAudioAccess] Exception while starting recording: {ex.Message}");
                    await OnAudioError($"Failed to start recording: {ex.Message}");
                    CleanupRecording();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebAudioAccess] Exception in StartRecordingAudio: {ex.Message}");
                await OnAudioError($"Microphone access failed: {ex.Message}");
                return false;
            }
        }

        [JSInvokable]
        public Task ReceiveAudioData(string base64EncodedPcm16Audio)
        {
            // Log that the method was called using console logging
            Debug.WriteLine($"[WebAudioAccess] ReceiveAudioData called, data length: {base64EncodedPcm16Audio?.Length ?? 0}");

            try
            {
                // Check if the handler is null
                if (_audioDataReceivedHandler == null)
                {
                    Debug.WriteLine("[WebAudioAccess] Error: _audioDataReceivedHandler is null");
                    return Task.CompletedTask;
                }

                // Check if the data is valid
                if (string.IsNullOrEmpty(base64EncodedPcm16Audio))
                {
                    Debug.WriteLine("[WebAudioAccess] Error: Received empty audio data");
                    return Task.CompletedTask;
                }

                // Invoke the callback with the received audio data
                _audioDataReceivedHandler.Invoke(this, new MicrophoneAudioReceivedEventArgs(base64EncodedPcm16Audio));
                Debug.WriteLine("[WebAudioAccess] Successfully invoked _audioDataReceivedHandler");
            }
            catch (Exception ex)
            {
                // Log the error with console logging
                Debug.WriteLine($"[WebAudioAccess] Error in ReceiveAudioData: {ex.Message}");
                Debug.WriteLine($"[WebAudioAccess] Stack trace: {ex.StackTrace}");
            }

            return Task.CompletedTask;
        }

        public async Task<bool> StopRecordingAudio()
        {
            if (_audioModule == null) return false;

            if (!_isRecording)
            {
                return false;
            }

            try
            {
                bool success = await _audioModule.InvokeAsync<bool>("stopRecording");
                CleanupRecording();
                return success;
            }
            catch (JSDisconnectedException)
            {
                // Handle circuit disconnection gracefully
                CleanupRecording();
                return true; // Pretend success since we can't actually verify
            }
            catch (Exception)
            {
                CleanupRecording();
                return false;
            }
        }

        private void CleanupRecording()
        {
            _isRecording = false;
            
            // Don't dispose the DotNetObjectReference here, as we might use it again
            _audioDataReceivedHandler = null;
        }

        public async Task ClearAudioQueue()
        {
            if (_audioModule == null) return;

            try
            {
                // Clear the queue first
                int queuedItems;
                lock (_audioLock)
                {
                    queuedItems = _audioQueue.Count;
                    _audioQueue.Clear();
                    Debug.WriteLine($"[WebAudioAccess] Cleared audio queue with {queuedItems} pending items");
                }

                // Stop any current audio playback
                Debug.WriteLine("[WebAudioAccess] Stopping current audio playback");
                await _audioModule.InvokeVoidAsync("stopAudioPlayback");
                Debug.WriteLine("[WebAudioAccess] Audio playback stopped");

                // Reset playing state
                lock (_audioLock)
                {
                    _isPlaying = false;
                }
            }
            catch (JSDisconnectedException)
            {
                // Handle circuit disconnection gracefully
                lock (_audioLock)
                {
                    _audioQueue.Clear();
                    _isPlaying = false;
                }
            }
            catch (Exception)
            {
                // Still clear local state even if JS fails
                lock (_audioLock)
                {
                    _audioQueue.Clear();
                    _isPlaying = false;
                }
            }
        }

        public async Task<List<AudioDeviceInfo>> GetAvailableMicrophones()
        {
            if (_audioModule == null)
            {
                await InitAudio();
            }

            if (_audioModule == null)
            {
                Debug.WriteLine("[WebAudioAccess] Cannot get available microphones: audio module is null");
                return new List<AudioDeviceInfo>();
            }

            try
            {
                var devices = await _audioModule.InvokeAsync<List<AudioDeviceInfo>>("getAvailableMicrophones");
                Debug.WriteLine($"[WebAudioAccess] Found {devices.Count} microphone devices");
                return devices;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebAudioAccess] Error getting available microphones: {ex.Message}");
                await OnAudioError($"Failed to get microphone list: {ex.Message}");
                return new List<AudioDeviceInfo>();
            }
        }

        public async Task<bool> SetMicrophoneDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                Debug.WriteLine("[WebAudioAccess] Cannot set microphone device: deviceId is null or empty");
                return false;
            }

            if (_audioModule == null)
            {
                await InitAudio();
            }

            if (_audioModule == null)
            {
                Debug.WriteLine("[WebAudioAccess] Cannot set microphone device: audio module is null");
                return false;
            }

            try
            {
                _selectedMicrophoneId = deviceId;
                Debug.WriteLine($"[WebAudioAccess] Microphone device set to: {deviceId}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebAudioAccess] Error setting microphone device: {ex.Message}");
                await OnAudioError($"Failed to set microphone device: {ex.Message}");
                return false;
            }
        }

        public async Task<string?> GetCurrentMicrophoneDevice()
        {
            await Task.CompletedTask;
            return _selectedMicrophoneId;
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            // Make sure recording is stopped
            if (_isRecording)
            {
                try 
                {
                    await StopRecordingAudio();
                }
                catch (JSDisconnectedException)
                {
                    // Circuit already disconnected, can't stop recording via JS
                    _isRecording = false;
                    _audioDataReceivedHandler = null;
                }
                catch (Exception)
                {
                    // Handle silently
                }
            }

            // Dispose the .NET reference
            if (_dotNetReference != null)
            {
                try
                {
                    _dotNetReference.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebAudioAccess] Error disposing DotNetObjectReference: {ex.Message}");
                }
                finally
                {
                    _dotNetReference = null;
                }
            }

            // Dispose the JS module
            if (_audioModule != null)
            {
                try
                {
                    await _audioModule.DisposeAsync();
                }
                catch (JSDisconnectedException)
                {
                    // Circuit already disconnected, can't dispose via JS
                }
                catch (Exception)
                {
                    // Handle silently
                }
                finally
                {
                    // Ensure the reference is cleared even if disposal fails
                    _audioModule = null;
                }
            }
        }
    }
}
