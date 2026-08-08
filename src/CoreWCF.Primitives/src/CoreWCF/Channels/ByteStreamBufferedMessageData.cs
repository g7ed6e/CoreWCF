// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using CoreWCF.Runtime;

namespace CoreWCF.Channels
{
    internal class ByteStreamBufferedMessageData
    {
        private ReadOnlySequence<byte> _buffer;
        private BufferManager _bufferManager;
        private byte[] _rentedBuffer;
        private int _refCount;

        public ByteStreamBufferedMessageData(ReadOnlySequence<byte> buffer)
        {
            _buffer = buffer;
            _refCount = 0;
        }

        /// <summary>
        /// The buffer is a single array rented from <paramref name="bufferManager"/>; it is handed
        /// back once the last reference to this instance is released.
        /// </summary>
        public ByteStreamBufferedMessageData(ArraySegment<byte> buffer, BufferManager bufferManager)
            : this(new ReadOnlySequence<byte>(buffer.Array, buffer.Offset, buffer.Count))
        {
            _bufferManager = bufferManager;
            _rentedBuffer = buffer.Array;
        }

        private bool IsClosed => _refCount < 0;

        public ReadOnlySequence<byte> ReadOnlyBuffer
        {
            get
            {
                ThrowIfClosed();
                return _buffer;
            }
        }

        public void Open()
        {
            ThrowIfClosed();
            _refCount++;
        }

        public void Close()
        {
            if (!IsClosed)
            {
                if (--_refCount <= 0)
                {
                    if (_bufferManager != null && _rentedBuffer != null)
                    {
                        _bufferManager.ReturnBuffer(_rentedBuffer);
                    }

                    _bufferManager = null;
                    _rentedBuffer = null;
                    _buffer = default;
                    _refCount = int.MinValue;
                }
            }
        }

        public Stream ToStream() => new ByteStreamBufferedMessageDataStream(this);

        private void ThrowIfClosed()
        {
            if (IsClosed)
            {
                throw Fx.Exception.ObjectDisposed(SR.Format(SR.ObjectDisposed, this));
            }
        }

        // Holds a reference on the message data for as long as the body stream is in use, so the
        // buffer it reads from isn't handed back to the BufferManager underneath it.
        private sealed class ByteStreamBufferedMessageDataStream : DelegatingStream
        {
            private readonly ByteStreamBufferedMessageData _messageData;
            private bool _closed;

            public ByteStreamBufferedMessageDataStream(ByteStreamBufferedMessageData messageData)
                : base(PipeReader.Create(messageData.ReadOnlyBuffer).AsStream())
            {
                _messageData = messageData;
                _messageData.Open();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_closed)
                {
                    _closed = true;
                    _messageData.Close();
                }

                base.Dispose(disposing);
            }
        }
    }
}
