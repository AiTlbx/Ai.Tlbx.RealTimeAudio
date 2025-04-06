using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
