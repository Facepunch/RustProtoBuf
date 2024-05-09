using System;
using System.IO;

namespace SilentOrbit.ProtocolBuffers
{
    public static partial class ProtocolParser
    {
        /// <summary>
        /// Reads past a varint for an unknown field.
        /// </summary>
        public static void ReadSkipVarInt(BufferStream stream)
        {
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                    throw new IOException("Stream ended too early");

                if ((b & 0x80) == 0)
                    return; //end of varint
            }
        }

        /// <summary>
        /// Unsigned VarInt format
        /// Do not use to read int32, use ReadUint64 for that.
        /// </summary>
        public static uint ReadUInt32( Span<byte> array, int pos, out int length )
        {
            int b;
            uint val = 0;
            length = 0;

            for (int n = 0; n < 5; n++)
            {
                length++;

                if (pos >= array.Length)
                {
                    break;
                }
                b = array[ pos++ ];
                if (b < 0)
                    throw new IOException( "Stream ended too early" );

                //Check that it fits in 32 bits
                if ((n == 4) && (b & 0xF0) != 0)
                    throw new ProtocolBufferException( "Got larger VarInt than 32bit unsigned" );
                //End of check

                if ((b & 0x80) == 0)
                    return val | (uint)b << (7 * n);

                val |= (uint)(b & 0x7F) << (7 * n);
            }

            throw new ProtocolBufferException( "Got larger VarInt than 32bit unsigned" );
        }

        /// <summary>
        /// Unsigned VarInt format
        /// </summary>
        public static int WriteUInt32( uint val, Span<byte> array, int pos )
        {
            int length = 0;
            byte b;
            while (pos < array.Length)
            {
                length++;
                b = (byte)(val & 0x7F);
                val = val >> 7;
                if (val == 0)
                {
                    array[ pos++ ] = b;
                    break;
                }
                else
                {
                    b |= 0x80;
                    array[ pos++ ] = b;
                }
            }
            return length;
        }

        #region VarInt: int32, uint32, sint32

        /// <summary>
        /// Zig-zag signed VarInt format
        /// </summary>
        public static int ReadZInt32(BufferStream stream)
        {
            uint val = ReadUInt32(stream);
            return (int)(val >> 1) ^ ((int)(val << 31) >> 31);
        }

        /// <summary>
        /// Zig-zag signed VarInt format
        /// </summary>
        public static void WriteZInt32(BufferStream stream, int val)
        {
            WriteUInt32(stream, (uint)((val << 1) ^ (val >> 31)));
        }

        /// <summary>
        /// Unsigned VarInt format
        /// Do not use to read int32, use ReadUint64 for that.
        /// </summary>
        public static uint ReadUInt32(BufferStream stream)
        {
            int b;
            uint val = 0;

            for (int n = 0; n < 5; n++)
            {
                b = stream.ReadByte();
                if (b < 0)
                    throw new IOException("Stream ended too early");

                //Check that it fits in 32 bits
                if ((n == 4) && (b & 0xF0) != 0)
                    throw new ProtocolBufferException("Got larger VarInt than 32bit unsigned");
                //End of check

                if ((b & 0x80) == 0)
                    return val | (uint)b << (7 * n);

                val |= (uint)(b & 0x7F) << (7 * n);
            }

            throw new ProtocolBufferException("Got larger VarInt than 32bit unsigned");
        }

        /// <summary>
        /// Unsigned VarInt format
        /// </summary>
        public static void WriteUInt32(BufferStream stream, uint val)
        {
            byte b;
            while (true)
            {
                b = (byte)(val & 0x7F);
                val = val >> 7;
                if (val == 0)
                {
                    stream.WriteByte(b);
                    break;
                }
                else
                {
                    b |= 0x80;
                    stream.WriteByte(b);
                }
            }
        }

        #endregion

        #region VarInt: int64, UInt64, SInt64

        /// <summary>
        /// Zig-zag signed VarInt format
        /// </summary>
        public static long ReadZInt64(BufferStream stream)
        {
            ulong val = ReadUInt64(stream);
            return (long)(val >> 1) ^ ((long)(val << 63) >> 63);
        }

        /// <summary>
        /// Zig-zag signed VarInt format
        /// </summary>
        public static void WriteZInt64(BufferStream stream, long val)
        {
            WriteUInt64(stream, (ulong)((val << 1) ^ (val >> 63)));
        }

        /// <summary>
        /// Unsigned VarInt format
        /// </summary>
        public static ulong ReadUInt64(BufferStream stream)
        {
            int b;
            ulong val = 0;

            for (int n = 0; n < 10; n++)
            {
                b = stream.ReadByte();
                if (b < 0)
                    throw new IOException("Stream ended too early");

                //Check that it fits in 64 bits
                if ((n == 9) && (b & 0xFE) != 0)
                    throw new ProtocolBufferException("Got larger VarInt than 64 bit unsigned");
                //End of check

                if ((b & 0x80) == 0)
                    return val | (ulong)b << (7 * n);

                val |= (ulong)(b & 0x7F) << (7 * n);
            }

            throw new ProtocolBufferException("Got larger VarInt than 64 bit unsigned");
        }

        /// <summary>
        /// Unsigned VarInt format
        /// </summary>
        public static void WriteUInt64(BufferStream stream, ulong val)
        {
            byte b;
            while (true)
            {
                b = (byte)(val & 0x7F);
                val = val >> 7;
                if (val == 0)
                {
                    stream.WriteByte(b);
                    break;
                }
                else
                {
                    b |= 0x80;
                    stream.WriteByte(b);
                }
            }
        }

        /// <summary>
        /// Unsigned VarInt format
        /// </summary>
        public static int WriteUInt64(ulong val, byte[] buffer, int pos)
        {
            int len = 0;
            while (true)
            {
                len++;
                byte b = (byte)(val & 0x7F);
                val = val >> 7;
                if (val == 0)
                {
                    buffer[pos] = b;
                    break;
                }
                else
                {
                    b |= 0x80;
                    buffer[pos++] = b;
                }
            }
            return len;
        }

        #endregion

        #region Varint: bool

        public static bool ReadBool(BufferStream stream)
        {
            int b = stream.ReadByte();
            if (b < 0)
                throw new IOException("Stream ended too early");
            if (b == 1)
                return true;
            if (b == 0)
                return false;
            throw new ProtocolBufferException("Invalid boolean value");
        }

        public static void WriteBool(BufferStream stream, bool val)
        {
            stream.WriteByte(val ? (byte)1 : (byte)0);
        }

        #endregion
    }
}
