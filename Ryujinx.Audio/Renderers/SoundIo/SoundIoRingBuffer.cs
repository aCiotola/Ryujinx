﻿using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ryujinx.Audio.SoundIo
{
    /// <summary>
    /// A thread-safe variable-size circular buffer
    /// </summary>
    internal class SoundIoRingBuffer
    {
        private byte[] m_Buffer;
        private int    m_Size;
        private int    m_HeadOffset;
        private int    m_TailOffset;
        
        /// <summary>
        /// Gets the available bytes in the ring buffer
        /// </summary>
        public int Length
        {
            get { return m_Size; }
        }

        /// <summary>
        /// Constructs a new instance of a <see cref="SoundIoRingBuffer"/>
        /// </summary>
        public SoundIoRingBuffer()
        {
            m_Buffer = new byte[2048];
        }

        /// <summary>
        /// Constructs a new instance of a <see cref="SoundIoRingBuffer"/> with the specified capacity
        /// </summary>
        /// <param name="capacity">The number of entries that the <see cref="SoundIoRingBuffer"/> can initially contain</param>
        public SoundIoRingBuffer(int capacity)
        {
            m_Buffer = new byte[capacity];
        }

        /// <summary>
        /// Clears the ring buffer
        /// </summary>
        public void Clear()
        {
            m_Size       = 0;
            m_HeadOffset = 0;
            m_TailOffset = 0;
        }

        /// <summary>
        /// Clears the specified amount of bytes from the ring buffer
        /// </summary>
        /// <param name="size">The amount of bytes to clear from the ring buffer</param>
        public void Clear(int size)
        {
            lock (this)
            {
                if (size > m_Size)
                {
                    size = m_Size;
                }

                if (size == 0)
                {
                    return;
                }

                m_HeadOffset = (m_HeadOffset + size) % m_Buffer.Length;
                m_Size -= size;

                if (m_Size == 0)
                {
                    m_HeadOffset = 0;
                    m_TailOffset = 0;
                }

                return;
            }
        }

        /// <summary>
        /// Extends the capacity of the ring buffer
        /// </summary>
        private void SetCapacity(int capacity)
        {
            byte[] buffer = new byte[capacity];

            if (m_Size > 0)
            {
                if (m_HeadOffset < m_TailOffset)
                {
                    Buffer.BlockCopy(m_Buffer, m_HeadOffset, buffer, 0, m_Size);
                }
                else
                {
                    Buffer.BlockCopy(m_Buffer, m_HeadOffset, buffer, 0, m_Buffer.Length - m_HeadOffset);
                    Buffer.BlockCopy(m_Buffer, 0, buffer, m_Buffer.Length - m_HeadOffset, m_TailOffset);
                }
            }

            m_Buffer     = buffer;
            m_HeadOffset = 0;
            m_TailOffset = m_Size;
        }


        /// <summary>
        /// Writes a sequence of bytes to the ring buffer
        /// </summary>
        /// <param name="buffer">An array containing the data to write</param>
        /// <param name="index">The zero-based byte offset in <paramref name="buffer" /> from which to begin copying bytes to the ring buffer</param>
        /// <param name="count">The number of element to write</param>
        public void Write<T>(ReadOnlySpan<T> buffer, int index, int count) where T: struct
        {
            if (count == 0)
            {
                return;
            }

            int byteCount = count * Unsafe.SizeOf<T>();

            lock (this)
            {
                if ((m_Size + byteCount) > m_Buffer.Length)
                {
                    SetCapacity((m_Size + byteCount + 2047) & ~2047);
                }

                Span<T> internalBufferSpan = MemoryMarshal.Cast<byte, T>(m_Buffer.AsSpan());

                int tailOffset = m_TailOffset / Unsafe.SizeOf<T>();

                if (m_HeadOffset < m_TailOffset)
                {
                    int tailLength = m_Buffer.Length - tailOffset;

                    if (tailLength >= count)
                    {
                        buffer.Slice(index, count).CopyTo(internalBufferSpan.Slice(tailOffset));
                    }
                    else
                    {
                        int restCount = count - tailLength;

                        buffer.Slice(index, tailLength).CopyTo(internalBufferSpan.Slice(tailOffset));
                        buffer.Slice(index + tailLength, restCount).CopyTo(internalBufferSpan);
                    }
                }
                else
                {
                    buffer.Slice(index, count).CopyTo(internalBufferSpan.Slice(tailOffset));
                }

                m_Size += byteCount;
                m_TailOffset = (m_TailOffset + byteCount) % m_Buffer.Length;
            }
        }

        /// <summary>
        /// Reads a sequence of bytes from the ring buffer and advances the position within the ring buffer by the number of bytes read
        /// </summary>
        /// <param name="buffer">The buffer to write the data into</param>
        /// <param name="index">The zero-based byte offset in <paramref name="buffer" /> at which the read bytes will be placed</param>
        /// <param name="count">The maximum number of bytes to read</param>
        /// <returns>The total number of bytes read into the buffer. This might be less than the number of bytes requested if that number of bytes are not currently available, or zero if the ring buffer is empty</returns>
        public int Read<T>(T[] buffer, int index, int count)
        {
            lock (this)
            {
                if (count > m_Size)
                {
                    count = m_Size;
                }

                if (count == 0)
                {
                    return 0;
                }

                if (m_HeadOffset < m_TailOffset)
                {
                    Buffer.BlockCopy(m_Buffer, m_HeadOffset, buffer, index, count);
                }
                else
                {
                    int tailLength = m_Buffer.Length - m_HeadOffset;

                    if (tailLength >= count)
                    {
                        Buffer.BlockCopy(m_Buffer, m_HeadOffset, buffer, index, count);
                    }
                    else
                    {
                        Buffer.BlockCopy(m_Buffer, m_HeadOffset, buffer, index, tailLength);
                        Buffer.BlockCopy(m_Buffer, 0, buffer, index + tailLength, count - tailLength);
                    }
                }

                m_Size -= count;
                m_HeadOffset = (m_HeadOffset + count) % m_Buffer.Length;

                if (m_Size == 0)
                {
                    m_HeadOffset = 0;
                    m_TailOffset = 0;
                }

                return count;
            }
        }
    }
}
