using System;
using System.Buffers;

namespace Ai.Tlbx.RealTimeAudio.OpenAi.Internal
{
    /// <summary>
    /// Provides pooled byte arrays for audio processing to reduce garbage collection pressure.
    /// </summary>
    internal static class AudioBufferPool
    {
        private static readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

        /// <summary>
        /// Rents a byte array from the pool.
        /// </summary>
        /// <param name="minimumLength">The minimum length of the array to rent.</param>
        /// <returns>A byte array from the pool.</returns>
        public static byte[] Rent(int minimumLength)
        {
            return _pool.Rent(minimumLength);
        }

        /// <summary>
        /// Returns a byte array to the pool.
        /// </summary>
        /// <param name="array">The array to return to the pool.</param>
        /// <param name="clearArray">Whether to clear the array before returning it to the pool.</param>
        public static void Return(byte[] array, bool clearArray = false)
        {
            if (array != null)
            {
                _pool.Return(array, clearArray);
            }
        }
    }
}