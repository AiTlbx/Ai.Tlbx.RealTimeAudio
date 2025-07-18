using System.Text.Json;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Helper
{
    internal static class Extensions
    {
        private static JsonSerializerOptions? _options;

        public static string ToJson(this object obj)
        {
            if (_options == null)
            {
                _options = new JsonSerializerOptions
                {
                    WriteIndented = true,                   
                };
            }

            return JsonSerializer.Serialize(obj, _options);
        }
    }
}
