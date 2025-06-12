using Ai.Tlbx.RealTimeAudio.OpenAi.Models;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Tools
{
    /// <summary>
    /// Abstract base class for defining tools that the AI can use.
    /// Provides structure for defining the tool to OpenAI and executing it.
    /// </summary>
    public abstract class RealTimeTool : OpenAiFunctionDefinition
    {
        /// <summary>
        /// Executes the tool's logic with the provided arguments.
        /// </summary>
        /// <param name="argumentsJson">A JSON string containing the arguments provided by the AI.</param>
        /// <returns>A string result to be sent back to the AI. Can be simple text or JSON.</returns>
        public abstract Task<string> ExecuteAsync(string argumentsJson);
    }
} 