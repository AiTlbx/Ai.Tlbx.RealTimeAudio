using System.Text.Json.Serialization;
using Ai.Tlbx.RealTimeAudio.OpenAi.Tools;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Models
{
    /// <summary>
    /// Comprehensive settings for the OpenAI RealTime API
    /// </summary>
    public class OpenAiRealTimeSettings
    {
        /// <summary>
        /// Voice used by the assistant
        /// </summary>
        public AssistantVoice Voice { get; set; } = AssistantVoice.Alloy;
        
        /// <summary>
        /// Settings for audio/speech detection and turn-taking
        /// </summary>
        public TurnDetectionSettings TurnDetection { get; set; } = new TurnDetectionSettings();
        
        /// <summary>
        /// Format for input audio
        /// </summary>
        public AudioFormat InputAudioFormat { get; set; } = AudioFormat.Pcm16;
        
        /// <summary>
        /// Format for output audio
        /// </summary>
        public AudioFormat OutputAudioFormat { get; set; } = AudioFormat.Pcm16;
        
        /// <summary>
        /// Settings for audio transcription
        /// </summary>
        public TranscriptionSettings Transcription { get; set; } = new TranscriptionSettings();
        
        /// <summary>
        /// Modalities to enable (audio, text)
        /// </summary>
        public List<string> Modalities { get; set; } = new List<string> { "audio", "text" };
        
        /// <summary>
        /// Instructions for the assistant
        /// </summary>
        public string Instructions { get; set; } = "You are a helpful AI assistant. Be friendly, conversational, helpful, and engaging.";
        
        /// <summary>
        /// List of tools the assistant can use.
        /// </summary>
        public List<RealTimeTool> Tools { get; set; } = new List<RealTimeTool>();

        /// <summary>
        /// The speed at which the voice speaks
        /// </summary>
        public double Speed { get; set; } = 1.0;

        /// <summary>
        /// Diagnostic logging level for audio hardware access
        /// </summary>
        public DiagnosticLevel DiagnosticLevel { get; set; } = DiagnosticLevel.Normal;

        /// <summary>
        /// Creates a default settings object
        /// </summary>
        public OpenAiRealTimeSettings()
        {
        }
        
        /// <summary>
        /// Factory method to create default settings
        /// </summary>
        public static OpenAiRealTimeSettings CreateDefault()
        {
            return new OpenAiRealTimeSettings
            {
                Voice = AssistantVoice.Alloy,
                TurnDetection = new TurnDetectionSettings
                {
                    Type = TurnDetectionType.SemanticVad,
                    Eagerness = VadEagerness.Medium,
                    CreateResponse = true,
                    InterruptResponse = true
                },
                InputAudioFormat = AudioFormat.Pcm16,
                OutputAudioFormat = AudioFormat.Pcm16,
                Transcription = new TranscriptionSettings
                {
                    Model = TranscriptionModel.Whisper1
                },
                Modalities = new List<string> { "audio", "text" },
                Instructions = "You are a helpful AI assistant. Be friendly, conversational, helpful, and engaging. When the user speaks interrupt your answer and listen and then answer the new question.",
                DiagnosticLevel = DiagnosticLevel.Normal
            };
        }
        
        /// <summary>
        /// Factory method to create settings optimized for fast responses
        /// </summary>
        public static OpenAiRealTimeSettings CreateFastResponseSettings()
        {
            var settings = CreateDefault();
            settings.TurnDetection.Type = TurnDetectionType.SemanticVad;
            settings.TurnDetection.Eagerness = VadEagerness.High;
            return settings;
        }
        
        /// <summary>
        /// Factory method to create settings optimized for patient listening
        /// </summary>
        public static OpenAiRealTimeSettings CreatePatientListeningSettings()
        {
            var settings = CreateDefault();
            settings.TurnDetection.Type = TurnDetectionType.SemanticVad;
            settings.TurnDetection.Eagerness = VadEagerness.Low;
            return settings;
        }
        
        /// <summary>
        /// Helper to convert voice enum to string
        /// </summary>
        public string GetVoiceString()
        {
            var attr = GetJsonPropertyName(Voice);
            return attr ?? Voice.ToString().ToLower();
        }
        
        /// <summary>
        /// Helper to convert audio format enum to string
        /// </summary>
        public string GetAudioFormatString(AudioFormat format)
        {
            var attr = GetJsonPropertyName(format);
            return attr ?? format.ToString().ToLower();
        }
        
        /// <summary>
        /// Helper to convert transcription model enum to string
        /// </summary>
        public string GetTranscriptionModelString()
        {
            var attr = GetJsonPropertyName(Transcription.Model);
            return attr ?? Transcription.Model.ToString().ToLower().Replace("_", "-");
        }
        
        /// <summary>
        /// Get the JsonPropertyName attribute value for an enum
        /// </summary>
        private string? GetJsonPropertyName<T>(T enumValue) where T : Enum
        {
            var enumType = typeof(T);
            var memberInfo = enumType.GetMember(enumValue.ToString());
            
            if (memberInfo.Length > 0)
            {
                var attributes = memberInfo[0].GetCustomAttributes(typeof(JsonPropertyNameAttribute), false);
                if (attributes.Length > 0)
                {
                    return ((JsonPropertyNameAttribute)attributes[0]).Name;
                }
            }
            
            return null;
        }
    }
} 