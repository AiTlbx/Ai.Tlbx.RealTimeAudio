using Microsoft.JSInterop;

namespace Ai.Tlbx.RealTime.WebUi
{
    public class AudioInteropService : IAudioInteropService
    {
        private readonly IJSRuntime _js;
        public AudioInteropService(IJSRuntime js)
        {
            _js = js;
        }
        public async Task PlayAudioAsync(byte[] pcmData, int sampleRate)
        {
            await _js.InvokeVoidAsync("audioInterop.playAudio", pcmData, sampleRate);
        }
        public async Task StopAudioAsync()
        {
            await _js.InvokeVoidAsync("audioInterop.stopAudio");
        }
        public async Task<string[]> GetMicrophonesAsync()
        {
            return await _js.InvokeAsync<string[]>("audioInterop.getMicrophones");
        }
        public async Task StartRecordingAsync(string deviceId)
        {
            await _js.InvokeVoidAsync("audioInterop.startRecording", deviceId);
        }
        public async Task StopRecordingAsync()
        {
            await _js.InvokeVoidAsync("audioInterop.stopRecording");
        }
    }
} 