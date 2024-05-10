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
    public static void WriteToStream(this SilentOrbit.ProtocolBuffers.IProto proto, Stream stream)
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
        proto.WriteToStream(writer);
        
        var buffer = writer.GetBuffer();
        stream.Write(buffer.Array, buffer.Offset, buffer.Count);
    }

    public static void ReadFromStream(this SilentOrbit.ProtocolBuffers.IProto proto, Stream stream, bool isDelta = false)
    {
        if (proto == null)
        {
            throw new ArgumentNullException(nameof(proto));
        }

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
        
        var ms = Facepunch.Pool.Get<MemoryStream>();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        Facepunch.Pool.FreeMemoryStream(ref ms);
        
        using var reader = Facepunch.Pool.Get<BufferStream>().Initialize(bytes);
        proto.ReadFromStream(reader, isDelta);
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
