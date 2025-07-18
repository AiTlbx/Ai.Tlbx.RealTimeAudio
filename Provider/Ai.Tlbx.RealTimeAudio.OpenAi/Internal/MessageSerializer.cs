using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Provides optimized JSON serialization for OpenAI API messages.
    /// </summary>
    internal static class MessageSerializer
    {
        private static readonly JsonSerializerOptions _standardOptions = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        private static readonly JsonSerializerOptions _camelCaseOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// Serializes an object to JSON using standard naming policy.
        /// </summary>
        /// <typeparam name="T">The type of object to serialize.</typeparam>
        /// <param name="value">The object to serialize.</param>
        /// <returns>The JSON string representation of the object.</returns>
        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, _standardOptions);
        }

        /// <summary>
        /// Serializes an object to JSON using camelCase naming policy.
        /// </summary>
        /// <typeparam name="T">The type of object to serialize.</typeparam>
        /// <param name="value">The object to serialize.</param>
        /// <returns>The JSON string representation of the object.</returns>
        public static string SerializeCamelCase<T>(T value)
        {
            return JsonSerializer.Serialize(value, _camelCaseOptions);
        }

        /// <summary>
        /// Deserializes a JSON string to an object.
        /// </summary>
        /// <typeparam name="T">The type of object to deserialize to.</typeparam>
        /// <param name="json">The JSON string to deserialize.</param>
        /// <returns>The deserialized object.</returns>
        public static T? Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, _standardOptions);
        }

        /// <summary>
        /// Deserializes a JSON string to an object using camelCase naming policy.
        /// </summary>
        /// <typeparam name="T">The type of object to deserialize to.</typeparam>
        /// <param name="json">The JSON string to deserialize.</param>
        /// <returns>The deserialized object.</returns>
        public static T? DeserializeCamelCase<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, _camelCaseOptions);
        }
    }
}