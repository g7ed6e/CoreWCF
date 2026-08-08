// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using CoreWCF.Channels;
using Xunit;

namespace CoreWCF.Primitives.Tests
{
    // The ByteStream encoder reads from a ReadOnlySequence<byte>, which - unlike the ArraySegment it
    // replaced - can span several segments and can start part way into the first one. These tests
    // cover the shapes a PipeReader actually produces, which a sequence built from a single array at
    // offset zero never exercises.
    public class ByteStreamMessageSequenceTests
    {
        private const string ContentType = "application/octet-stream";

        private static MessageEncoder Encoder
            => new ByteStreamMessageEncodingBindingElement().CreateMessageEncoderFactory().Encoder;

        private static BufferManager BufferManager
            => BufferManager.CreateBufferManager(int.MaxValue, int.MaxValue);

        private static byte[] Payload(int length)
            => Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();

        [Fact]
        public async Task MultiSegmentSequence_GetBodyReturnsEveryByte()
        {
            byte[] expected = Payload(300);
            ReadOnlySequence<byte> sequence = CreateMultiSegment(expected, segmentSize: 64);
            Assert.False(sequence.IsSingleSegment);

            Message message = await Encoder.ReadMessageAsync(sequence, BufferManager, ContentType);

            Assert.Equal(expected, message.GetBody<byte[]>());
        }

        [Fact]
        public async Task MultiSegmentSequence_ReadInChunks_ReturnsEachByteOnce()
        {
            byte[] expected = Payload(300);
            ReadOnlySequence<byte> sequence = CreateMultiSegment(expected, segmentSize: 64);
            Assert.False(sequence.IsSingleSegment);

            Message message = await Encoder.ReadMessageAsync(sequence, BufferManager, ContentType);

            Assert.Equal(expected, ReadBodyInChunks(message, chunkSize: 37));
        }

        [Fact]
        public async Task SingleSegmentSequence_ReadInChunks_ReturnsEachByteOnce()
        {
            byte[] expected = Payload(300);

            Message message = await Encoder.ReadMessageAsync(new ReadOnlySequence<byte>(expected), BufferManager, ContentType);

            Assert.Equal(expected, ReadBodyInChunks(message, chunkSize: 37));
        }

        [Fact]
        public async Task SequenceStartingPastTheStartOfItsSegment_ReadsOnlyTheSlice()
        {
            // A sequence whose Start is not at index 0 of its first segment: the reader must treat
            // positions as relative to the sequence, not to the underlying segment.
            byte[] backing = Payload(300);
            const int offset = 91;
            byte[] expected = backing.Skip(offset).ToArray();

            Message message = await Encoder.ReadMessageAsync(
                new ReadOnlySequence<byte>(backing, offset, backing.Length - offset), BufferManager, ContentType);

            Assert.Equal(expected, ReadBodyInChunks(message, chunkSize: 37));
        }

        [Fact]
        public async Task EmptySequence_ProducesAMessageWithAnEmptyBody()
        {
            Message message = await Encoder.ReadMessageAsync(ReadOnlySequence<byte>.Empty, BufferManager, ContentType);

            Assert.NotNull(message);
            Assert.Empty(message.GetBody<byte[]>());
        }

        [Fact]
        public async Task WriteBodyContents_ToAWriterOtherThanXmlByteStreamWriter_WritesTheBody()
        {
            byte[] expected = Encoding.ASCII.GetBytes("This is a text message");
            ReadOnlySequence<byte> sequence = CreateMultiSegment(expected, segmentSize: 8);

            Message message = await Encoder.ReadMessageAsync(sequence, BufferManager, ContentType);

            using var stream = new MemoryStream();
            using (XmlDictionaryWriter writer = XmlDictionaryWriter.CreateTextWriter(stream, Encoding.UTF8, ownsStream: false))
            {
                message.WriteBodyContents(writer);
            }

            string written = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Contains(Convert.ToBase64String(expected), written);
        }

        private static byte[] ReadBodyInChunks(Message message, int chunkSize)
        {
            using XmlDictionaryReader reader = message.GetReaderAtBodyContents();

            // The encoder hands back a reader positioned before the <Binary> element.
            while (reader.NodeType != XmlNodeType.Element)
            {
                Assert.True(reader.Read(), "Failed to reach the body element.");
            }

            Assert.True(reader.Read(), "Failed to reach the body content.");

            var actual = new List<byte>();
            byte[] chunk = new byte[chunkSize];
            int read;
            while ((read = reader.ReadContentAsBase64(chunk, 0, chunk.Length)) > 0)
            {
                actual.AddRange(chunk.Take(read));
            }

            return actual.ToArray();
        }

        private static ReadOnlySequence<byte> CreateMultiSegment(byte[] data, int segmentSize)
        {
            var first = new Segment(data.AsMemory(0, Math.Min(segmentSize, data.Length)));
            Segment last = first;
            for (int i = segmentSize; i < data.Length; i += segmentSize)
            {
                last = last.Append(data.AsMemory(i, Math.Min(segmentSize, data.Length - i)));
            }

            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

            public Segment Append(ReadOnlyMemory<byte> memory)
            {
                var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
                Next = next;
                return next;
            }
        }
    }
}
