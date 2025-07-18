namespace Ai.Tlbx.RealTimeAudio.OpenAi.Events
{
    /// <summary>
    /// Delegate for handling microphone audio data received events.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The event arguments containing the received audio data.</param>
    public delegate void MicrophoneAudioReceivedEventHandler(object sender, MicrophoneAudioReceivedEventArgs e);
}