namespace Ai.Tlbx.VoiceAssistant.WebUi
{
    public interface IAudioInteropService
    {
        Task PlayAudioAsync(byte[] pcmData, int sampleRate);
        Task StopAudioAsync();
        Task<string[]> GetMicrophonesAsync();
        Task StartRecordingAsync(string deviceId);
        Task StopRecordingAsync();
    }
} 