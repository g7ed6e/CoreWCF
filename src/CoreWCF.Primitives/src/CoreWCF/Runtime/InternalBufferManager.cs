// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;

namespace CoreWCF.Runtime
{
    internal abstract class InternalBufferManager
    {
        protected InternalBufferManager()
        {
        }

        public abstract byte[] TakeBuffer(int bufferSize);
        public abstract void ReturnBuffer(byte[] buffer);
        public abstract void Clear();

        public static InternalBufferManager Create(long maxBufferPoolSize, int maxBufferSize)
        {
            if (maxBufferPoolSize == 0)
            {
                return GCBufferManager.Value;
            }
            else
            {
                Fx.Assert(maxBufferPoolSize > 0 && maxBufferSize >= 0, "bad params, caller should verify");
                return new PooledBufferManager2(maxBufferPoolSize, maxBufferSize);
            }
        }

        private class PooledBufferManager2 : InternalBufferManager
        {
            public PooledBufferManager2(long maxBufferPoolSize, int maxBufferSize)
            {

            }

            public override byte[] TakeBuffer(int bufferSize) => ArrayPool<byte>.Shared.Rent(bufferSize);

            public override void ReturnBuffer(byte[] buffer) => ArrayPool<byte>.Shared.Return(buffer);

            public override void Clear()
            {

            }
        }

        private class GCBufferManager : InternalBufferManager
        {
            private GCBufferManager()
            {
            }

            public static GCBufferManager Value { get; } = new();

            public override void Clear()
            {
            }

            public override byte[] TakeBuffer(int bufferSize)
            {
                return Fx.AllocateByteArray(bufferSize);
            }

            public override void ReturnBuffer(byte[] buffer)
            {
                // do nothing, GC will reclaim this buffer
            }
        }
    }
}
