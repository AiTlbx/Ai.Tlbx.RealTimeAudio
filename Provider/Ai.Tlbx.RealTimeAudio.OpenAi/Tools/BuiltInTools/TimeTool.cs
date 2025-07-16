using Ai.Tlbx.RealTimeAudio.OpenAi.Models;
using System;
using System.Threading.Tasks;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Tools.BuiltInTools
{
    /// <summary>
    /// A built-in tool for retrieving the current date and time.
    /// </summary>
    public class TimeTool : RealTimeTool
    {
        public override string Name => "get_current_time";

        public override string Description => "Gets the current exact date and time. Just tell the user the time with seconds. If he asks also tell the date or convert the time to a given time zone, this server stands in berlin time)";

        public override bool? Strict => false;

        /// <summary>
        /// Executes the tool to get the current time.
        /// </summary>
        /// <param name="argumentsJson">Ignored for this tool as it takes no arguments.</param>
        /// <returns>The current date and time on the server as an ISO 8601 string.</returns>
        public override Task<string> ExecuteAsync(string argumentsJson)
        {
            // Arguments are ignored.            
            var currentTime = DateTime.Now.ToString("yyyy.MM.dd - HH:mm:ss"); 
            return Task.FromResult("The current time is: " + currentTime);
        }
    }
} 