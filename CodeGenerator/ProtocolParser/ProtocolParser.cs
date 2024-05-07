using System;
using System.IO;
using System.Text;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Facepunch.Extend;
using UnityEngine.Profiling;
using SilentOrbit.ProtocolBuffers;

namespace SilentOrbit.ProtocolBuffers
{
    public interface IProto
    {
        void WriteToStream( ref BufferStream stream );
        void ReadFromStream( ref BufferStream stream, bool isDelta = false );
        void ReadFromStream( ref BufferStream stream, int size, bool isDelta = false );
    }

    public interface IProto<in T> : IProto
        where T : IProto
    {
        void WriteToStreamDelta( ref BufferStream stream, T previousProto );
    }
    
    public static partial class ProtocolParser
    {
        private const int staticBufferSize = 128 * 1024;
        
        // Seperate copy of buffer per thread
        [ThreadStatic] private static byte[] _staticBuffer;
        
        private static byte[] GetStaticBuffer() => _staticBuffer ??= new byte[staticBufferSize];

        public static readonly Facepunch.ArrayPool<byte> ArrayPool = new(128 * 1024 * 1024);

        public static int ReadFixedInt32( ref BufferStream stream ) =>
            stream.Data<int>();

        public static void WriteFixedInt32( ref BufferStream stream, int i ) =>
            stream.Data<int>() = i;
        
        public static long ReadFixedInt64( ref BufferStream stream ) =>
            stream.Data<long>();

        public static void WriteFixedInt64( ref BufferStream stream, long i ) =>
            stream.Data<long>() = i;
        
        public static float ReadSingle( ref BufferStream stream ) =>
            stream.Data<float>();

        public static void WriteSingle( ref BufferStream stream, float f ) =>
            stream.Data<float>() = f;

        public static double ReadDouble( ref BufferStream stream ) =>
            stream.Data<double>();

        public static void WriteDouble( ref BufferStream stream, double f ) =>
            stream.Data<double>() = f;

        public static unsafe string ReadString( ref BufferStream stream )
        {
            Profiler.BeginSample( "ProtoParser.ReadString" );
			
            int length = (int)ReadUInt32( ref stream );
            if ( length <= 0 )
            {
                Profiler.EndSample();
                return "";
            }

            string str;
            var bytes = stream.Bytes( length );
            fixed ( byte* ptr = &bytes[0] )
            {
                str = Encoding.UTF8.GetString( ptr, length );
            }

            Profiler.EndSample();

            return str;
        }

        public static void WriteString( ref BufferStream stream, string val )
        {
            Profiler.BeginSample( "ProtoParser.WriteString" );

            var buffer = GetStaticBuffer();
            var len = Encoding.UTF8.GetBytes( val, 0, val.Length, buffer, 0 );

            WriteUInt32( ref stream, (uint)len );

            if ( len > 0 )
            {
                new Span<byte>( buffer, 0, len ).CopyTo( stream.Bytes( len ) );
            }
            
            Profiler.EndSample();
        }

        /// <summary>
        /// Reads a length delimited byte array into a new byte[]
        /// </summary>
        public static byte[] ReadBytes( ref BufferStream stream )
        {
            Profiler.BeginSample( "ProtoParser.ReadBytes" );

            // Only limit length when reading from network
            int length = (int)ReadUInt32( ref stream );

            //Bytes
            byte[] buffer = new byte[ length ];
            ReadBytesInto( ref stream, buffer, length );
            Profiler.EndSample();

            return buffer;
        }

        /// <summary>
        /// Read into a byte[] that is disposed when the object is returned to the pool
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static ArraySegment<byte> ReadPooledBytes( ref BufferStream stream )
        {
            Profiler.BeginSample( "ProtoParser.ReadPooledBytes" );

            // Only limit length when reading from network
            int length = (int)ReadUInt32( ref stream );

            //Bytes
            byte[] buffer = ArrayPool.Rent( length );
            ReadBytesInto( ref stream, buffer, length );
            Profiler.EndSample();

            return new ArraySegment<byte>( buffer, 0, length );
        }

        private static void ReadBytesInto( ref BufferStream stream, byte[] buffer, int length )
        {
            stream.Bytes( length ).CopyTo( buffer );
        }

        /// <summary>
        /// Skip the next varint length prefixed bytes.
        /// Alternative to ReadBytes when the data is not of interest.
        /// </summary>
        public static void SkipBytes( ref BufferStream stream )
        {
            int length = (int)ReadUInt32( ref stream );
            stream.Bytes( length );
        }
        
        /// <summary>
        /// Writes length delimited byte array
        /// </summary>
        public static void WriteBytes( ref BufferStream stream, byte[] val )
        {
            WriteUInt32( ref stream, (uint)val.Length );
            new Span<byte>( val ).CopyTo( stream.Bytes( val.Length ) );
        }

        public static void WritePooledBytes( ref BufferStream stream, ArraySegment<byte> segment )
        {
            if (segment.Array == null)
            {
                WriteUInt32( ref stream, 0 );
                return;
            }

            WriteUInt32( ref stream, (uint)segment.Count );
            new Span<byte>( segment.Array, segment.Offset, segment.Count ).CopyTo( stream.Bytes( segment.Count ) );
        }
    }
}

public static class ProtoStreamExtensions
{
    public static void WriteToStream(this SilentOrbit.ProtocolBuffers.IProto proto, Stream stream, int maxLength = 1 * 1024 * 1024)
    {
        if (proto == null)
        {
            throw new ArgumentNullException(nameof(proto));
        }

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var buffer = ProtocolParser.ArrayPool.Rent(maxLength);
        var writer = new BufferStream(buffer);
        proto.WriteToStream(ref writer);
        stream.Write(buffer, 0, writer.Position);
        ProtocolParser.ArrayPool.Return(buffer);
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
        var reader = new BufferStream(bytes);
        proto.ReadFromStream(ref reader, isDelta);
        Facepunch.Pool.FreeMemoryStream(ref ms);
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

        var buffer = ProtocolParser.ArrayPool.Rent(4096);
        var ms = Facepunch.Pool.Get<MemoryStream>();
        var remaining = length;
        while (remaining > 0)
        {
            var bytesRead = stream.Read(buffer, 0, Math.Min(remaining, buffer.Length));
            if (bytesRead <= 0)
            {
                throw new InvalidOperationException("Unexpected end of stream");
            }

            remaining -= bytesRead;
            ms.Write(buffer, 0, bytesRead);
        }
        ProtocolParser.ArrayPool.Return(buffer);
        
        var bytes = ms.ToArray();
        var reader = new BufferStream(bytes);
        proto.ReadFromStream(ref reader, isDelta);
        Facepunch.Pool.FreeMemoryStream(ref ms);
    }
    
    public static byte[] ToProtoBytes(this SilentOrbit.ProtocolBuffers.IProto proto, int maxLength = 1 * 1024 * 1024)
    {
        if (proto == null)
        {
            throw new ArgumentNullException(nameof(proto));
        }

        var buffer = ProtocolParser.ArrayPool.Rent(maxLength);
        var writer = new BufferStream(buffer);
        proto.WriteToStream(ref writer);
        var bytes = new byte[writer.Position];
        Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
        ProtocolParser.ArrayPool.Return(buffer);
        return bytes;
    }
}
