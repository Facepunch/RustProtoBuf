using System;
using SilentOrbit.Code;

namespace SilentOrbit.ProtocolBuffers
{
    static class InspectionCode
    {
        public static void GenerateShared(CodeWriter cw, Options options)
        {
            cw.Bracket("public enum UidType");
            cw.WriteLine($"/// <summary>");
            cw.WriteLine($"/// This UID refers to an entity");
            cw.WriteLine("/// </summary>");
            cw.WriteLine($"{ProtoBuiltin.NetworkableId},");
            cw.WriteLine($"/// <summary>");
            cw.WriteLine($"/// This UID refers to an item container");
            cw.WriteLine($"/// </summary>");
            cw.WriteLine($"{ProtoBuiltin.ItemContainerId},");
            cw.WriteLine($"/// <summary>");
            cw.WriteLine($"/// This UID refers to an item");
            cw.WriteLine($"/// </summary>");
            cw.WriteLine($"{ProtoBuiltin.ItemId},");
            cw.WriteLine($"/// <summary>");
            cw.WriteLine($"/// This UID is not important and needs to be cleared to zero");
            cw.WriteLine($"/// </summary>");
            cw.WriteLine($"Clear,");
            cw.EndBracket();
            cw.WriteLine();
            cw.WriteLine("public delegate void UidInspector<T>(UidType type, ref T value);");
            cw.WriteLine();
        }

        public static void GenerateUidInspector(ProtoMessage m, CodeWriter cw, Options options)
        {
            cw.Bracket("public void InspectUids(UidInspector<ulong> action)");

            foreach (var f in m.Fields.Values)
            {
                if (f.ProtoType.ProtoName == ProtoBuiltin.NetworkableId || f.ProtoType.ProtoName == ProtoBuiltin.ItemContainerId || f.ProtoType.ProtoName == ProtoBuiltin.ItemId)
                {
                    var type = f.OptionUidClear ? "Clear" : f.ProtoType.ProtoName;
                    if (f.Rule == FieldRule.Repeated)
                    {
                        cw.IfBracket($"{f.CsName} != null");
                        cw.ForeachBracket("uid", f.CsName);
                        cw.WriteLine($"action(UidType.{type}, ref uid.Value);");
                        cw.WriteLine($"{f.CsName}[i] = uid;"); // Write changes back, note: ForeachBracket doesn't actually use a foreach
                        cw.EndBracket();
                        cw.EndBracket();
                    }
                    else
                    {
                        cw.WriteLine($"action(UidType.{type}, ref {f.CsName}.Value);");
                    }
                }
                else if (f.ProtoType is ProtoMessage message && !message.OptionExternal)
                {
                    var isRepeated = f.Rule == FieldRule.Repeated;
                    var itemName = isRepeated ? "_item" : f.CsName;

                    if (isRepeated)
                    {
                        cw.IfBracket($"{f.CsName} != null");
                        cw.ForeachBracket("_item", f.CsName);
                    }

                    if (f.ProtoType.Nullable)
                    {
                        cw.WriteLine($"{itemName}?.InspectUids(action);");
                    }
                    else
                    {
                        cw.WriteLine($"{itemName}.InspectUids(action);");
                    }

                    if (isRepeated)
                    {
                        cw.EndBracket();
                        cw.EndBracket();
                    }
                }
            }

            cw.EndBracket();
        }
    }
}
