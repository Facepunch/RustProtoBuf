using System;
using System.IO;
using System.Text;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Facepunch.Extend;
using UnityEngine;
using UnityEngine.Profiling;
using SilentOrbit.ProtocolBuffers;

namespace SilentOrbit.ProtocolBuffers
{
    public interface IProto
    {
        void WriteToStream( BufferStream stream );
        void ReadFromStream( BufferStream stream, bool isDelta = false );
        void ReadFromStream( BufferStream stream, int size, bool isDelta = false );
    }

    public interface IProto<in T> : IProto
        where T : IProto
    {
        void WriteToStreamDelta( BufferStream stream, T previousProto );
        
        void CopyTo( T other );
    }
    
    public static partial class ProtocolParser
    {
        private const int staticBufferSize = 128 * 1024;
        
        // Seperate copy of buffer per thread
        [ThreadStatic] private static byte[] _staticBuffer;
        
        private static byte[] GetStaticBuffer() => _staticBuffer ??= new byte[staticBufferSize];

        public static int ReadFixedInt32( BufferStream stream ) => stream.Read<int>();

        public static void WriteFixedInt32( BufferStream stream, int i ) => stream.Write<int>(i);
        
        public static long ReadFixedInt64( BufferStream stream ) => stream.Read<long>();

        public static void WriteFixedInt64( BufferStream stream, long i ) => stream.Write<long>(i);
        
        public static float ReadSingle( BufferStream stream ) => stream.Read<float>();

        public static void WriteSingle( BufferStream stream, float f ) => stream.Write<float>(f);

        public static double ReadDouble( BufferStream stream ) => stream.Read<double>();

        public static void WriteDouble( BufferStream stream, double f ) => stream.Write<double>(f);

        public static unsafe string ReadString( BufferStream stream )
        {
            Profiler.BeginSample( "ProtoParser.ReadString" );
			
            int length = (int)ReadUInt32( stream );
            if ( length <= 0 )
            {
                Profiler.EndSample();
                return "";
            }

            string str;
            var bytes = stream.GetRange( length ).GetSpan();
            fixed ( byte* ptr = &bytes[0] )
            {
                str = Encoding.UTF8.GetString( ptr, length );
            }

            Profiler.EndSample();

            return str;
        }

        public static void WriteString( BufferStream stream, string val )
        {
            Profiler.BeginSample( "ProtoParser.WriteString" );

            var buffer = GetStaticBuffer();
            var len = Encoding.UTF8.GetBytes( val, 0, val.Length, buffer, 0 );

            WriteUInt32( stream, (uint)len );

            if ( len > 0 )
            {
                new Span<byte>( buffer, 0, len ).CopyTo( stream.GetRange( len ).GetSpan() );
            }
            
            Profiler.EndSample();
        }

        /// <summary>
        /// Reads a length delimited byte array into a new byte[]
        /// </summary>
        public static byte[] ReadBytes( BufferStream stream )
        {
            Profiler.BeginSample( "ProtoParser.ReadBytes" );

            // Only limit length when reading from network
            int length = (int)ReadUInt32( stream );

            //Bytes
            byte[] buffer = new byte[ length ];
            ReadBytesInto( stream, buffer, length );
            Profiler.EndSample();

            return buffer;
        }

        /// <summary>
        /// Read into a byte[] that is disposed when the object is returned to the pool
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static ArraySegment<byte> ReadPooledBytes( BufferStream stream )
        {
            Profiler.BeginSample( "ProtoParser.ReadPooledBytes" );

            // Only limit length when reading from network
            int length = (int)ReadUInt32( stream );

            //Bytes
            byte[] buffer = BufferStream.Shared.ArrayPool.Rent( length );
            ReadBytesInto( stream, buffer, length );
            Profiler.EndSample();

            return new ArraySegment<byte>( buffer, 0, length );
        }

        private static void ReadBytesInto( BufferStream stream, byte[] buffer, int length )
        {
            stream.GetRange( length ).GetSpan().CopyTo( buffer );
        }

        /// <summary>
        /// Skip the next varint length prefixed bytes.
        /// Alternative to ReadBytes when the data is not of interest.
        /// </summary>
        public static void SkipBytes( BufferStream stream )
        {
            int length = (int)ReadUInt32( stream );
            stream.Skip( length );
        }
        
        /// <summary>
        /// Writes length delimited byte array
        /// </summary>
        public static void WriteBytes( BufferStream stream, byte[] val )
        {
            WriteUInt32( stream, (uint)val.Length );
            new Span<byte>( val ).CopyTo( stream.GetRange( val.Length ).GetSpan() );
        }

        public static void WritePooledBytes( BufferStream stream, ArraySegment<byte> segment )
        {
            if (segment.Array == null)
            {
                WriteUInt32( stream, 0 );
                return;
            }

            WriteUInt32( stream, (uint)segment.Count );
            new Span<byte>( segment.Array, segment.Offset, segment.Count ).CopyTo( stream.GetRange( segment.Count ).GetSpan() );
        }
    }
}

public static class ProtoStreamExtensions
{
    public static void WriteToStream(this SilentOrbit.ProtocolBuffers.IProto proto, Stream stream, bool lengthDelimited = false, int maxSizeHint = 2 * 1024 * 1024)
    {
        if (proto == null)
        {
            throw new ArgumentNullException(nameof(proto));
        }

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var writer = Facepunch.Pool.Get<BufferStream>().Initialize();

        var (maxLength, lengthPrefixSize) = GetLengthPrefixSize(maxSizeHint);
        BufferStream.RangeHandle lengthRange = default;
        if (lengthDelimited)
        {
            lengthRange = writer.GetRange(lengthPrefixSize);
        }

        var start = writer.Position;
        proto.WriteToStream(writer);

        if (lengthDelimited)
        {
            var length = writer.Position - start;
            if (length > maxLength)
            {
                throw new InvalidOperationException($"Written proto exceeds maximum size hint (maxSizeHint={maxSizeHint}, actualLength={length})");
            }
			
            var lengthSpan = lengthRange.GetSpan();
            var writtenBytes = ProtocolParser.WriteUInt32((uint)length, lengthSpan, 0);

            if (writtenBytes != lengthPrefixSize)
            {
                lengthSpan[writtenBytes - 1] |= 0x80; // mark the last written byte as having a continuation
                
                while (writtenBytes < lengthPrefixSize - 1)
                {
                    lengthSpan[writtenBytes++] = 0x80; // continuation with no bits set
                }
                
                lengthSpan[writtenBytes] = 0; // and the last byte terminates the varint
            }
        }

        if (writer.Length > 0)
        {
            var buffer = writer.GetBuffer();
            stream.Write(buffer.Array, buffer.Offset, buffer.Count);
        }
    }
    
    private static (int MaxLength, int LengthPrefixSize) GetLengthPrefixSize(int maxSizeHint)
    {
        if (maxSizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSizeHint));
        }

        if (maxSizeHint <= 0x7F) return (0x7F, 1);
        if (maxSizeHint <= 0x3FFF) return (0x3FFF, 2);
        if (maxSizeHint <= 0x1FFFFF) return (0x1FFFFF, 3);
        if (maxSizeHint <= 0xFFFFFFF) return (0xFFFFFF, 4);
        
        throw new ArgumentOutOfRangeException(nameof(maxSizeHint));
    }

    public static void ReadFromStream(this SilentOrbit.ProtocolBuffers.IProto proto, Stream stream, bool isDelta = false, int maxSize = 1 * 1024 * 1024)
    {
        if (proto == null)
        {
            throw new ArgumentNullException(nameof(proto));
        }

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var startPosition = stream.Position;
		
        var buffer = BufferStream.Shared.ArrayPool.Rent(maxSize);
        var offset = 0;
        var remaining = maxSize;
        while (remaining > 0)
        {
            var bytesRead = stream.Read(buffer, offset, remaining);
            if (bytesRead <= 0)
            {
                break;
            }

            offset += bytesRead;
            remaining -= bytesRead;
        }
		
        using var reader = Facepunch.Pool.Get<BufferStream>().Initialize(buffer, offset);
        proto.ReadFromStream(reader, isDelta);
        BufferStream.Shared.ArrayPool.Return(buffer);
        
        var protoReadLength = reader.Position;
        stream.Position = startPosition + protoReadLength;
    }

    public static void ReadFromStream(this SilentOrbit.ProtocolBuffers.IProto proto, Stream stream, int length, bool isDelta = false)
    {
        if (proto == null)
        {
            throw new ArgumentNullException(nameof(proto));
        }

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var buffer = BufferStream.Shared.ArrayPool.Rent(length);
        var offset = 0;
        var remaining = length;
        while (remaining > 0)
        {
            var bytesRead = stream.Read(buffer, offset, remaining);
            if (bytesRead <= 0)
            {
                throw new InvalidOperationException("Unexpected end of stream");
            }

            offset += bytesRead;
            remaining -= bytesRead;
        }
        
        using var reader = Facepunch.Pool.Get<BufferStream>().Initialize(buffer, length);
        proto.ReadFromStream(reader, isDelta);
        
        BufferStream.Shared.ArrayPool.Return(buffer);
    }

    public static void ReadFromStreamLengthDelimited(this SilentOrbit.ProtocolBuffers.IProto proto, Stream stream, bool isDelta = false)
    {
        if (proto == null)
        {
            throw new ArgumentNullException(nameof(proto));
        }

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
        
        var length = (int)ProtocolParser.ReadUInt32(stream);
        ReadFromStream(proto, stream, length, isDelta);
    }
    
    public static byte[] ToProtoBytes(this SilentOrbit.ProtocolBuffers.IProto proto)
    {
        if (proto == null)
        {
            throw new ArgumentNullException(nameof(proto));
        }

        using var writer = Facepunch.Pool.Get<BufferStream>().Initialize();
        proto.WriteToStream(writer);
        
        var buffer = writer.GetBuffer();
        var bytes = new byte[writer.Position];
        new Span<byte>(buffer.Array, buffer.Offset, buffer.Count).CopyTo(bytes);
        return bytes;
    }
}
