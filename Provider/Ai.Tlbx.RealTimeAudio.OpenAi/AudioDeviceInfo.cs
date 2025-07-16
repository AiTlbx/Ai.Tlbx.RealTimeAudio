using System;

namespace Ai.Tlbx.RealTimeAudio.OpenAi
{
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