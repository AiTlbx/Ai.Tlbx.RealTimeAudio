using Ai.Tlbx.RealTimeAudio.OpenAi.Tools;
using System.Text.Json.Serialization;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Models
{
    /// <summary>
    /// Represents the definition of a function that the AI model can call.
    /// </summary>
    public abstract class OpenAiFunctionDefinition
    {
        [JsonPropertyName("name")]
        public virtual string? Name { get; set; }

        [JsonPropertyName("description")]
        public virtual string? Description { get; set; }

        [JsonPropertyName("parameters")]
        public virtual OpenAiFunctionParameters? Parameters { get; set; }

        [JsonPropertyName("strict")]
        public virtual bool? Strict { get; set; }
    }

    /// <summary>
    /// Defines the parameters for a function tool using JSON Schema.
    /// </summary>
    public class OpenAiFunctionParameters
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("properties")]
        public Dictionary<string, OpenAiParameterProperty>? Properties { get; set; }

        [JsonPropertyName("required")]
        public List<string>? Required { get; set; }
    }

    /// <summary>
    /// Describes a single parameter property.
    /// </summary>
    public class OpenAiParameterProperty
    {
        /// <summary>
        /// "string", "number", "integer", "boolean", "array", "object"
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>
        /// description string
        /// Optional
        /// A description of what the parameter represents
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
} 