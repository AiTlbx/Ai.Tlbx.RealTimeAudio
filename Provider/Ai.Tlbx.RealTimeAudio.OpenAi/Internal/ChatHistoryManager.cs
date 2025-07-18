namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Manages the chat history for the OpenAI real-time conversation.
    /// </summary>
    internal sealed class ChatHistoryManager
    {
        private readonly List<OpenAiChatMessage> _messages = new();
        private readonly object _lock = new();

        /// <summary>
        /// Gets the current chat messages as a read-only list.
        /// </summary>
        /// <returns>A read-only list of chat messages.</returns>
        public IReadOnlyList<OpenAiChatMessage> GetMessages()
        {
            lock (_lock)
            {
                return _messages.AsReadOnly();
            }
        }

        /// <summary>
        /// Adds a new message to the chat history.
        /// </summary>
        /// <param name="message">The message to add.</param>
        public void AddMessage(OpenAiChatMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            lock (_lock)
            {
                _messages.Add(message);
            }
        }

        /// <summary>
        /// Clears all messages from the chat history.
        /// </summary>
        public void ClearHistory()
        {
            lock (_lock)
            {
                _messages.Clear();
            }
        }

        /// <summary>
        /// Gets the number of messages in the chat history.
        /// </summary>
        /// <returns>The number of messages.</returns>
        public int GetMessageCount()
        {
            lock (_lock)
            {
                return _messages.Count;
            }
        }

        /// <summary>
        /// Gets the last message in the chat history.
        /// </summary>
        /// <returns>The last message, or null if no messages exist.</returns>
        public OpenAiChatMessage? GetLastMessage()
        {
            lock (_lock)
            {
                return _messages.LastOrDefault();
            }
        }

        /// <summary>
        /// Checks if the given message should be added to avoid duplicates.
        /// </summary>
        /// <param name="messageText">The message text to check.</param>
        /// <param name="role">The role of the message.</param>
        /// <returns>True if the message should be added, false otherwise.</returns>
        public bool ShouldAddMessage(string messageText, string role)
        {
            if (string.IsNullOrWhiteSpace(messageText))
                return false;

            lock (_lock)
            {
                if (_messages.Count == 0)
                    return true;

                var lastMessage = _messages[_messages.Count - 1];
                return lastMessage.Role != role || lastMessage.Content != messageText;
            }
        }

        /// <summary>
        /// Gets messages of a specific role.
        /// </summary>
        /// <param name="role">The role to filter by (e.g., "user", "assistant").</param>
        /// <returns>A list of messages with the specified role.</returns>
        public IReadOnlyList<OpenAiChatMessage> GetMessagesByRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                throw new ArgumentException("Role cannot be null or empty", nameof(role));

            lock (_lock)
            {
                return _messages.Where(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase)).ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Gets the most recent messages up to a specified count.
        /// </summary>
        /// <param name="count">The maximum number of messages to retrieve.</param>
        /// <returns>A list of the most recent messages.</returns>
        public IReadOnlyList<OpenAiChatMessage> GetRecentMessages(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative");

            lock (_lock)
            {
                return _messages.Skip(Math.Max(0, _messages.Count - count)).ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Removes the last message from the chat history.
        /// </summary>
        /// <returns>The removed message, or null if no messages exist.</returns>
        public OpenAiChatMessage? RemoveLastMessage()
        {
            lock (_lock)
            {
                if (_messages.Count == 0)
                    return null;

                var lastMessage = _messages[_messages.Count - 1];
                _messages.RemoveAt(_messages.Count - 1);
                return lastMessage;
            }
        }

        /// <summary>
        /// Formats the chat history as a string for logging or display purposes.
        /// </summary>
        /// <returns>A formatted string representation of the chat history.</returns>
        public string GetFormattedHistory()
        {
            lock (_lock)
            {
                if (_messages.Count == 0)
                    return "No messages in history";

                var formatted = new System.Text.StringBuilder();
                foreach (var message in _messages)
                {
                    formatted.AppendLine($"[{message.Role}]: {message.Content}");
                }
                return formatted.ToString();
            }
        }
    }
}