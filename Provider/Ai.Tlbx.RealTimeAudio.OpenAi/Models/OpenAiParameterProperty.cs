using System.Text.Json.Serialization;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Models
{
    /// <summary>
    /// Describes a single parameter property for a function tool.
    /// </summary>
    public class OpenAiParameterProperty
    {
        /// <summary>
        /// Gets or sets the type of the parameter property.
        /// Valid values include "string", "number", "integer", "boolean", "array", "object".
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>
        /// Gets or sets the description of what the parameter represents.
        /// This is optional and provides context for the AI model.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}