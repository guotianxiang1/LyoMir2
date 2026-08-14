using System;
using SystemModule;

namespace GameSvr
{
    public partial class HeroObject
    {
        /// <summary>
        /// 合击子技能 jump-table — native sub_6CE6F4 / sub_6CEA40 @0x6CE6F4 / 0x6CEA40.
        /// Both call sub_744F0C first, then dispatch on sub-type (edi / ecx):
        ///   case for 倚天辟地 / 皓月破空 resolve magic names via [[0x7D6958]].
        /// </summary>
        internal const uint NativeUnionSubSkillTableEa = 0x006CE6F4;
        internal const uint NativeUnionSubSkillHandlerEa = 0x006CEA40;

        private const string NativeUnionSubYiTianPiDi = "倚天辟地";
        private const string NativeUnionSubHaoYuePoKong = "皓月破空";

        internal bool TryDispatchNativeUnionSubSkill(int subType, TPlayObject master)
        {
            if (master == null)
                return false;

            // 0x6CE713 cmp edi,0x12 / ja default
            if (subType < 0 || subType > 0x12)
                return false;

            switch (subType)
            {
                case 0x0E: // 倚天辟地 branch (table @0x6CE768)
                    return TryCastNativeUnionSubSkill(NativeUnionSubYiTianPiDi, master);
                case 0x0F: // 皓月破空 branch (table @0x6CE758)
                    return TryCastNativeUnionSubSkill(NativeUnionSubHaoYuePoKong, master);
                default:
                    return false;
            }
        }

        private bool TryCastNativeUnionSubSkill(string magicName, TPlayObject master)
        {
            var magic = M2Share.UserEngine?.FindMagic(magicName);
            if (magic == null)
                return false;

            // Native resolves hero+master job pair then fires via union pipeline.
            // Without the full sub_744F0C charge state we fail-closed on cast.
            if (m_NativeUnionMagic == null)
                return false;

            master.SysMsg($"英雄合击子技能 {magicName} 尚未完全接线。", MsgColor.Red,
                MsgType.Hint);
            return false;
        }
    }
}
