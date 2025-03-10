using System;
using System.IO;
using System.Text;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Facepunch.Extend;
using UnityEngine.Profiling;

namespace SilentOrbit.ProtocolBuffers
{
    public interface IProto
    {
        void WriteToStream( Stream stream );
        void ReadFromStream( Stream stream, int size, bool isDelta = false );
    }

    public static partial class ProtocolParser
    {
        // Thread static does not support intializers use threadlocal and access using staticBuffer.Value
        private static ThreadLocal<byte[]> staticBuffer = new ThreadLocal<byte[]>(() => new byte[ 1024 * 128 ]);

        public static float ReadSingle( Stream stream )
        {
            if (stream is IStreamReader reader)
            {
                return reader.Float();
            }

            stream.Read( staticBuffer.Value, 0, 4 );
            return staticBuffer.Value.ReadUnsafe<float>();
        }

        public static void WriteSingle( Stream stream, float f )
        {
            if (stream is IStreamWriter writer)
            {
                writer.Float( f );
                return;
            }

            staticBuffer.Value.WriteUnsafe( f );
            stream.Write( staticBuffer.Value, 0, 4 );
        }

        public static double ReadDouble( Stream stream )
        {
            if (stream is IStreamReader reader)
            {
                return reader.Double();
            }

            stream.Read( staticBuffer.Value, 0, 8 );
            return staticBuffer.Value.ReadUnsafe<double>();
        }

        public static void WriteDouble( Stream stream, double f )
        {
            if (stream is IStreamWriter writer)
            {
                writer.Double( f );
                return;
            }

            staticBuffer.Value.WriteUnsafe( f );
            stream.Write( staticBuffer.Value, 0, 8 );
        }

        public static string ReadString( Stream stream )
        {
            if (stream is IStreamReader reader)
            {
                return reader.StringRaw( NetworkDefines.MaxNetReadPacketSize, variableLength: true );
            }

            Profiler.BeginSample( "ProtoParser.ReadString" );

            // Only limit length when reading from network
            int length = (int)ReadUInt32( stream );

            string str;

            if (length >= staticBuffer.Value.Length)
            {
                Profiler.BeginSample( "new Buffer" );
                byte[] buffer = new byte[ length ];
                Profiler.EndSample();
                stream.Read( buffer, 0, length );
                str = Encoding.UTF8.GetString( buffer, 0, length );
            }
            else
            {
                stream.Read( staticBuffer.Value, 0, length );
                str = Encoding.UTF8.GetString( staticBuffer.Value, 0, length );
            }

            Profiler.EndSample();

            return str;
        }

        public static void WriteString( Stream stream, string val )
        {
            if (stream is IStreamWriter writer)
            {
                writer.String( val, variableLength: true );
                return;
            }

            Profiler.BeginSample( "ProtoParser.WriteString" );
            var len = Encoding.UTF8.GetBytes( val, 0, val.Length, staticBuffer.Value, 0 );

            WriteUInt32( stream, (uint)len );
            stream.Write( staticBuffer.Value, 0, len );
            Profiler.EndSample();
        }

        /// <summary>
        /// Reads a length delimited byte array into a new byte[]
        /// </summary>
        public static byte[] ReadBytes( Stream stream )
        {
            if (stream is IStreamReader reader)
            {
                return reader.BytesWithSize( NetworkDefines.MaxNetReadPacketSize, variableLength: true );
            }

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
        public static ArraySegment<byte> ReadPooledBytes( Stream stream )
        {
            if (stream is IStreamReader reader)
            {
                return reader.PooledBytes( NetworkDefines.MaxNetReadPacketSize, variableLength: true );
            }

            Profiler.BeginSample( "ProtoParser.ReadPooledBytes" );

            // Only limit length when reading from network
            int length = (int)ReadUInt32( stream );

            //Bytes
            byte[] buffer = ArrayPool<byte>.Shared.Rent( length );
            ReadBytesInto( stream, buffer, length );
            Profiler.EndSample();

            return new ArraySegment<byte>( buffer, 0, length );
        }

        private static void ReadBytesInto( Stream stream, byte[] buffer, int length )
        {
            int read = 0;
            while (read < length)
            {
                int r = stream.Read( buffer, read, length - read );
                if (r == 0)
                    throw new ProtocolBufferException( "Expected " + (length - read) + " got " + read );
                read += r;
            }
        }

        /// <summary>
        /// Skip the next varint length prefixed bytes.
        /// Alternative to ReadBytes when the data is not of interest.
        /// </summary>
        public static void SkipBytes( Stream stream )
        {
            int length = (int)ReadUInt32( stream );
            if (stream.CanSeek)
                stream.Seek( length, SeekOrigin.Current );
            else
                ReadBytes( stream );
        }

        // We don't need IStreamWriter down here because they simply copy bytes into a steam rather than use the temp buffer

        /// <summary>
        /// Writes length delimited byte array
        /// </summary>
        public static void WriteBytes( Stream stream, byte[] val )
        {
            WriteUInt32( stream, (uint)val.Length );
            stream.Write( val, 0, val.Length );
        }

        public static void WritePooledBytes( Stream stream, ArraySegment<byte> segment )
        {
            if (segment.Array == null)
            {
                WriteUInt32( stream, 0 );
                return;
            }

            WriteUInt32( stream, (uint)segment.Count );
            stream.Write( segment.Array, segment.Offset, segment.Count );
        }
    }
}
