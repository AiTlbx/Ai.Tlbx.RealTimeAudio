using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }

    /// <summary>
    /// Delegate for handling microphone audio data received events.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The event arguments containing the received audio data.</param>
    public delegate void MicrophoneAudioReceivedEventHandler(object sender, MicrophoneAudioReceivedEvenArgs e);

    /// <summary>
    /// Represents information about an audio device.
    /// </summary>
    public class AudioDeviceInfo
    {
        /// <summary>
        /// Gets or sets the unique identifier for the audio device.
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the display name of the audio device.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets a value indicating whether this device is the system default.
        /// </summary>
        public bool IsDefault { get; set; }
    }
}
