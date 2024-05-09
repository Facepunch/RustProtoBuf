//
//  Reader/Writer for field key
//
using System;
using System.IO;

namespace SilentOrbit.ProtocolBuffers
{
    public enum Wire
    {
        Varint = 0,          //int32, int64, UInt32, UInt64, SInt32, SInt64, bool, enum
        Fixed64 = 1,         //fixed64, sfixed64, double
        LengthDelimited = 2, //string, bytes, embedded messages, packed repeated fields
        //Start = 3,         //  groups (deprecated)
        //End = 4,           //  groups (deprecated)
        Fixed32 = 5,         //32-bit    fixed32, SFixed32, float
    }

    public struct Key
    {
        public uint Field { get; set; }

        public Wire WireType { get; set; }

        public Key(uint field, Wire wireType)
        {
            this.Field = field;
            this.WireType = wireType;
        }

        public override string ToString()
        {
            return string.Format("[Key: {0}, {1}]", Field, WireType);
        }
    }

    public static partial class ProtocolParser
    {

        public static Key ReadKey(BufferStream stream)
        {
            uint n = ReadUInt32(stream);
            return new Key(n >> 3, (Wire)(n & 0x07));
        }

        public static Key ReadKey(byte firstByte, BufferStream stream)
        {
            if (firstByte < 128)
                return new Key((uint)(firstByte >> 3), (Wire)(firstByte & 0x07));
            uint fieldID = ((uint)ReadUInt32(stream) << 4) | ((uint)(firstByte >> 3) & 0x0F);
            return new Key(fieldID, (Wire)(firstByte & 0x07));
        }

        public static void WriteKey(BufferStream stream, Key key)
        {
            uint n = (key.Field << 3) | ((uint)key.WireType);
            WriteUInt32(stream, n);
        }

        /// <summary>
        /// Seek past the value for the previously read key.
        /// </summary>
        public static void SkipKey(BufferStream stream, Key key)
        {
            switch (key.WireType)
            {
                case Wire.Fixed32:
                    stream.Skip(4);
                    return;
                case Wire.Fixed64:
                    stream.Skip(8);
                    return;
                case Wire.LengthDelimited:
                    stream.Skip((int)ProtocolParser.ReadUInt32(stream));
                    return;
                case Wire.Varint:
                    ProtocolParser.ReadSkipVarInt(stream);
                    return;
                default:
                    throw new NotImplementedException("Unknown wire type: " + key.WireType);
            }
        }
    }
}

