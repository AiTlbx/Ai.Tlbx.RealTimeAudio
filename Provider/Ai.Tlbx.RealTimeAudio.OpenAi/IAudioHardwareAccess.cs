using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ai.Tlbx.RealTimeAudio.OpenAi.Events;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;

namespace Ai.Tlbx.RealTimeAudio.OpenAi
{
    /// <summary>
    /// Interface for accessing audio hardware capabilities for recording and playback.
    /// Provides methods to initialize, start/stop recording, and play audio.
    /// </summary>
    public interface IAudioHardwareAccess : IAsyncDisposable
    {
        /// <summary>
        /// Event that fires when an audio error occurs in the hardware
        /// </summary>
        event EventHandler<string>? AudioError;

        /// <summary>
        /// Initializes the audio hardware and prepares it for recording and playback.
        /// </summary>
        /// <returns>A task representing the asynchronous initialization operation.</returns>
        Task InitAudio();

        /// <summary>
        /// Starts recording audio from the microphone and sets up the handler for received audio data.
        /// </summary>
        /// <param name="audioDataReceivedHandler">The event handler that will be called when audio data is received from the microphone.</param>
        /// <returns>A task that resolves to true if recording started successfully, false otherwise.</returns>
        Task<bool> StartRecordingAudio(MicrophoneAudioReceivedEventHandler audioDataReceivedHandler);
        
        /// <summary>
        /// Plays the provided audio through the system's audio output.
        /// </summary>
        /// <param name="base64EncodedPcm16Audio">The audio data encoded as a base64 string in PCM 16-bit format.</param>
        /// <param name="sampleRate">The sample rate of the audio in Hz.</param>
        /// <returns>True if playback started successfully, false otherwise.</returns>
        bool PlayAudio(string base64EncodedPcm16Audio, int sampleRate);
        
        /// <summary>
        /// Stops the current audio recording session.
        /// </summary>
        /// <returns>A task that resolves to true if recording was successfully stopped, false otherwise.</returns>
        Task<bool> StopRecordingAudio();

        /// <summary>
        /// Clears any pending audio in the queue and stops the current playback immediately.
        /// Used when the user interrupts the AI's response to ensure no buffered audio continues playing.
        /// </summary>
        Task ClearAudioQueue();

        /// <summary>
        /// Gets a list of available microphone devices.
        /// </summary>
        /// <returns>A list of audio device information objects representing available microphones.</returns>
        Task<List<AudioDeviceInfo>> GetAvailableMicrophones();

        /// <summary>
        /// Requests microphone permission from the user and gets a list of available microphone devices with labels.
        /// This method will explicitly request microphone permission and activate the microphone temporarily to get device labels.
        /// </summary>
        /// <returns>A list of audio device information objects representing available microphones with proper labels.</returns>
        Task<List<AudioDeviceInfo>> RequestMicrophonePermissionAndGetDevices();

        /// <summary>
        /// Sets the microphone device to use for recording.
        /// </summary>
        /// <param name="deviceId">The ID of the microphone device to use.</param>
        /// <returns>True if the device was set successfully, false otherwise.</returns>
        Task<bool> SetMicrophoneDevice(string deviceId);

        /// <summary>
        /// Gets the ID of the currently selected microphone device.
        /// </summary>
        /// <returns>The ID of the currently selected microphone device, or null if none is selected.</returns>
        Task<string?> GetCurrentMicrophoneDevice();

        /// <summary>
        /// Sets the diagnostic logging level for the audio hardware implementation.
        /// </summary>
        /// <param name="level">The diagnostic level to set.</param>
        /// <returns>A task that resolves to true if the level was set successfully, false otherwise.</returns>
        Task<bool> SetDiagnosticLevel(DiagnosticLevel level);

        /// <summary>
        /// Gets the current diagnostic logging level.
        /// </summary>
        /// <returns>The current diagnostic level.</returns>
        Task<DiagnosticLevel> GetDiagnosticLevel();

        /// <summary>
        /// Sets the logging action for this hardware component.
        /// </summary>
        /// <param name="logAction">Action to be called with log level and message.</param>
        void SetLogAction(Action<LogLevel, string> logAction);
    }
}
