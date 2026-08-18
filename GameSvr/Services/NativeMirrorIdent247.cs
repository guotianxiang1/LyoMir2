using SystemModule;
using GameSvr.Services;

namespace GameSvr
{
    /// <summary>
    /// 战神 ident 247 / sub_65805C @0x65805C：len==0xD(13) 门 + body 三 dword
    /// d0=[body+0], d4=[body+4], d8=[body+8] -> sub_699310 @0x699310。
    /// 形参：edi=d0=ParamNo, esi=d4=index(1..50), [ebp+8]=d8=value。
    /// C# 文本入口仅作为兼容适配器；原生二进制入口仍必须先执行 13 字节长度门。
    /// </summary>
    public static class NativeMirrorIdent247
    {
        internal const int NativeBodyLenGate = 0x0D;
        internal const int NativeIndexMin = 1;
        internal const int NativeIndexMax = 0x32;
        internal const uint NativeHandlerEa = 0x0065805C;
        internal const uint NativeWriterEa = 0x00699310;

        internal static INativeMirParamsStore Store { get; set; }
            = new NativeMirParamsMySqlStore();

        internal static void ApplyFromTextBody(string body)
        {
            if (string.IsNullOrEmpty(body))
                return;

            var paramNoText = string.Empty;
            var rest = HUtil32.GetValidStr3(body, ref paramNoText,
                HUtil32.Backslash);
            var indexText = string.Empty;
            rest = HUtil32.GetValidStr3(rest, ref indexText,
                HUtil32.Backslash);
            var valueText = rest;

            if (!int.TryParse(paramNoText, out var paramNo)
                || !int.TryParse(indexText, out var index)
                || !int.TryParse(valueText, out var value))
            {
                return;
            }

            ApplyMirParamWrite(paramNo, index, value);
        }

        internal static void ApplyMirParamWrite(int paramNo, int index, int value)
        {
            if (index < NativeIndexMin || index > NativeIndexMax)
                return;

            if (M2Share.g_Config == null)
                return;

            var flat = paramNo * 100 + index;
            if (flat < 0 || flat >= M2Share.g_Config.GlobalVal.Length)
                return;

            // sub_699310 @0x699310 → sub_724E48 @0x724E48 (cl=1) 写 gamedata.MirParams。
            // 0x69949D test bl / 0x69949F je：持久化失败时不更新内存映射。
            var store = Store;
            if (store == null)
                return;
            if (!store.TryWriteGlobalValue(paramNo, index, value, out var sqlError))
            {
                if (!string.IsNullOrEmpty(sqlError))
                    M2Share.ErrorMessage("[MirParams] ident247 persist: " + sqlError);
                return;
            }

            M2Share.g_Config.GlobalVal[flat] = value;
        }
    }
}
