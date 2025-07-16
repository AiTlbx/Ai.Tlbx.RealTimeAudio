using System.Text.Json.Serialization;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Models
{
    /// <summary>
    /// Represents the definition of a function that the AI model can call.
    /// This is the base class for all function definitions used in real-time tools.
    /// </summary>
    public abstract class OpenAiFunctionDefinition
    {
        /// <summary>
        /// Gets or sets the name of the function.
        /// </summary>
        [JsonPropertyName("name")]
        public virtual string? Name { get; set; }

        /// <summary>
        /// Gets or sets the description of what the function does.
        /// </summary>
        [JsonPropertyName("description")]
        public virtual string? Description { get; set; }

        /// <summary>
        /// Gets or sets the parameters schema for the function.
        /// </summary>
        [JsonPropertyName("parameters")]
        public virtual OpenAiFunctionParameters? Parameters { get; set; }

        /// <summary>
        /// Gets or sets whether the function uses strict mode for parameter validation.
        /// </summary>
        [JsonPropertyName("strict")]
        public virtual bool? Strict { get; set; }
    }
} 