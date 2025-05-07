using System;
using System.Collections.Generic;

namespace SilentOrbit.ProtocolBuffers
{
    class ProtoMessage : ProtoType, IComment
    {
        public override Wire WireType
        {
            get { return Wire.LengthDelimited; }
        }

        public string Comments { get; set; }

        public Dictionary<int, Field> Fields = new Dictionary<int, Field>();
        public Dictionary<string, ProtoMessage> Messages = new Dictionary<string, ProtoMessage>();
        public Dictionary<string, ProtoEnum> Enums = new Dictionary<string, ProtoEnum>();

		public string BaseClass
		{
			get
			{
				return this.OptionBase;
			}
		}

        public string SerializerType
        {
            get
            {
				if ( this.OptionType == "interface" || this.OptionNoPartials )
				{
					return CsType + "Serialized";
				}
				else if (this.OptionExternal )
                    return CsType;
                else
                    return CsType;
            }
        }

        public string FullSerializerType
        {
            get
            {
				if ( this.OptionType == "interface" || this.OptionNoPartials )
				{
					return FullCsType + "Serialized";
				}
				else if ( this.OptionExternal )
					return FullCsType;
				else
					return FullCsType;
            }
        }

        public ProtoMessage(ProtoMessage parent, string package)
            : base(parent, package)
        {
            this.OptionType = "class";
        }

        public override string ToString()
        {
            return "message " + FullProtoName;
        }

        public bool IsUsingBinaryWriter
        {
            get
            {
                foreach (Field f in Fields.Values)
                    if (f.IsUsingBinaryWriter)
                        return true;
                return false;
            }
        }

        /// <summary>
        /// If all fields are constant then this message is constant too
        /// </summary>
        public override int WireSize
        {
            get
            {
                int totalSize = 0;
                foreach (Field f in Fields.Values)
                {
                    if (f.ProtoType.WireSize < 0)
                        return -1;
                    totalSize += 2 + f.ProtoType.WireSize;
                }
                return totalSize;
            }
        }

        private bool _isCalculatingMaxWireSize;
        private int? _maxWireSizeCache;
        public override int MaximumWireSize
        {
            get
            {
                if (_maxWireSizeCache != null)
                    return _maxWireSizeCache.Value;

                if (_isCalculatingMaxWireSize)
                    return -1; // sucky fix for infinite recursion

                _isCalculatingMaxWireSize = true;

                var totalSize = 0;
                foreach (var f in Fields.Values)
                {
                    var typeSize = f.ProtoType.MaximumWireSize;
                    if (typeSize <= 0)
                    {
                        totalSize = -1;
                        break;
                    }

                    if (f.Rule == FieldRule.Repeated)
                    {
                        typeSize *= 10_000; // assume a generous maximum length for repeated fields
                    }
                    
                    totalSize += 2 + typeSize; // allow up to two bytes for each field's tag
                }

                _maxWireSizeCache = totalSize;
                _isCalculatingMaxWireSize = false;
                return totalSize;
            }
        }
    }
}

