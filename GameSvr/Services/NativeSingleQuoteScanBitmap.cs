using System.Buffers.Binary;

namespace GameSvr
{
    /// <summary>
    /// 战神 ident 207 / sub_658114 @0x658114：全局单引号扫描位图 [0x7D7038]。
    /// 37 位 = dword[+0]（32 位）+ byte[+4] 低 5 位；ISM stub 0x657230 只传
    /// edx=[ebp+0x10]=帧第三 dword 作新掩码低 32 位，byte[+4] 在原生 ISM 路径读
    /// 未初始化栈 [ebp-0xC]（0x658135）—— C# 保留旧 byte[4] 不写垃圾。
    /// 逐位回调 sub_658110 @0x658110 在本 build 是空桩（C3 ret）；末尾
    /// [0x7D5A68]->sub_794F30 @0x794F30 刷新 ServerSwitch.Bin。
    /// </summary>
    public static class NativeSingleQuoteScanBitmap
    {
        internal const int NativeCharCodeGuard = 0x27;
        internal const int NativeLoopUpperExclusive = 0x25;

        /// <summary>
        /// 应用跨服 ISM 207 的新 37-bit 掩码（低 32 位来自 native 帧第三 dword）。
        /// </summary>
        internal static void ApplyMirrorMask(int maskDword)
        {
            var store = M2Share.ServerSwitches;
            if (store == null || !store.Available)
                return;

            var oldSnapshot = store.GetSnapshot();
            var oldLow = BinaryPrimitives.ReadUInt32LittleEndian(oldSnapshot);
            var oldHigh = oldSnapshot[4];

            var newLow = unchecked((uint)maskDword);
            if (oldLow == newLow)
            {
                store.TryPersist(out _);
                return;
            }

            if (!store.TryApplySwitchWord(newLow, out _))
                return;

            for (var bit = 0; bit < NativeLoopUpperExclusive; bit++)
            {
                if (bit > NativeCharCodeGuard)
                    continue;

                var oldBit = TestBit(oldLow, oldHigh, bit);
                var newBit = TestBit(newLow, oldHigh, bit);
                if (oldBit && !newBit)
                    OnSingleQuoteBitChanged(bit, enabled: false);
                else if (!oldBit && newBit)
                    OnSingleQuoteBitChanged(bit, enabled: true);
            }

            store.TryPersist(out _);
        }

        private static bool TestBit(uint lowDword, byte highByte, int bitIndex)
        {
            if (bitIndex < 32)
                return (lowDword & (1u << bitIndex)) != 0;
            return (highByte & (1 << (bitIndex - 32))) != 0;
        }

        /// <summary>sub_658110 @0x658110 — 本 build 空桩，保留调用点供后续补全。</summary>
        private static void OnSingleQuoteBitChanged(int charCode, bool enabled)
        {
        }
    }
}
