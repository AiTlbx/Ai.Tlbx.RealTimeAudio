namespace Ai.Tlbx.RealTimeAudio.OpenAi.Events
{
    /// <summary>
    /// Event arguments for when OpenAI requests a tool call.
    /// </summary>
    public class ToolCallEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the unique ID for this specific tool call.
        /// </summary>
        public string ToolCallId { get; }

        /// <summary>
        /// Gets the name of the function/tool requested.
        /// </summary>
        public string FunctionName { get; }

        /// <summary>
        /// Gets the arguments as a JSON string.
        /// </summary>
        public string ArgumentsJson { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallEventArgs"/> class.
        /// </summary>
        /// <param name="toolCallId">The unique ID for this specific tool call.</param>
        /// <param name="functionName">The name of the function/tool requested.</param>
        /// <param name="argumentsJson">The arguments as a JSON string.</param>
        public ToolCallEventArgs(string toolCallId, string functionName, string argumentsJson)
        {
            ToolCallId = toolCallId;
            FunctionName = functionName;
            ArgumentsJson = argumentsJson;
        }
    }
}
