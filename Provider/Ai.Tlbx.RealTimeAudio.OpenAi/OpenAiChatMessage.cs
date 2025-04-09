namespace Ai.Tlbx.RealTimeAudio.OpenAi;

/// <summary>
/// Represents a chat message in the OpenAI conversation format.
/// Provides standard message types and factory methods for tool-related messages.
/// </summary>
public class OpenAiChatMessage
{
    // Constants for Roles
    public const string UserRole = "user";
    public const string AssistantRole = "assistant";
    public const string ToolCallRole = "tool_call";
    public const string ToolResultRole = "tool_result";

    // Core Properties
    /// <summary>
    /// The content of the message.
    /// </summary>
    public string Content { get; private set; }
    
    /// <summary>
    /// The role of the message sender (user, assistant, tool_call, tool_result).
    /// </summary>
    public string Role { get; private set; }
    
    /// <summary>
    /// Timestamp when the message was created.
    /// </summary>
    public DateTime Timestamp { get; }

    // Tool-related Properties (nullable)
    /// <summary>
    /// Unique identifier for tool calls, used to match calls with their results.
    /// </summary>
    public string? ToolCallId { get; private set; }
    
    /// <summary>
    /// Name of the tool being called or that produced the result.
    /// </summary>
    public string? ToolName { get; private set; }
    
    /// <summary>
    /// JSON-formatted arguments for the tool call.
    /// </summary>
    public string? ToolArgumentsJson { get; private set; }
    
    /// <summary>
    /// JSON-formatted result from the tool execution.
    /// Only populated for messages with ToolResultRole.
    /// </summary>
    public string? ToolResultJson { get; private set; }

    // Private constructor to control instantiation via factory methods
    private OpenAiChatMessage(string role)
    {
        Role = role;
        Timestamp = DateTime.Now;
        Content = string.Empty;
    }

    /// <summary>
    /// Creates a standard user or assistant message.
    /// </summary>
    /// <param name="content">The message content.</param>
    /// <param name="role">The role of the message sender (must be UserRole or AssistantRole).</param>
    /// <exception cref="ArgumentException">Thrown when an invalid role is provided.</exception>
    public OpenAiChatMessage(string content, string role)
    {
        if (role != UserRole && role != AssistantRole)
        {
            throw new ArgumentException($"Invalid role '{role}' for standard message constructor. Use factory methods for tool messages.", nameof(role));
        }
        Content = content;
        Role = role;
        Timestamp = DateTime.Now;
    }
    
    /// <summary>
    /// Creates a tool call message representing an AI's request to use a specific tool.
    /// </summary>   
    /// <param name="toolName">Name of the tool to be called.</param>
    /// <param name="argumentsJson">JSON-formatted arguments for the tool.</param>
    /// <returns>A new OpenAiChatMessage with ToolCallRole.</returns>
    public static OpenAiChatMessage CreateToolCallMessage(string toolName, string argumentsJson)
    {
        var message = new OpenAiChatMessage(ToolCallRole)
        {           
            ToolName = toolName,
            ToolArgumentsJson = argumentsJson,
            Content = $"AI requested tool: {toolName}"
        };
        return message;
    }
    
    /// <summary>
    /// Creates a tool result message representing the outcome of a tool execution.
    /// </summary>
    /// <param name="toolCallId">Identifier matching the original tool call.</param>
    /// <param name="toolName">Name of the tool that produced the result.</param>
    /// <param name="resultJson">JSON-formatted result from the tool execution.</param>
    /// <returns>A new OpenAiChatMessage with ToolResultRole.</returns>
    public static OpenAiChatMessage CreateToolResultMessage(string toolName, string resultJson)
    {
        var message = new OpenAiChatMessage(ToolResultRole)
        {    
            ToolName = toolName,
            ToolResultJson = resultJson,
            Content = $"Tool result for: {toolName}"
        };
        return message;
    }
}