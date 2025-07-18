using System.Text.Json.Serialization;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Models
{
    /// <summary>
    /// Defines the parameters for a function tool using JSON Schema.
    /// </summary>
    public class OpenAiFunctionParameters
    {
        /// <summary>
        /// Gets or sets the type of the parameter schema (typically "object").
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>
        /// Gets or sets the properties of the parameter schema.
        /// </summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, OpenAiParameterProperty>? Properties { get; set; }

        /// <summary>
        /// Gets or sets the list of required parameter names.
        /// </summary>
        [JsonPropertyName("required")]
        public List<string>? Required { get; set; }
    }
}