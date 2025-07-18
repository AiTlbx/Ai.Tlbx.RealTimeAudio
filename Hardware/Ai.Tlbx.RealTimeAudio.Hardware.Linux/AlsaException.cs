using System.Runtime.Serialization;

namespace Ai.Tlbx.RealTimeAudio.Hardware.Linux
{
    /// <summary>
    /// Exception thrown when an ALSA operation fails
    /// </summary>
#pragma warning disable CS0618 // Suppress obsolete warning for this class
    public class AlsaException : Exception
#pragma warning restore CS0618
    {
        /// <summary>
        /// Gets the ALSA error code that caused this exception
        /// </summary>
        public int ErrorCode { get; init; }

        /// <summary>
        /// Gets the operation that was being performed when the error occurred
        /// </summary>
        public string Operation { get; init; }

        /// <summary>
        /// Initializes a new instance of the AlsaException class
        /// </summary>
        public AlsaException() 
            : base()
        {
            Operation = "Unknown";
        }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message
        /// </summary>
        public AlsaException(string message) 
            : base(message)
        {
            Operation = "Unknown";
        }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message
        /// and a reference to the inner exception that is the cause of this exception
        /// </summary>
        public AlsaException(string message, Exception innerException) 
            : base(message, innerException)
        {
            Operation = "Unknown";
        }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message,
        /// error code, and operation
        /// </summary>
        public AlsaException(string message, int errorCode, string operation)
            : base(message)
        {
            Operation = operation ?? string.Empty;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message,
        /// error code, operation, and inner exception
        /// </summary>
        public AlsaException(string message, int errorCode, string operation, Exception innerException)
            : base(message, innerException)
        {
            Operation = operation ?? string.Empty;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message,
        /// error code, and operation
        /// </summary>
        public AlsaException(int errorCode)
        {
            Operation = "Unknown";
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message,
        /// error code, and operation
        /// </summary>
        public AlsaException(int errorCode, string operation)
        {
            Operation = operation;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message,
        /// error code, operation, and inner exception
        /// </summary>
        public AlsaException(int errorCode, string operation, string message)
            : base(message)
        {
            Operation = operation;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with serialized data
        /// </summary>
        #pragma warning disable SYSLIB0051 // Suppress obsolete serialization warning
        protected AlsaException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        #pragma warning restore SYSLIB0051
        {
            Operation = info.GetString(nameof(Operation)) ?? string.Empty;
            ErrorCode = info.GetInt32(nameof(ErrorCode));
        }

        /// <summary>
        /// Sets the SerializationInfo with information about the exception
        /// </summary>
        [Obsolete("This method is obsolete due to formatter-based serialization.")]
        #pragma warning disable SYSLIB0051 // Suppress obsolete serialization warning
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        #pragma warning restore SYSLIB0051
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            base.GetObjectData(info, context);
            info.AddValue(nameof(Operation), Operation);
            info.AddValue(nameof(ErrorCode), ErrorCode);
        }

        /// <summary>
        /// Gets a message that describes the error based on the ALSA error code
        /// </summary>
        /// <param name="errorCode">The ALSA error code</param>
        /// <returns>A string describing the error</returns>
        private static string GetErrorMessage(int errorCode)
        {
            // This is a placeholder since we don't have direct access to ALSA error strings
            // In a real implementation, this would call snd_strerror or similar
            return $"ALSA error code: {errorCode}";
        }
    }
} 