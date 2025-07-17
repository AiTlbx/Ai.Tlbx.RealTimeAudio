using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Ai.Tlbx.RealTimeAudio.OpenAi.Helper;
using Ai.Tlbx.RealTimeAudio.OpenAi.Models;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Configures the OpenAI real-time API session settings.
    /// </summary>
    internal sealed class SessionConfigurator
    {
        private const string MODEL_GPT4O_REALTIME = "gpt-4o-realtime-preview-2025-06-03";
        private const string MODEL_TRANSCRIBE = "gpt-4o-transcribe";

        private readonly ICustomLogger _logger;
        private readonly JsonSerializerOptions _camelCaseJsonOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionConfigurator"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        public SessionConfigurator(ICustomLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _camelCaseJsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <summary>
        /// Builds the session configuration payload for the OpenAI API.
        /// </summary>
        /// <param name="settings">The real-time settings to configure.</param>
        /// <returns>The session configuration object.</returns>
        public object BuildSessionConfiguration(OpenAiRealTimeSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            try
            {
                object? turnDetectionConfig = BuildTurnDetectionConfig(settings);
                List<object>? toolsConfig = BuildToolsConfig(settings);

                var sessionPayload = new
                {
                    model = MODEL_GPT4O_REALTIME,
                    voice = settings.GetVoiceString(),
                    modalities = settings.Modalities.ToArray(),
                    temperature = 0.8,
                    speed = settings.Speed,
                    tool_choice = "auto",
                    input_audio_format = settings.GetAudioFormatString(settings.InputAudioFormat),
                    input_audio_noise_reduction = new
                    {
                        type = "near_field"
                    },
                    output_audio_format = settings.GetAudioFormatString(settings.OutputAudioFormat),
                    input_audio_transcription = new { model = MODEL_TRANSCRIBE },
                    instructions = settings.Instructions,
                    turn_detection = turnDetectionConfig,
                    tools = toolsConfig
                };

                _logger.Log(LogLevel.Info, $"Session config: {sessionPayload.ToJson()}");

                return new
                {
                    type = "session.update",
                    session = sessionPayload
                };
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error building session configuration: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Serializes the session configuration to JSON.
        /// </summary>
        /// <param name="sessionConfig">The session configuration object.</param>
        /// <returns>The JSON string representation of the configuration.</returns>
        public string SerializeConfiguration(object sessionConfig)
        {
            if (sessionConfig == null)
                throw new ArgumentNullException(nameof(sessionConfig));

            try
            {
                string configJson = MessageSerializer.SerializeCamelCase(sessionConfig);
                _logger.Log(LogLevel.Info, $"Serialized session config: {configJson}");
                return configJson;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error serializing session configuration: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Gets a description of the session configuration for logging.
        /// </summary>
        /// <param name="settings">The real-time settings.</param>
        /// <returns>A descriptive string about the session configuration.</returns>
        public string GetConfigurationDescription(OpenAiRealTimeSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            string turnTypeDesc = settings.TurnDetection.Type switch
            {
                TurnDetectionType.SemanticVad => "semantic VAD",
                TurnDetectionType.ServerVad => "server VAD",
                TurnDetectionType.None => "no turn detection",
                _ => "unknown turn detection"
            };

            var toolsConfig = BuildToolsConfig(settings);
            string toolsDesc = (toolsConfig != null && toolsConfig.Count > 0) ? $" with {toolsConfig.Count} tool(s)" : "";

            return $"Session configured with voice: {settings.GetVoiceString()}, {turnTypeDesc}{toolsDesc}";
        }

        private object? BuildTurnDetectionConfig(OpenAiRealTimeSettings settings)
        {
            // If turn detection is disabled, return null
            if (settings.TurnDetection.Type == TurnDetectionType.None)
            {
                return null;
            }

            // For semantic VAD
            if (settings.TurnDetection.Type == TurnDetectionType.SemanticVad)
            {
                return new
                {
                    type = GetJsonPropertyName(settings.TurnDetection.Type) ?? "semantic_vad",
                    eagerness = GetJsonPropertyName(settings.TurnDetection.Eagerness) ?? "auto",
                    create_response = settings.TurnDetection.CreateResponse,
                    interrupt_response = settings.TurnDetection.InterruptResponse
                };
            }

            // For server VAD
            return new
            {
                type = GetJsonPropertyName(settings.TurnDetection.Type) ?? "server_vad",
                threshold = settings.TurnDetection.Threshold ?? 0.5f,
                prefix_padding_ms = settings.TurnDetection.PrefixPaddingMs ?? 300,
                silence_duration_ms = settings.TurnDetection.SilenceDurationMs ?? 500
            };
        }

        private List<object>? BuildToolsConfig(OpenAiRealTimeSettings settings)
        {
            if (settings.Tools == null || settings.Tools.Count == 0)
                return null;

            var toolsConfig = new List<object>();
            foreach (var toolDef in settings.Tools)
            {
                if (toolDef?.Name == null) continue;

                toolsConfig.Add(new
                {
                    type = "function",
                    name = toolDef.Name,
                    description = toolDef.Description,
                    parameters = toolDef.Parameters
                });
            }

            return toolsConfig.Count > 0 ? toolsConfig : null;
        }

        /// <summary>
        /// Gets the JsonPropertyName attribute value for an enum.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="enumValue">The enum value.</param>
        /// <returns>The JSON property name or null if not found.</returns>
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