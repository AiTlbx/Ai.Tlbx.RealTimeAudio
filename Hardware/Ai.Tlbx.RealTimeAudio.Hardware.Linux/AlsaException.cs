using System;
using System.Runtime.Serialization;

namespace Ai.Tlbx.RealTimeAudio.Hardware.Linux
{
    /// <summary>
    /// Exception thrown when an ALSA operation fails
    /// </summary>
    [Serializable]
    public class AlsaException : Exception
    {
        /// <summary>
        /// Gets the ALSA error code that caused this exception
        /// </summary>
        public int ErrorCode { get; }

        /// <summary>
        /// Gets the operation that was being performed when the error occurred
        /// </summary>
        public string Operation { get; }

        /// <summary>
        /// Initializes a new instance of the AlsaException class
        /// </summary>
        public AlsaException() 
            : base() { }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message
        /// </summary>
        public AlsaException(string message) 
            : base(message) { }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message
        /// and a reference to the inner exception that is the cause of this exception
        /// </summary>
        public AlsaException(string message, Exception innerException) 
            : base(message, innerException) { }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message,
        /// error code, and operation
        /// </summary>
        public AlsaException(string message, int errorCode, string operation)
            : base(message)
        {
            ErrorCode = errorCode;
            Operation = operation;
        }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with a specified error message,
        /// error code, operation, and inner exception
        /// </summary>
        public AlsaException(string message, int errorCode, string operation, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            Operation = operation;
        }

        /// <summary>
        /// Initializes a new instance of the AlsaException class with serialized data
        /// </summary>
        protected AlsaException(SerializationInfo info, StreamingContext context) 
            : base(info, context)
        {
            ErrorCode = info.GetInt32(nameof(ErrorCode));
            Operation = info.GetString(nameof(Operation)) ?? string.Empty;
        }

        /// <summary>
        /// Sets the SerializationInfo with information about the exception
        /// </summary>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(ErrorCode), ErrorCode);
            info.AddValue(nameof(Operation), Operation);
            base.GetObjectData(info, context);
        }
    }
} 