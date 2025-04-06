namespace Ai.Tlbx.RealTimeAudio.OpenAi
{
    /// <summary>
    /// Event arguments for when OpenAI requests a tool call.
    /// </summary>
    public class ToolCallEventArgs : EventArgs
    {
        public string ToolCallId { get; } // Unique ID for this specific call
        public string FunctionName { get; } // Name of the function/tool requested
        public string ArgumentsJson { get; } // Arguments as a JSON string

        public ToolCallEventArgs(string toolCallId, string functionName, string argumentsJson)
        {
            ToolCallId = toolCallId;
            FunctionName = functionName;
            ArgumentsJson = argumentsJson;
        }
    }
}
