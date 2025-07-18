namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Represents the possible states of the OpenAI real-time API connection.
    /// </summary>
    internal enum ConnectionState
    {
        /// <summary>
        /// The connection is not established.
        /// </summary>
        Disconnected,
        
        /// <summary>
        /// The connection is in the process of being established.
        /// </summary>
        Connecting,
        
        /// <summary>
        /// The connection is established and ready for use.
        /// </summary>
        Connected,
        
        /// <summary>
        /// The connection is established and recording audio.
        /// </summary>
        Recording,
        
        /// <summary>
        /// The connection is in the process of being closed.
        /// </summary>
        Disconnecting,
        
        /// <summary>
        /// The connection is in an error state.
        /// </summary>
        Error
    }

    /// <summary>
    /// Manages connection state transitions for the OpenAI real-time API.
    /// </summary>
    internal sealed class StateManager
    {
        private ConnectionState _currentState = ConnectionState.Disconnected;
        private readonly object _lock = new();

        /// <summary>
        /// Gets the current connection state.
        /// </summary>
        public ConnectionState CurrentState
        {
            get
            {
                lock (_lock)
                {
                    return _currentState;
                }
            }
        }

        /// <summary>
        /// Attempts to transition from one state to another.
        /// </summary>
        /// <param name="from">The expected current state.</param>
        /// <param name="to">The desired new state.</param>
        /// <returns>True if the transition was successful, false otherwise.</returns>
        public bool TryTransition(ConnectionState from, ConnectionState to)
        {
            lock (_lock)
            {
                if (_currentState != from)
                    return false;

                _currentState = to;
                return true;
            }
        }

        /// <summary>
        /// Forces a state transition without validation.
        /// </summary>
        /// <param name="newState">The new state to set.</param>
        public void ForceTransition(ConnectionState newState)
        {
            lock (_lock)
            {
                _currentState = newState;
            }
        }

        /// <summary>
        /// Checks if the current state is one of the specified states.
        /// </summary>
        /// <param name="states">The states to check against.</param>
        /// <returns>True if the current state matches any of the specified states.</returns>
        public bool IsInState(params ConnectionState[] states)
        {
            lock (_lock)
            {
                foreach (var state in states)
                {
                    if (_currentState == state)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Checks if a transition from the current state to the specified state is valid.
        /// </summary>
        /// <param name="to">The target state.</param>
        /// <returns>True if the transition is valid.</returns>
        public bool IsValidTransition(ConnectionState to)
        {
            lock (_lock)
            {
                return _currentState switch
                {
                    ConnectionState.Disconnected => to == ConnectionState.Connecting,
                    ConnectionState.Connecting => to == ConnectionState.Connected || to == ConnectionState.Error || to == ConnectionState.Disconnected,
                    ConnectionState.Connected => to == ConnectionState.Recording || to == ConnectionState.Disconnecting || to == ConnectionState.Error,
                    ConnectionState.Recording => to == ConnectionState.Connected || to == ConnectionState.Disconnecting || to == ConnectionState.Error,
                    ConnectionState.Disconnecting => to == ConnectionState.Disconnected || to == ConnectionState.Error,
                    ConnectionState.Error => to == ConnectionState.Disconnected || to == ConnectionState.Connecting,
                    _ => false
                };
            }
        }
    }
}