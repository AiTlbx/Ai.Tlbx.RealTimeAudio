using System.Runtime.InteropServices;

namespace Ai.Tlbx.RealTimeAudio.Hardware.Linux
{
    /// <summary>
    /// P/Invoke definitions for ALSA (Advanced Linux Sound Architecture)
    /// </summary>
    internal static class AlsaNative
    {
        public const string ALSA_LIBRARY = "libasound.so.2";

        // PCM format definitions
        public enum SndPcmFormat
        {
            SND_PCM_FORMAT_S16_LE = 2,  // Signed 16-bit little-endian
            SND_PCM_FORMAT_FLOAT_LE = 6 // 32-bit float little-endian
        }

        // PCM stream types
        public enum SndPcmStreamType
        {
            SND_PCM_STREAM_PLAYBACK = 0,
            SND_PCM_STREAM_CAPTURE = 1
        }

        // ALSA PCM Access Types
        public enum SndPcmAccessType
        {
            SND_PCM_ACCESS_RW_INTERLEAVED = 3
        }

        // PCM open modes
        public const int SND_PCM_NONBLOCK = 1;
        public const int SND_PCM_ASYNC = 2;

        // PCM state
        public enum SndPcmState
        {
            SND_PCM_STATE_OPEN = 0,
            SND_PCM_STATE_SETUP = 1,
            SND_PCM_STATE_PREPARED = 2,
            SND_PCM_STATE_RUNNING = 3,
            SND_PCM_STATE_XRUN = 4,
            SND_PCM_STATE_DRAINING = 5,
            SND_PCM_STATE_PAUSED = 6,
            SND_PCM_STATE_SUSPENDED = 7,
            SND_PCM_STATE_DISCONNECTED = 8
        }

        // Error codes
        public const int EAGAIN = 11;
        public const int EBADFD = 77;
        public const int EPIPE = 32;
        public const int ESTRPIPE = 86;

        // PCM device functions
        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_open(out IntPtr pcm, string name, SndPcmStreamType stream, int mode);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_close(IntPtr pcm);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_prepare(IntPtr pcm);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_resume(IntPtr pcm);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_start(IntPtr pcm);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_drop(IntPtr pcm);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_drain(IntPtr pcm);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern SndPcmState snd_pcm_state(IntPtr pcm);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);

        // PCM hardware parameters functions
        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_hw_params_malloc(out IntPtr @params);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_hw_params_any(IntPtr pcm, IntPtr @params);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_hw_params_set_access(IntPtr pcm, IntPtr @params, SndPcmAccessType access);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_hw_params_set_format(IntPtr pcm, IntPtr @params, SndPcmFormat format);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_hw_params_set_rate(IntPtr pcm, IntPtr @params, uint val, int dir);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_hw_params_set_channels(IntPtr pcm, IntPtr @params, uint val);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_hw_params_set_buffer_size(IntPtr pcm, IntPtr @params, ulong val);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_hw_params_set_period_size(IntPtr pcm, IntPtr @params, ulong val, int dir);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_hw_params(IntPtr pcm, IntPtr @params);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern void snd_pcm_hw_params_free(IntPtr @params);

        // PCM I/O functions
        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern long snd_pcm_readi(IntPtr pcm, byte[] buffer, ulong size);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern long snd_pcm_writei(IntPtr pcm, byte[] buffer, ulong size);

        // PCM info functions
        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_info_malloc(out IntPtr info);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern void snd_pcm_info_free(IntPtr info);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_pcm_info(IntPtr pcm, IntPtr info);

        // Error handling
        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr snd_strerror(int errnum);

        // Device name handling
        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_device_name_hint(int card, string iface, out IntPtr hints);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr snd_device_name_get_hint(IntPtr hint, string id);

        [DllImport(ALSA_LIBRARY, CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_device_name_free_hint(IntPtr hints);

        // Helper method to get a string from an error code
        public static string GetAlsaErrorMessage(int errorCode)
        {
            IntPtr strPtr = snd_strerror(errorCode);
            return Marshal.PtrToStringAnsi(strPtr) ?? $"Unknown error ({errorCode})";
        }
    }
} 