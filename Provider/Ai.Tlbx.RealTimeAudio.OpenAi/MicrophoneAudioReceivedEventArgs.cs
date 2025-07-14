namespace Ai.Tlbx.RealTimeAudio.OpenAi
{
    public class MicrophoneAudioReceivedEventArgs : EventArgs
    {
        public string Base64EncodedPcm16Audio { get; set; }

        public MicrophoneAudioReceivedEventArgs(string base64EncodedPcm16Audio)
        {
            Base64EncodedPcm16Audio = base64EncodedPcm16Audio;

        }
    }
}
