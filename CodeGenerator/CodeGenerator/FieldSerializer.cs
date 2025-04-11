using System;
using System.Collections.Generic;
using SilentOrbit.Code;

namespace SilentOrbit.ProtocolBuffers
{
    static class FieldSerializer
    {
        #region Reader

        /// <summary>
        /// Return true for normal code and false if generated thrown exception.
        /// In the latter case a break is not needed to be generated afterwards.
        /// </summary>
        public static bool FieldReader(Field f, CodeWriter cw)
        {
            if (f.Rule == FieldRule.Repeated)
            {
                //Make sure we are not reading a list of interfaces
                if (f.ProtoType.OptionType == "interface")
                {
                    cw.WriteLine("throw new NotSupportedException(\"Can't deserialize a list of interfaces\");");
                    return false;
                }

                if (f.OptionPacked == true)
                {
                    cw.Comment("repeated packed");
                    cw.WriteLine("long end" + f.ID + " = global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadUInt32(stream);");
                    cw.WriteLine("end" + f.ID + " += stream.Position;");
                    cw.WhileBracket("stream.Position < end" + f.ID);
                    cw.WriteLine("instance." + f.CsName + ".Add(" + FieldReaderType(f, "stream", null) + ");");
                    cw.EndBracket();

                    cw.WriteLine("if (stream.Position != end" + f.ID + ")");
                    cw.WriteIndent("throw new global::SilentOrbit.ProtocolBuffers.ProtocolBufferException(\"Read too many bytes in packed data\");");
                }
                else
                {
                    cw.Comment("repeated");

                    // note: can only use 'repeated packed' for primitives so this code path doesn't need to be duplicated above
                    if (f.ProtoType is ProtoMessage && f.ProtoType.OptionType == "struct")
                    {
                        cw.WriteLine( "{" );
                        cw.WriteIndent( $"var a = default( {f.ProtoType.FullCsType} );" );
                        cw.WriteIndent( $"{FieldReaderType(f, "stream", "ref a")};" );
                        cw.WriteIndent( $"instance.{f.CsName}.Add( a );" );
                        cw.WriteLine( "}" );
                    }
                    else
                    {
                        cw.WriteLine("instance." + f.CsName + ".Add(" + FieldReaderType(f, "stream", null) + ");");
                    }
                }
            }
            else
            {
                if (f.OptionReadOnly)
                {
                    //The only "readonly" fields we can modify
                    //We could possibly support bytes primitive too but it would require the incoming length to match the wire length
                    if (f.ProtoType is ProtoMessage)
                    {
                        cw.WriteLine(FieldReaderType(f, "stream", "instance." + f.CsName) + ";");
                        return true;
                    }
                    cw.WriteLine("throw new InvalidOperationException(\"Can't deserialize into a readonly primitive field\");");
                    return false;
                }

                if (f.ProtoType is ProtoMessage)
                {
                    if ( f.ProtoType.OptionType == "struct")
                    {
						if ( f.OptionUseReferences )
						{
							cw.WriteLine( FieldReaderType( f, "stream", "ref instance." + f.CsName ) + ";" );
						}
						else
						{
							cw.WriteLine( "{" );
							cw.WriteIndent( "var a = instance." + f.CsName + ";" );
							cw.WriteIndent( "instance." + f.CsName + " = " + FieldReaderType( f, "stream", "ref a" ) + ";" );
							cw.WriteLine( "}" );
						}
                        
                        return true;
                    }

                    cw.WriteLine("if (instance." + f.CsName + " == null)");
                    if (f.ProtoType.OptionType == "interface")
                        cw.WriteIndent("throw new InvalidOperationException(\"Can't deserialize into a interfaces null pointer\");");
                    else
                        cw.WriteIndent("instance." + f.CsName + " = " + FieldReaderType(f, "stream", null) + ";");
                    cw.WriteLine("else");
                    cw.WriteIndent(FieldReaderType(f, "stream", "instance." + f.CsName) + ";");
                    return true;
                }

                cw.WriteLine("instance." + f.CsName + " = " + FieldReaderType(f, "stream", "instance." + f.CsName) + ";");
            }
            return true;
        }

        /// <summary>
        /// Read a primitive from the stream
        /// </summary>
        static string FieldReaderType(Field f, string stream, string instance)
        {
            if (f.OptionCodeType != null)
            {
                switch (f.OptionCodeType)
                {
                    case "DateTime":
                        switch (f.ProtoType.ProtoName)
                        {
                            case ProtoBuiltin.UInt64:
                            case ProtoBuiltin.Int64:
                            case ProtoBuiltin.Fixed64:
                            case ProtoBuiltin.SFixed64:
                                return "new DateTime((long)" + FieldReaderPrimitive(f, stream, instance) + ")";
                        }
                        throw new ProtoFormatException("Local feature, DateTime, must be stored in a 64 bit field", f.Source);

                    case "TimeSpan":
                        switch (f.ProtoType.ProtoName)
                        {
                            case ProtoBuiltin.UInt64:
                            case ProtoBuiltin.Int64:
                            case ProtoBuiltin.Fixed64:
                            case ProtoBuiltin.SFixed64:
                                return "new TimeSpan((long)" + FieldReaderPrimitive(f, stream, instance) + ")";
                        }
                        throw new ProtoFormatException("Local feature, TimeSpan, must be stored in a 64 bit field", f.Source);

                    default:
                        //Assume enum
                        return "(" + f.OptionCodeType + ")" + FieldReaderPrimitive(f, stream, instance);
                }
            }

            return FieldReaderPrimitive(f, stream, instance);
        }

        static string FieldReaderPrimitive(Field f, string stream, string instance)
        {
            if (f.ProtoType is ProtoMessage)
            {
                var m = f.ProtoType as ProtoMessage;
                if ((f.Rule == FieldRule.Repeated && f.ProtoType.OptionType != "struct") || instance == null)
                    return m.FullSerializerType + ".DeserializeLengthDelimited(" + stream + ")";
                else
                    return m.FullSerializerType + ".DeserializeLengthDelimited(" + stream + ", " + instance + ", isDelta )";
            }

            if (f.ProtoType is ProtoEnum)
                return "(" + f.ProtoType.FullCsType + ")global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadUInt64(" + stream + ")";

            if (f.ProtoType is ProtoBuiltin)
            {
                switch (f.ProtoType.ProtoName)
                {
                    case ProtoBuiltin.Double:
                        //return binaryReader + ".ReadDouble()";
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadDouble(" + stream + ")";
                    case ProtoBuiltin.Float:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadSingle(" + stream + ")";
                       // return binaryReader + ".ReadSingle()";
                    case ProtoBuiltin.Int32: //Wire format is 64 bit varint
                        return "(int)global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadUInt64(" + stream + ")";
                    case ProtoBuiltin.Int64:
                        return "(long)global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadUInt64(" + stream + ")";
                    case ProtoBuiltin.UInt32:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadUInt32(" + stream + ")";
                    case ProtoBuiltin.UInt64:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadUInt64(" + stream + ")";
                    case ProtoBuiltin.SInt32:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadZInt32(" + stream + ")";
                    case ProtoBuiltin.SInt64:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadZInt64(" + stream + ")";
                    case ProtoBuiltin.Fixed32:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadSingle(" + stream + ")";
                    case ProtoBuiltin.Fixed64:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadDouble(" + stream + ")";
                    case ProtoBuiltin.SFixed32:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadFixedInt32(" + stream + ")";
                    case ProtoBuiltin.SFixed64:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadFixedInt64(" + stream + ")";
                    case ProtoBuiltin.Bool:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadBool(" + stream + ")";
                    case ProtoBuiltin.String:
                        return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadString(" + stream + ")";
                    case ProtoBuiltin.Bytes:
                        return f.OptionPooled
                            ? "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadPooledBytes(" + stream + ")"
                            : "global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadBytes(" + stream + ")";
                    case ProtoBuiltin.NetworkableId:
                    case ProtoBuiltin.ItemContainerId:
                    case ProtoBuiltin.ItemId:
                        return $"new {f.ProtoType.FullCsType}(global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadUInt64({stream}))";
                    default:
                        throw new ProtoFormatException("unknown built in: " + f.ProtoType.ProtoName, f.Source);
                }

            }

            throw new NotImplementedException();
        }

        #endregion

        #region Writer

        static void KeyWriter(string stream, int id, Wire wire, CodeWriter cw)
        {
            uint n = ((uint)id << 3) | ((uint)wire);
            cw.Comment("Key for field: " + id + ", " + wire);
            //cw.WriteLine("ProtocolParser.WriteUInt32(" + stream + ", " + n + ");");
            VarintWriter(stream, n, cw);
        }

        /// <summary>
        /// Generates writer for a varint value known at compile time
        /// </summary>
        static void VarintWriter(string stream, uint value, CodeWriter cw)
        {
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                value = value >> 7;
                if (value == 0)
                {
                    cw.WriteLine(stream + ".WriteByte(" + b + ");");
                    break;
                }

                //Write part of value
                b |= 0x80;
                cw.WriteLine(stream + ".WriteByte(" + b + ");");
            }
        }

        /// <summary>
        /// Generates inline writer of a length delimited byte array
        /// </summary>
        static void BytesWriter(Field f, string stream, CodeWriter cw)
        {
            cw.Comment("Length delimited byte array");

            //Original
            //cw.WriteLine("ProtocolParser.WriteBytes(" + stream + ", " + memoryStream + ".ToArray());");

            //Much slower than original
            /*
            cw.WriteLine("ProtocolParser.WriteUInt32(" + stream + ", (uint)" + memoryStream + ".Length);");
            cw.WriteLine(memoryStream + ".Seek(0, System.IO.SeekOrigin.Begin);");
            cw.WriteLine(memoryStream + ".CopyTo(" + stream + ");");
            */

            //Same speed as original
            /*
            cw.WriteLine("ProtocolParser.WriteUInt32(" + stream + ", (uint)" + memoryStream + ".Length);");
            cw.WriteLine(stream + ".Write(" + memoryStream + ".ToArray(), 0, (int)" + memoryStream + ".Length);");
            */

            //10% faster than original using GetBuffer rather than ToArray
            cw.WriteLine("uint length" + f.ID + " = (uint)msField.Length;");
            cw.WriteLine("global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteUInt32(" + stream + ", length" + f.ID + ");");
            cw.WriteLine(stream + ".Write(msField.GetBuffer(), 0, (int)length" + f.ID + ");");
        }

        private static void WriteOptionalFieldCheck( CodeWriter cw, Field f )
        {
            // Support fields with custom default values
            string defaultString = f.OptionDefault ?? "default";

            // For some reason Ray isn't comparible so just check it's fields 
            if (f.ProtoTypeName == "Ray")
            {
                cw.IfBracket( $"instance.{f.CsName}.origin != default && instance.{f.CsName}.direction != default" );
            }
            else
            {
                // Normally compare to default 
                cw.IfBracket( $"instance.{f.CsName} != {defaultString}" );
            }
        }

        /// <summary>
        /// Generates code for writing one field
        /// </summary>
        public static void FieldWriter(ProtoMessage m, Field f, CodeWriter cw, bool hasPrevious = false )
        {
            var canDelta = f.ProtoType.OptionDeltaCompare;
            if ( f.ProtoType.ProtoName == ProtoBuiltin.String ) canDelta = true;
            if ( f.ProtoType.ProtoName == ProtoBuiltin.Float ) canDelta = true;
            if ( f.ProtoType.ProtoName == ProtoBuiltin.Fixed32 ) canDelta = true;
            if ( f.ProtoType.ProtoName == ProtoBuiltin.Int32 ) canDelta = true;
            if ( f.ProtoType.ProtoName == ProtoBuiltin.UInt32 ) canDelta = true;
            if ( f.ProtoType.ProtoName == ProtoBuiltin.UInt64 ) canDelta = true;
            if ( f.ProtoType.ProtoName == ProtoBuiltin.Double ) canDelta = true;

            // Don't change behavior of SerializeDelta() calls
            // Don't skip enums when default values because their default may be awkward
            bool canOptional = !hasPrevious && !(f.ProtoType is ProtoEnum);

            if (f.Rule == FieldRule.Repeated)
            {
                if (f.OptionPacked == true)
                {
                    //Repeated packed
                    cw.IfBracket("instance." + f.CsName + " != null");

                    KeyWriter("stream", f.ID, Wire.LengthDelimited, cw);
                    if (f.ProtoType.WireSize < 0)
                    {
                        //Un-optimized, unknown size
                        cw.WriteLine("msField.SetLength(0);");
                        if ( f.IsUsingBinaryWriter )
                            throw new System.NotSupportedException();
                            //cw.WriteLine("BinaryWriter bw" + f.ID + " = new BinaryWriter(ms" + f.ID + ");");

                        cw.ForeachBracket( "i" + f.ID, "instance." + f.CsName );
                        cw.WriteLine(FieldWriterType(f, "msField", "i" + f.ID, hasPrevious ) );
                        cw.EndBracket();

                        BytesWriter(f, "stream", cw);
                    }
                    else
                    {
                        //Optimized with known size
                        //No memorystream buffering, write size first at once

                        //For constant size messages we can skip serializing to the MemoryStream
                        cw.WriteLine("global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteUInt32(stream, " + f.ProtoType.WireSize + "u * (uint)instance." + f.CsName + ".Count);");

                        cw.ForeachBracket( "i" + f.ID, "instance." + f.CsName );
                        cw.WriteLine(FieldWriterType(f, "stream", "i" + f.ID, hasPrevious ) );
                        cw.EndBracket();
                    }
                    cw.EndBracket();
                }
                else
                {
                    //Repeated not packet
                    cw.IfBracket("instance." + f.CsName + " != null");
                    cw.ForeachBracket( "i" + f.ID, "instance." + f.CsName );
                    KeyWriter("stream", f.ID, f.ProtoType.WireType, cw);
                    cw.WriteLine(FieldWriterType(f, "stream", "i" + f.ID, hasPrevious ) );
                    cw.EndBracket();
                    cw.EndBracket();
                }
                return;
            }
            else if (f.Rule == FieldRule.Optional)
            {
                if (f.ProtoType is ProtoMessage ||
                    f.ProtoType.ProtoName == ProtoBuiltin.String ||
                    f.ProtoType.ProtoName == ProtoBuiltin.Bytes)
                {
                    if (f.ProtoType.Nullable) //Struct always exist, not optional
                    {
                        // Manual null checks above = don't do default value checks
                        canOptional = false;

                        if (f.ProtoType.ProtoName == ProtoBuiltin.Bytes && f.OptionPooled)
                            cw.IfBracket("instance." + f.CsName + ".Array != null");
                        else
                            cw.IfBracket("instance." + f.CsName + " != null");
                    }

                    if (canOptional)
                    {
                        WriteOptionalFieldCheck( cw, f );
                    }

                    if ( hasPrevious && canDelta )
                        cw.IfBracket( "instance." + f.CsName + " != previous." + f.CsName );

                    KeyWriter("stream", f.ID, f.ProtoType.WireType, cw);
                    cw.WriteLine(FieldWriterType(f, "stream", "instance." + f.CsName, hasPrevious ) );
                    if (f.ProtoType.Nullable) //Struct always exist, not optional
                        cw.EndBracket();

                    if ( hasPrevious && canDelta ) 
                        cw.EndBracket();

                    if (canOptional)
                        cw.EndBracket();
                    return;
                }
                if (f.ProtoType is ProtoEnum)
                {
                    if (f.OptionDefault != null)
                        cw.IfBracket("instance." + f.CsName + " != " + f.ProtoType.CsType + "." + f.OptionDefault);
                    if ( canOptional)
                    {
                        WriteOptionalFieldCheck( cw, f );
                    }
                    KeyWriter("stream", f.ID, f.ProtoType.WireType, cw);
                    cw.WriteLine(FieldWriterType(f, "stream", "instance." + f.CsName, hasPrevious ) );
                    if (f.OptionDefault != null)
                        cw.EndBracket();
                    if (canOptional)
                        cw.EndBracket();
                    return;
                }

                if ( hasPrevious && canDelta )
                    cw.IfBracket( "instance." + f.CsName + " != previous." + f.CsName );

                if ( canOptional)
                {
                    WriteOptionalFieldCheck( cw, f );
                }

                KeyWriter("stream", f.ID, f.ProtoType.WireType, cw);
                cw.WriteLine(FieldWriterType(f, "stream", "instance." + f.CsName, hasPrevious ) );

                if ( hasPrevious && canDelta ) 
                    cw.EndBracket();
                if (canOptional)
                    cw.EndBracket();
                return;
            }
            else if (f.Rule == FieldRule.Required)
            {
                if ( hasPrevious && canDelta ) 
                    cw.IfBracket( "instance." + f.CsName + " != previous." + f.CsName );

                if (f.ProtoType is ProtoMessage && f.ProtoType.OptionType != "struct" ||
                    f.ProtoType.ProtoName == ProtoBuiltin.String ||
                    f.ProtoType.ProtoName == ProtoBuiltin.Bytes)
                {
                    // Don't do extra optional checks if it's trying to throw an error
                    canOptional = false;

                    if (f.ProtoType.ProtoName == ProtoBuiltin.Bytes && f.OptionPooled)
                        cw.WriteLine("if (instance." + f.CsName + ".Array == null)");
                    else
                        cw.WriteLine("if (instance." + f.CsName + " == null)");

                    cw.WriteIndent("throw new ArgumentNullException(\"" + f.CsName + "\", \"Required by proto specification.\");");
                }
                
                // Skip default values of fields
                if ( canOptional)
                {
                    WriteOptionalFieldCheck( cw, f );
                }
                KeyWriter("stream", f.ID, f.ProtoType.WireType, cw);
                cw.WriteLine(FieldWriterType(f, "stream", "instance." + f.CsName, hasPrevious ) );

                if ( hasPrevious && canDelta )
                    cw.EndBracket();

                if ( canOptional )
                    cw.EndBracket();

                return;
            }
            throw new NotImplementedException("Unknown rule: " + f.Rule);
        }

        static string FieldWriterType(Field f, string stream, string instance, bool hasPrevious )
        {
            if (f.OptionCodeType != null)
            {
                switch (f.OptionCodeType)
                {
                    case "DateTime":
                    case "TimeSpan":
                        return FieldWriterPrimitive(f, stream, instance + ".Ticks", hasPrevious );
                    default: //enum
                        break;
                }
            }
            return FieldWriterPrimitive(f, stream, instance, hasPrevious );
        }

        static string FieldWriterPrimitive(Field f, string stream, string instance, bool hasPrevious )
        {
            if (f.ProtoType is ProtoEnum)
                return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteUInt64(" + stream + ",(ulong)" + instance + ");";

            if (f.ProtoType is ProtoMessage pm)
            {
                CodeWriter cw = new CodeWriter();

                var maxSize = f.ProtoType.MaximumWireSize;
                if (maxSize < 0) maxSize = int.MaxValue - 1;
                var lengthByteCount = (int)Math.Ceiling(Math.Ceiling(Math.Log(maxSize + 1, 2) / 7));
                cw.WriteLine($"// maxSize = {maxSize}, bytes={lengthByteCount}");

                // reserve a spot for the length prefix, padding out the end in case the length is shorter 
                cw.WriteLine($"var lengthRange{f.ID} = {stream}.GetRange({lengthByteCount});");
                cw.WriteLine($"var startPos{f.ID} = {stream}.Position;");
                
                if ( hasPrevious )
                    cw.WriteLine($"{pm.FullSerializerType}.SerializeDelta({stream}, {instance}, {instance.Replace("instance.", "previous.")} );");
                else 
                    cw.WriteLine($"{pm.FullSerializerType}.Serialize({stream}, {instance});");

                cw.WriteLine($"var lengthValue{f.ID} = {stream}.Position - startPos{f.ID};");
                if (maxSize != int.MaxValue - 1)
                {
                    cw.IfBracket($"lengthValue{f.ID} > {(int)(Math.Ceiling(Math.Pow(2, lengthByteCount * 7))) - 1}");
                    cw.WriteLine($"throw new InvalidOperationException(\"Not enough space was reserved for the length prefix of field {f.CsName} ({pm.FullCsType})\");");
                    cw.EndBracket();
                }

                cw.WriteLine($"var lengthSpan{f.ID} = lengthRange{f.ID}.GetSpan();");
                cw.WriteLine($"var writtenBytes{f.ID} = global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteUInt32((uint)lengthValue{f.ID}, lengthSpan{f.ID}, 0);");

                if (lengthByteCount > 1)
                {
                    cw.IfBracket($"writtenBytes{f.ID} < {lengthByteCount}");
                    
                    if (lengthByteCount == 2)
                    {
                        cw.WriteLine($"lengthSpan{f.ID}[0] |= 0x80;");
                        cw.WriteLine($"lengthSpan{f.ID}[1] = 0x00;");
                    }
                    else
                    {
                        cw.WriteLine($"lengthSpan{f.ID}[writtenBytes{f.ID} - 1] |= 0x80;");
                        cw.WriteLine($"while (writtenBytes{f.ID} < {lengthByteCount - 1}) lengthSpan{f.ID}[writtenBytes{f.ID}++] = 0x80;");
                        cw.WriteLine($"lengthSpan{f.ID}[{lengthByteCount - 1}] = 0x00;");
                    }
                    
                    cw.EndBracket();
                }
                
                return cw.Code;
            }

            switch (f.ProtoType.ProtoName)
            {
                case ProtoBuiltin.Double:
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteDouble(" + stream + ", " + instance + ");";
                case ProtoBuiltin.Float:
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteSingle(" + stream + ", " + instance + ");";
                case ProtoBuiltin.Fixed32:
                case ProtoBuiltin.Fixed64:
                case ProtoBuiltin.SFixed32:
                case ProtoBuiltin.SFixed64:
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteUnimplemented(" + instance + ");";
                case ProtoBuiltin.Int32: //Serialized as 64 bit varint
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteUInt64(" + stream + ",(ulong)" + instance + ");";
                case ProtoBuiltin.Int64:
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteUInt64(" + stream + ",(ulong)" + instance + ");";
                case ProtoBuiltin.UInt32:
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteUInt32(" + stream + ",(uint)" + instance + ");";
                case ProtoBuiltin.UInt64:
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteUInt64(" + stream + ", " + instance + ");";
                case ProtoBuiltin.SInt32:
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteZInt32(" + stream + ", " + instance + ");";
                case ProtoBuiltin.SInt64:
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteZInt64(" + stream + ", " + instance + ");";
                case ProtoBuiltin.Bool:
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteBool(" + stream + ", " + instance + ");";
                case ProtoBuiltin.String:
                    return "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteString(" + stream + ", " + instance + " );";
                case ProtoBuiltin.Bytes:
                    return f.OptionPooled
                        ? "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WritePooledBytes(" + stream + ", " + instance + ");"
                        : "global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteBytes(" + stream + ", " + instance + ");";
                case ProtoBuiltin.NetworkableId:
                case ProtoBuiltin.ItemContainerId:
                case ProtoBuiltin.ItemId:
                    return $"global::SilentOrbit.ProtocolBuffers.ProtocolParser.WriteUInt64({stream}, {instance}.Value);";
            }

            throw new NotImplementedException();
        }

        #endregion
    }
}

