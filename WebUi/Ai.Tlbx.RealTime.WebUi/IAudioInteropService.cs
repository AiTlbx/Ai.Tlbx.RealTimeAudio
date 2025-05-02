using System.Threading.Tasks;

namespace Ai.Tlbx.RealTime.WebUi
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