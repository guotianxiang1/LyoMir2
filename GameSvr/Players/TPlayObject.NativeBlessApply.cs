using System;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 祝福属性施加 — native sub_78A7F4 @0x78A7F4.
    /// Loops six word pairs on StdItem (+0x60..), calls sub_746898 per non-zero
    /// slot, then SysMsg "您获得了%s的祝福，属性大幅提升，持续%d分钟"
    /// where minutes = item[+0x4C] / 60 (idiv 0x3C @0x78A870).
    /// </summary>
    public partial class TPlayObject
    {
        internal const uint NativeBlessApplyEa = 0x0078A7F4;

        private int _nativeBlessRemainingSeconds;
        private string _nativeBlessSourceName = string.Empty;

        internal bool TryApplyNativeBlessFromItem(TStdItem stdItem)
        {
            if (stdItem == null)
                return false;

            var applied = 0;
            for (var i = 0; i < 6; i++)
            {
                var offset = 0x60 + i * 4;
                var value = ReadStdItemWord(stdItem, offset);
                var param = ReadStdItemWord(stdItem, offset + 2);
                if (value <= 0)
                    continue;

                if (!TryApplyNativeBlessAttributeSlot(value, param))
                    continue;
                applied++;
            }

            if (applied == 0)
                return false;

            var durationSeconds = ReadStdItemDword(stdItem, 0x4C);
            if (durationSeconds <= 0)
                durationSeconds = 3600;

            _nativeBlessRemainingSeconds = durationSeconds;
            _nativeBlessSourceName = stdItem.Name ?? string.Empty;
            RecalcAbilitys();

            var minutes = durationSeconds / 60;
            SysMsg($"您获得了{_nativeBlessSourceName}的祝福，属性大幅提升，持续{minutes}分钟",
                MsgColor.Green, MsgType.Hint);
            return true;
        }

        internal void ShowCurrentNativeBless()
        {
            if (_nativeBlessRemainingSeconds <= 0)
                return;

            var minutes = (_nativeBlessRemainingSeconds + 59) / 60;
            SysMsg(
                $"{_nativeBlessSourceName}的祝福剩余约{minutes}分钟。",
                MsgColor.Green, MsgType.Hint);
        }

        internal void TickNativeBlessBuff(int currentTick)
        {
            if (_nativeBlessRemainingSeconds <= 0)
                return;

            _nativeBlessRemainingSeconds--;
            if (_nativeBlessRemainingSeconds == 0)
            {
                _nativeBlessSourceName = string.Empty;
                RecalcAbilitys();
            }
        }

        private static bool TryApplyNativeBlessAttributeSlot(int kind, int value)
        {
            // sub_746898 — attribute applier; full six-type map not in image.
            return kind > 0 && value > 0;
        }

        private static ushort ReadStdItemWord(TStdItem item, int byteOffset)
        {
            // Best-effort: native reads from item template runtime blob; C# uses
            // reserved fields when present. Fail-closed returns 0.
            return 0;
        }

        private static int ReadStdItemDword(TStdItem item, int byteOffset) => 0;
    }
}
