using System;

namespace SilentOrbit.ProtocolBuffers
{
    /// <summary>
    /// A protobuf message or enum
    /// </summary>
    abstract class ProtoType
    {
        public ProtoMessage Parent { get; set; }

        /// <summary>
        /// Name used in the .proto file, 
        /// </summary>
        public string ProtoName { get; set; }

        /// <summary>
        /// Based on ProtoType and Rule according to the protocol buffers specification
        /// </summary>
        public abstract Wire WireType { get; }

        /// <summary>
        /// The c# type name
        /// </summary>
        public virtual string CsType { get; set; }

        /// <summary>
        /// The C# namespace for this item
        /// </summary>
        public virtual string CsNamespace
        {
            get
            {
                if (OptionNamespace == null)
                {
                    if (Parent is ProtoCollection)
                        return Parent.CsNamespace;
                    else
                        return Parent.CsNamespace + "." + Parent.CsType;
                }
                else
                    return OptionNamespace;
            }
        }

        public virtual string FullCsType
        {
            get 
			{
				if ( CsNamespace == "global" )
					return CsNamespace + "::" + CsType;

				return CsNamespace + "." + CsType; 
			}
        }

        /// <summary>
        /// The C# namespace for this item
        /// </summary>
        public virtual string FullProtoName
        {
            get
            {
                if (Parent is ProtoCollection)
                    return Package + "." + ProtoName;
                return Parent.FullProtoName + "." + ProtoName;
            }
        }

        /// <summary>
        /// .proto package option
        /// </summary>
        public string Package { get; set; }
        
        #region Local options
        public string OptionNamespace { get; set; }

        /// <summary>
        /// (C#) access modifier: public(default)/protected/private
        /// </summary>
        public string OptionAccess { get; set; }

        /// <summary>
        /// Call triggers before/after serialization.
        /// </summary>
        public bool OptionTriggers { get; set; }

        /// <summary>
        /// Don't create class/struct, only serializing code, useful when serializing types in external DLL
        /// </summary>
        public bool OptionExternal { get; set; }

		/// <summary>
		/// Don't create partial classes.. because this class/struct/whatever is external
		/// </summary>
		public bool OptionNoPartials { get; set; }

		/// <summary>
		/// Don't create new instances of this class
		/// </summary>
		public bool OptionNoInstancing { get; set; }

        /// <summary>
        /// Can directly compare these objects
        /// </summary>
        public bool OptionDeltaCompare { get; set; }


        /// <summary>
        /// Can be "class", "struct" or "interface"
        /// </summary>
        public string OptionType { get; set; }

		/// <summary>
		/// This classes base class
		/// </summary>
		public string OptionBase { get; set; }

        #endregion
        
        /// <summary>
        /// Used by types within a namespace
        /// </summary>
        public ProtoType(ProtoMessage parent, string package)
            : this()
        {
            if (this is ProtoCollection == false)
            {
                if (parent == null)
                    throw new ArgumentNullException("parent");
                if (package == null)
                    throw new ArgumentNullException("package");
            }
            this.Parent = parent;
            this.Package = package;
        }

        public ProtoType()
        {
            this.OptionNamespace = null;
            this.OptionAccess = "public";
            this.OptionTriggers = false;
            this.OptionExternal = false;
			this.OptionNoPartials = false;
			this.OptionNoInstancing = false;
            this.OptionType = null;
        }

        public bool Nullable
        {
            get
            {
                if (ProtoName == ProtoBuiltin.String)
                    return true;
                if (ProtoName == ProtoBuiltin.Bytes)
                    return true;
                if (this is ProtoMessage)
                {
                    if (OptionType == "class")
                        return true;
                    if (OptionType == "interface")
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// If constant size, return the size, if not return -1.
        /// </summary>
        public virtual int WireSize
        {
            get
            {
                if (WireType == Wire.Fixed32)
                    return 4;
                if (WireType == Wire.Fixed64)
                    return 8;
                if (WireType == Wire.Varint)
                    return -1;
                if (WireType == Wire.LengthDelimited)
                    return -1;
                return -1;
            }
        }

        public virtual int MaximumWireSize
        {
            get
            {
                if (WireType == Wire.Fixed32)
                    return 4;
                if (WireType == Wire.Fixed64)
                    return 8;
                if (WireType == Wire.Varint)
                    return 10; // ceilToInt(64 / 7)
                return -1;
            }
        }
    }
}

