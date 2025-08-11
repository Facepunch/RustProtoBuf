using System;
using SilentOrbit.Code;

namespace SilentOrbit.ProtocolBuffers
{
    static class MessageSerializer
    {
        public static void GenerateClassSerializer(ProtoMessage m, CodeWriter cw, Options options)
        {
            if (m.OptionExternal || m.OptionType == "interface")
            {
				var baseclass = m.BaseClass;

				if ( baseclass != null )
					baseclass = " : " + baseclass;
				else
					baseclass = "";

                //Don't make partial class of external classes or interfaces
                //Make separate static class for them
                cw.Bracket($"{m.OptionAccess} partial class {m.SerializerType}{baseclass}");
            }
            else if (m.OptionType == "struct")
            {
                //cw.Attribute("System.Serializable()");
                cw.Bracket($"{m.OptionAccess} partial struct {m.SerializerType} : SilentOrbit.ProtocolBuffers.IProto<{m.SerializerType}>");
            }
            else
            {
                //cw.Attribute("System.Serializable()");
                cw.Bracket($"{m.OptionAccess} partial {m.OptionType} {m.SerializerType} : IDisposable, Facepunch.Pool.IPooled, SilentOrbit.ProtocolBuffers.IProto<{m.SerializerType}>");
            }

            GenerateReset( m, cw, options );
            GenerateCopy( m, cw, options );

            GenerateReader(m, cw);

            GenerateWriter(m, cw, options);
            foreach (ProtoMessage sub in m.Messages.Values)
            {
                cw.WriteLine();
                GenerateClassSerializer(sub, cw, options);
            }

            InspectionCode.GenerateUidInspector(m, cw, options);

            cw.EndBracket();
            cw.WriteLine();
            return;
        }

        static void GenerateCreateNew( CodeWriter cw, string name, ProtoMessage m )
        {
            if (m.OptionType != "struct")
            {
                cw.WriteLine(m.CsType + " " + name + " = Facepunch.Pool.Get<" + m.CsType + ">();");
            }
            else
            {
                cw.WriteLine(m.CsType + " " + name + " = default(" + m.CsType + ");");
            }
        }

        static void GenerateReader(ProtoMessage m, CodeWriter cw)
        {
            #region Helper Deserialize Methods
            string refstr = (m.OptionType == "struct") ? "ref " : "";
            string virtualstr = (m.OptionType == "struct") ? "" : "virtual ";
			if ( m.OptionType != "interface" && !m.OptionNoPartials )
            {
				if ( !m.OptionNoInstancing )
				{
					cw.Summary( "Helper: create a new instance to deserializing into" );
					cw.Bracket( m.OptionAccess + " static " + m.CsType + " Deserialize(BufferStream stream)" );
                    GenerateCreateNew( cw, "instance", m );
                    cw.WriteLine( "Deserialize(stream, " + refstr + "instance, false);" );
					cw.WriteLine( "return instance;" );
                    cw.EndBracketSpace();

					cw.Summary( "Helper: create a new instance to deserializing into" );
					cw.Bracket( m.OptionAccess + " static " + m.CsType + " DeserializeLengthDelimited(BufferStream stream)" );
                    GenerateCreateNew( cw, "instance", m );
                    cw.WriteLine( "DeserializeLengthDelimited(stream, " + refstr + "instance, false);" );
					cw.WriteLine( "return instance;" );
					cw.EndBracketSpace();

					cw.Summary( "Helper: create a new instance to deserializing into" );
					cw.Bracket( m.OptionAccess + " static " + m.CsType + " DeserializeLength(BufferStream stream, int length)" );
                    GenerateCreateNew( cw, "instance", m );
                    cw.WriteLine( "DeserializeLength(stream, length, " + refstr + "instance, false);" );
					cw.WriteLine( "return instance;" );
                    cw.EndBracketSpace();

                    cw.Summary( "Helper: put the buffer into a MemoryStream and create a new instance to deserializing into" );
                    cw.Bracket( m.OptionAccess + " static " + m.CsType + " Deserialize(byte[] buffer)" );
                    GenerateCreateNew( cw, "instance", m );
                    cw.WriteLine( "using var stream = Facepunch.Pool.Get<BufferStream>().Initialize(buffer);" );
                    cw.WriteLine( "Deserialize(stream, " + refstr + "instance, false);" );
                    cw.WriteLine( "return instance;" );
                    cw.EndBracketSpace();
                }

				cw.Summary( "Load this value from a proto buffer" );
				cw.Bracket( m.OptionAccess + " void FromProto( BufferStream stream, bool isDelta = false )" );
				cw.WriteLine( $"Deserialize( stream, {refstr}this, isDelta );" );
                cw.EndBracketSpace();

                cw.Bracket( $"public {virtualstr}void WriteToStream( BufferStream stream )" );
                cw.WriteLine( $"Serialize( stream, this );" );
                cw.EndBracketSpace();

                cw.Bracket( $"public {virtualstr}void WriteToStreamDelta( BufferStream stream, " + m.CsType + " previous )" );
                if (m.OptionType == "struct")
                {
                    cw.WriteLine( "SerializeDelta( stream, this, previous );");
                }
                else
                {
                    cw.WriteLine( "if ( previous == null ) Serialize( stream, this );");
                    cw.WriteLine( "else SerializeDelta( stream, this, previous );");
                }

                cw.EndBracketSpace();
                
                cw.Bracket( $"public {virtualstr}void ReadFromStream( BufferStream stream, bool isDelta = false )" );
                cw.WriteLine( $"Deserialize( stream, {refstr}this, isDelta );" );
                cw.EndBracketSpace();

                cw.Bracket( $"public {virtualstr}void ReadFromStream( BufferStream stream, int size, bool isDelta = false )" );
                cw.WriteLine( $"DeserializeLength( stream, size, {refstr}this, isDelta );" );
                cw.EndBracketSpace();

            }

            /*cw.Summary("Helper: put the buffer into a Span<byte> before deserializing");
            cw.Bracket(m.OptionAccess + " static " + m.FullCsType + " Deserialize(Span<byte> buffer, " + refstr + m.FullCsType + " instance, bool isDelta = false )");
            cw.WriteLine("var stream = new BufferStream( buffer );");
            cw.WriteLine("Deserialize(stream, " + refstr + "instance, isDelta );" );
            cw.WriteLine("return instance;");
            cw.EndBracketSpace();*/
            #endregion

            string[] methods = new string[]
            {
                "Deserialize", //Default old one
                "DeserializeLengthDelimited", //Start by reading length prefix and stay within that limit
                "DeserializeLength", //Read at most length bytes given by argument
            };

            //Main Deserialize
            foreach (string method in methods)
            {
                if (method == "Deserialize")
                {
                    cw.Summary("Takes the remaining content of the stream and deserialze it into the instance.");
                    cw.Bracket(m.OptionAccess + " static " + m.FullCsType + " " + method + "(BufferStream stream, " + refstr + m.FullCsType + " instance, bool isDelta )" );
                }
                else if (method == "DeserializeLengthDelimited")
                {
                    cw.Summary("Read the VarInt length prefix and the given number of bytes from the stream and deserialze it into the instance.");
                    cw.Bracket(m.OptionAccess + " static " + m.FullCsType + " " + method + "(BufferStream stream, " + refstr + m.FullCsType + " instance, bool isDelta )");
                }
                else if (method == "DeserializeLength")
                {
                    cw.Summary("Read the given number of bytes from the stream and deserialze it into the instance.");
                    cw.Bracket(m.OptionAccess + " static " + m.FullCsType + " " + method + "(BufferStream stream, int length, " + refstr + m.FullCsType + " instance, bool isDelta )" );
                }
                else
                    throw new NotImplementedException();

                //Prepare List<> and default values
                cw.IfBracket( "!isDelta" );

                foreach (Field f in m.Fields.Values)
                {
                    if (f.Rule == FieldRule.Repeated)
                    {
                        //Initialize lists of the custom DateTime or TimeSpan type.
                        string csType = f.ProtoType.FullCsType;
                        if (f.OptionCodeType != null)
                            csType = f.OptionCodeType;

                        cw.WriteLine("if (instance." + f.CsName + " == null)");
                        cw.WriteIndent("instance." + f.CsName + " = Facepunch.Pool.Get<List<" + csType + ">>();");
                    }
                    else if (f.OptionDefault != null)
                    {
                        if (f.ProtoType is ProtoEnum)
                            cw.WriteLine("instance." + f.CsName + " = " + f.ProtoType.FullCsType + "." + f.OptionDefault + ";");
                        else
                            cw.WriteLine("instance." + f.CsName + " = " + f.OptionDefault + ";");
                    }
                    else if (f.Rule == FieldRule.Optional)
                    {
                        if (f.ProtoType is ProtoEnum)
                        {
                            ProtoEnum pe = f.ProtoType as ProtoEnum;
                            //the default value is the first value listed in the enum's type definition
                            foreach (var kvp in pe.Enums)
                            {
                                cw.WriteLine("instance." + f.CsName + " = " + pe.FullCsType + "." + kvp.Name + ";");
                                break;
                            }
                        }
                    }
                }
                cw.EndBracket();

                if (method == "DeserializeLengthDelimited")
                {
                    //Important to read stream position after we have read the length field
                    cw.WriteLine("long limit = global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadUInt32(stream);");
                    cw.WriteLine("limit += stream.Position;");
                }
                if (method == "DeserializeLength")
                {
                    //Important to read stream position after we have read the length field
                    cw.WriteLine("long limit = stream.Position + length;");
                }

                cw.WhileBracket("true");

                if (method == "DeserializeLengthDelimited" || method == "DeserializeLength")
                {
                    cw.IfBracket("stream.Position >= limit");
                    cw.WriteLine("if (stream.Position == limit)");
                    cw.WriteIndent("break;");
                    cw.WriteLine("else");
                    cw.WriteIndent("throw new global::SilentOrbit.ProtocolBuffers.ProtocolBufferException(\"Read past max limit\");");
                    cw.EndBracket();
                }

                cw.WriteLine("int keyByte = stream.ReadByte();");

                if (method == "Deserialize")
                    cw.WriteLine("if (keyByte == -1 || keyByte == 0)");
                else
                    cw.WriteLine("if (keyByte == -1)");
                if (method == "Deserialize")
                    cw.WriteIndent("break;");
                else
                    cw.WriteIndent("throw new System.IO.EndOfStreamException();");

                //Determine if we need the lowID optimization
                bool hasLowID = false;
                foreach (Field f in m.Fields.Values)
                {
                    if (f.ID < 16)
                    {
                        hasLowID = true;
                        break;
                    }
                }

                if (hasLowID)
                {
                    cw.Comment("Optimized reading of known fields with field ID < 16");
                    cw.Switch("keyByte");
                    foreach (Field f in m.Fields.Values)
                    {
                        if (f.ID >= 16)
                            continue;
                        cw.Dedent();
                        cw.Comment("Field " + f.ID + " " + f.WireType);
                        cw.Indent();
                        cw.Case(((f.ID << 3) | (int)f.WireType));
                        if (FieldSerializer.FieldReader(f, cw))
                            cw.WriteLine("continue;");
                    }
                    cw.SwitchEnd();
                    cw.WriteLine();
                }
                cw.WriteLine("var key = global::SilentOrbit.ProtocolBuffers.ProtocolParser.ReadKey((byte)keyByte, stream);");

                cw.WriteLine();

                cw.Comment("Reading field ID > 16 and unknown field ID/wire type combinations");
                cw.Switch("key.Field");
                //cw.Case(0);
                //cw.WriteLine("throw new global::SilentOrbit.ProtocolBuffers.ProtocolBufferException(\"Invalid field id: 0, something went wrong in the stream\");");
                foreach (Field f in m.Fields.Values)
                {
                    if (f.ID < 16)
                        continue;
                    cw.Case(f.ID);
                    //Makes sure we got the right wire type
                    cw.WriteLine("if(key.WireType != global::SilentOrbit.ProtocolBuffers.Wire." + f.WireType + ")");
                    cw.WriteIndent("break;"); //This can be changed to throw an exception for unknown formats.
                    if (FieldSerializer.FieldReader(f, cw))
                        cw.WriteLine("continue;");
                }
                cw.CaseDefault();
                cw.WriteLine("global::SilentOrbit.ProtocolBuffers.ProtocolParser.SkipKey(stream, key);");
                cw.WriteLine("break;");
                cw.SwitchEnd();
                cw.EndBracket();
                cw.WriteLine();

                if (m.OptionTriggers)
                    cw.WriteLine("instance.AfterDeserialize();");
                cw.WriteLine("return instance;");
                cw.EndBracket();
                cw.WriteLine();
            }

            return;
        }

        static void ResetAndPoolField( CodeWriter cw, string name, Field f )
        {
            switch ( f.ProtoTypeName )
            {
                case "bool":
                    {
                        cw.WriteLine( name + " = false;" );
                        break;
                    }

                case "bytes":
                    {
                        if ( f.OptionPooled )
                        {
                            cw.IfBracket( name + ".Array != null" );
                            cw.WriteLine( "BufferStream.Shared.ArrayPool.Return(" + name + ".Array);" );
                            cw.EndBracket();
                            cw.WriteLine( name + " = default(ArraySegment<byte>);");
                        }
                        else
                        {
                            cw.WriteLine( name + " = null;" );
                        }
                        break;
                    }

                case "string":
                    {
                        cw.WriteLine( name + " = string.Empty;" );
                        break;
                    }

                case "uint32":
                case "sint32":
                case "int32":
                case "fixed32":
                case "sfixed32":
                case "float":
                case "double":
                case "int64":
                case "uint64":
                case "sint64":
                case "fixed64":
                case "sfixed64":
                    {
                        cw.WriteLine( name + " = 0;" );
                        break;
                    }

                case ProtoBuiltin.NetworkableId:
                case ProtoBuiltin.ItemContainerId:
                case ProtoBuiltin.ItemId:
                    {
                        cw.WriteLine( $"{name} = default( {f.ProtoType.FullCsType} );" );
                        break;
                    }

                default:
                    {

                        if ( f.ProtoType.OptionType == "struct" )
                        {
                            //cw.WriteLine( "// Don't bother resetting structs? " );
                            //cw.WriteLine( "// " + f.ProtoType.OptionNamespace + "." + f.ProtoTypeName + "Serialized.ResetToPool( " + name + ");" );
                            cw.WriteLine( name + " = default( " + f.ProtoType.FullCsType + " );" );
                        }
                        else if ( f.ProtoType is ProtoEnum )
                        {
                            cw.WriteLine( name + " = 0;" );
                        }
                        else if ( f.ProtoType.OptionExternal )
                        {
                            cw.Bracket( "if ( " + name + " != null )" );
                            cw.WriteLine( name + " = null;" );
                            cw.EndBracket();
                        }
                        else
                        {
                            //cw.WriteLine( "// " + ( f.ProtoType.ToString ) );
                            cw.Bracket( "if ( " + name + " != null )" );
                            cw.WriteLine( name + ".ResetToPool();" );
                            cw.WriteLine( name + " = null;" );
                            cw.EndBracket();
                        }

                        break;
                    }
            }
        }

        /// <summary>
        /// Generates code for resetting
        /// </summary>
        static void GenerateReset( ProtoMessage m, CodeWriter cw, Options options )
        {
            //
            // No reset for external classes
            //
            if ( m.OptionExternal && m.OptionType != "struct" )
                return;

            cw.Summary( "Reset the class to its default state" );
            cw.Bracket( m.OptionAccess + " static void ResetToPool(" + m.CsType + " instance)" );

            if ( m.OptionType != "struct" )
            {
                cw.WriteLine( "if ( !instance.ShouldPool ) return;" );
                cw.WriteLine();
            }

            foreach ( Field f in m.Fields.Values )
            {
                if ( f.Rule == FieldRule.Repeated )
                {
                    cw.Bracket( "if ( instance."+ f.CsName + " != null )" );

                    if ( f.ProtoType is ProtoMessage && f.ProtoType.OptionType != "struct" )
                    {
                        cw.Bracket( "for ( int i=0; i< instance." + f.CsName + ".Count; i++ )" );
                        ResetAndPoolField( cw, "instance." + f.CsName + "[i]", f );
                        cw.EndBracket();
                    }

                    cw.WriteLine( "var c = instance." + f.CsName + ";" );
                    
                    if(f.ProtoType.OptionType == "class")
                    {
                        cw.WriteLine("Facepunch.Pool.Free( ref c );");
                    }
                    else
                    {
                        cw.WriteLine("Facepunch.Pool.FreeUnmanaged( ref c );");
                    }
                    
                    cw.WriteLine( "instance." + f.CsName + " = c;" );
                    cw.EndBracket();
                    cw.WriteLine();
                    continue;
                }

                ResetAndPoolField( cw,  "instance." + f.CsName, f );
            }

            if ( m.OptionType != "struct" )
            {
                cw.WriteLine();
                cw.WriteLine( "Facepunch.Pool.Free( ref instance );" );
            }

            cw.EndBracket();
            cw.WriteLine();

            if ( m.OptionType != "struct" )
            {
                cw.Summary( "Reset the class to its default state" );
                cw.Bracket( m.OptionAccess + " void ResetToPool()" );
                cw.WriteLine( "ResetToPool( this );" );
                cw.EndBracketSpace();

                cw.WriteLine();
                cw.WriteLine( "public bool ShouldPool = true;" );
                cw.WriteLine( "private bool _disposed = false;" );
                cw.WriteLine();

                cw.Bracket( "public virtual void Dispose()" );
                cw.WriteLine( $"if ( !ShouldPool ) throw new Exception( \"Trying to dispose {m.CsType} with ShouldPool set to false!\" );" );
                cw.WriteLine( "if ( _disposed ) return;" );
                cw.WriteLine( "ResetToPool();" );
                cw.WriteLine( "_disposed = true;" );
                cw.EndBracketSpace();

                cw.Bracket( "public virtual void EnterPool()" );
                cw.WriteLine( "_disposed = true;" );
                cw.EndBracketSpace();

                cw.Bracket( "public virtual void LeavePool()" );
                cw.WriteLine( "_disposed = false;" );
                cw.EndBracketSpace();
            }
        }

        /// <summary>
        /// Generates code for writing a class/message
        /// </summary>
        static void GenerateWriter( ProtoMessage m, CodeWriter cw, Options options )
        {
            string stack = "global::SilentOrbit.ProtocolBuffers.ProtocolParser.Stack";
            if ( options.ExperimentalStack != null )
            {
                throw new System.NotSupportedException();
                cw.WriteLine( "[ThreadStatic]" );
                cw.WriteLine( "static global::SilentOrbit.ProtocolBuffers.MemoryStreamStack stack = new " + options.ExperimentalStack + "();" );
                stack = "stack";
            }

            // SerializeDelta
            {
                cw.Summary( "Serialize the instance into the stream, using the delta from the previous" );
                cw.Bracket( m.OptionAccess + " static void SerializeDelta(BufferStream stream, " + m.CsType + " instance, " + m.CsType + " previous )" );

                if ( m.OptionTriggers )
                {
                    cw.WriteLine( "instance.BeforeSerialize();" );
                    cw.WriteLine();
                }

                foreach ( Field f in m.Fields.Values )
                {
                    FieldSerializer.FieldWriter( m, f, cw, true );
                }

                cw.EndBracket();
                cw.WriteLine();
            }

            cw.Summary("Serialize the instance into the stream");
            cw.Bracket(m.OptionAccess + " static void Serialize(BufferStream stream, " + m.CsType + " instance)");
            if (m.OptionTriggers)
            {
                cw.WriteLine("instance.BeforeSerialize();");
                cw.WriteLine();
            }
            //if (m.IsUsingBinaryWriter)
            //    cw.WriteLine("BinaryWriter bw = new BinaryWriter(stream);");

            foreach (Field f in m.Fields.Values)
                FieldSerializer.FieldWriter(m, f, cw);

            cw.EndBracket();
            cw.WriteLine();

			if ( m.OptionType != "interface" && !m.OptionNoPartials )
			{
				cw.Summary( "Serialize to a Stream" );
				cw.Bracket( m.OptionAccess + " void ToProto( BufferStream stream )" );
				cw.WriteLine( "Serialize( stream, this );" );
				cw.EndBracketSpace();
			}
        }

        /// <summary>
        /// Generates code for resetting
        /// </summary>
        static void GenerateCopy( ProtoMessage m, CodeWriter cw, Options options )
        {
            //
            // No reset for external classes
            //
            if ( m.OptionExternal )
                return;

            cw.Summary( "Copy one instance to another" );
            cw.Bracket( m.OptionAccess + " void CopyTo(" + m.CsType + " instance)" );

            foreach ( Field f in m.Fields.Values )
            {
                if ( f.Rule == FieldRule.Repeated )
                {
                    cw.IfBracket( $"this.{f.CsName} != null" );
                    cw.WriteLine( $"instance.{f.CsName} = Facepunch.Pool.Get<List<{f.ProtoType.FullCsType}>>();" );

                    cw.ForeachBracket( "item", $"this.{f.CsName}" );
                    if ( f.ProtoType.ProtoName == "bytes" )
                    {
                        cw.WriteLine( "throw new NotImplementedException( \"TODO: Copy bytes\" );" );
                    }
                    else if ( f.ProtoType.OptionType == "struct" || f.ProtoType is ProtoEnum )
                    {
                        // value type - no copy needed
                        cw.WriteLine( $"instance.{f.CsName}.Add( item );" );
                    }
                    else if ( f.ProtoType is ProtoMessage )
                    {
                        cw.WriteLine( $"var copy = item.Copy();" );
                        cw.WriteLine( $"instance.{f.CsName}.Add( copy );" );
                    }
                    else
                    {
                        // primitive - no copy needed
                        cw.WriteLine( $"instance.{f.CsName}.Add( item );" );
                    }
                    cw.EndBracket();

                    cw.EndBracket();
                    cw.Bracket( "else" );
                    cw.WriteLine( $"instance.{f.CsName} = null;" );
                    cw.EndBracket();

                    continue;
                }

                switch ( f.ProtoTypeName )
                {
                    case "bool":
                    case "uint32":
                    case "int32":
                    case "sint32":
                    case "fixed32":
                    case "sfixed32":
                    case "float":
                    case "double":
                    case "int64":
                    case "uint64":
                    case "sint64":
                    case "fixed64":
                    case "sfixed64":
                    case "string":
                    case ProtoBuiltin.NetworkableId:
                    case ProtoBuiltin.ItemContainerId:
                    case ProtoBuiltin.ItemId:
                        {
                            cw.WriteLine( "instance." + f.CsName + " = " + "this." + f.CsName + ";" );
                            break;
                        }
                    case "bytes":
                        {
                            if ( f.OptionPooled )
                            {
                                cw.IfBracket( $"this.{f.CsName}.Array == null" );
                                cw.WriteLine( $"instance.{f.CsName} = default(ArraySegment<byte>);" );
                                cw.EndBracket();
                                cw.WriteLine( "else" );
                                cw.Bracket();
                                cw.WriteLine( $"var buffer{f.ID} = BufferStream.Shared.ArrayPool.Rent( this.{f.CsName}.Count );" );
                                cw.WriteLine( $"Array.Copy( this.{f.CsName}.Array, 0, buffer{f.ID}, 0, this.{f.CsName}.Count );" );
                                cw.WriteLine( $"instance.{f.CsName} = new ArraySegment<byte>( buffer{f.ID}, 0, this.{f.CsName}.Count );" );
                                cw.EndBracket();
                            }
                            else
                            {
                                cw.WriteLine( "if ( this." + f.CsName + " == null )" );
                                cw.Bracket();
                                cw.WriteLine( "instance." + f.CsName + " = null;" );
                                cw.EndBracket();
                                cw.WriteLine( "else" );
                                cw.Bracket();
                                cw.WriteLine( "instance." + f.CsName + " = new byte[this." + f.CsName + ".Length];" );
                                cw.WriteLine( "Array.Copy( this." + f.CsName + ", instance." + f.CsName + ", instance." + f.CsName + ".Length );" );
                                cw.EndBracket();
                            }
                            break;
                        }

                    default:
                        {
                            if ( f.ProtoType.OptionType == "struct" || f.ProtoType is ProtoEnum )
                            {
                                cw.WriteLine( "instance." + f.CsName + " = " + "this." + f.CsName + ";" );
                            }
                            else
                            {
                                cw.IfBracket( "this." + f.CsName + " != null" );

                                cw.WriteLine( "if ( instance." + f.CsName + " == null )" );
                                cw.WriteLine( "   instance." + f.CsName + " = " + "this." + f.CsName + ".Copy();" );
                                cw.WriteLine( "else" );
                                cw.WriteLine( "   this." + f.CsName + ".CopyTo( instance." + f.CsName + " );" );
                                cw.EndBracket();
                                cw.WriteLine( "else" );
                                cw.Bracket();
                                cw.WriteLine( "instance." + f.CsName + " = null;" );
                                cw.EndBracket();
                            }
                            break;
                        }
                }                       
            }

            cw.EndBracket();
            cw.WriteLine();

            cw.Summary( "Reset the class to its default state - " + m.OptionType );
            cw.Bracket( m.OptionAccess + " " + m.CsType + " Copy()" );
            GenerateCreateNew( cw, "newInstance", m );
            cw.WriteLine( "this.CopyTo( newInstance );" );
            cw.WriteLine( "return newInstance;" );
            cw.EndBracketSpace();
        }
    }


}

