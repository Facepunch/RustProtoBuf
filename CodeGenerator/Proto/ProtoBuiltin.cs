using System;

namespace SilentOrbit.ProtocolBuffers
{
    /// <summary>
    /// Representation of the build in data types
    /// </summary>
    class ProtoBuiltin : ProtoType
    {
        #region Const of build in proto types
        public const string Double = "double";
        public const string Float = "float";
        public const string Int32 = "int32";
        public const string Int64 = "int64";
        public const string UInt32 = "uint32";
        public const string UInt64 = "uint64";
        public const string SInt32 = "sint32";
        public const string SInt64 = "sint64";
        public const string Fixed32 = "fixed32";
        public const string Fixed64 = "fixed64";
        public const string SFixed32 = "sfixed32";
        public const string SFixed64 = "sfixed64";
        public const string Bool = "bool";
        public const string String = "string";
        public const string Bytes = "bytes";
        public const string NetworkableId = "NetworkableId";
        public const string ItemContainerId = "ItemContainerId";
        public const string ItemId = "ItemId";
        #endregion

        public ProtoBuiltin(string name, Wire wire, string csType)
        {
            ProtoName = name;
            wireType = wire;
            base.CsType = csType;
        }

        public override string CsType
        {
            get { return base.CsType; }
            set { throw new InvalidOperationException(); }
        }

        public override string CsNamespace
        {
            get { throw new InvalidOperationException(); }
        }

        public override string FullCsType
        {
            get { return CsType; }
        }

        readonly Wire wireType;

        public override Wire WireType { get { return wireType; } }

        public override int WireSize
        {
            get
            {
                if (ProtoName == Bool)
                    return 1;
                return base.WireSize;
            }
        }

        public override int MaximumWireSize
        {
            get
            {
                switch (ProtoName)
                {
                    case Bool:
                        return 1;
                    case SFixed32:
                    case Fixed32:
                    case Float:
                        return 4;
                    case SFixed64:
                    case Fixed64:
                    case Double:
                        return 8;
                    case Int32:
                    case SInt32:
                    case UInt32:
                        return 5; // ceilToInt(32 / 7)
                    case Int64:
                    case SInt64:
                    case UInt64:
                    case NetworkableId:
                    case ItemContainerId:
                    case ItemId:
                        return 10; // ceilToInt(64 / 7)
                    case String:
                    case Bytes:
                        return 512 * 1024; // technically unbounded but in practice will always have some limit
                    default:
                        return base.MaximumWireSize;
                }
            }
        }
    }
}

