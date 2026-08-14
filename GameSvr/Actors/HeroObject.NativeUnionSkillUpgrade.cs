using System;
using SystemModule;

namespace GameSvr
{
    public partial class HeroObject
    {
        /// <summary>Hero-only lines for sub_6868F4 ([hero+0x320], [hero+0x400]).</summary>
        internal int m_nNativeHeroAttackInterval;
        internal int m_nNativeHeroHolyDefense;

        /// <summary>
        /// 英雄合击4级升级 — native sub_78AF68 @0x78AF68.
        /// Gates: item "火龙之心", union magic at [+0x6D4] level &lt; 4,
        /// hero level table at 0x7D4DD8 vs [+0x278], then sub_745294 level+1.
        /// </summary>
        internal const uint NativeUnionSkillUpgradeEa = 0x0078AF68;
        private const string NativeUnionUpgradeItemName = "火龙之心";

        internal bool TryNativeUnionSkillUpgrade(TPlayObject master)
        {
            if (master == null)
                return false;

            if (!master.TryFindBagItemByStdName(NativeUnionUpgradeItemName,
                    out var heartItem))
            {
                master.SysMsg("只能在英雄包裹使用", MsgColor.Red, MsgType.Hint);
                return false;
            }

            var unionMagic = m_NativeUnionMagic;
            if (unionMagic == null)
            {
                master.SysMsg("请先将合击技能升至4级", MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (unionMagic.btLevel >= 4)
            {
                master.SysMsg("无法继续升级合击技能", MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (unionMagic.btLevel < 3)
            {
                master.SysMsg("请先将合击技能升至4级", MsgColor.Red, MsgType.Hint);
                return false;
            }

            var requiredHeroLevel = NativeUnionLevelGate[unionMagic.btLevel];
            if (m_Abil.Level < requiredHeroLevel)
            {
                master.SysMsg(
                    $"下一级合击技能提升需要英雄等级达到{requiredHeroLevel}级",
                    MsgColor.Red, MsgType.Hint);
                return false;
            }

            if (!master.DeleteBagItem(heartItem))
                return false;

            unionMagic.btLevel = 4;
            RecalcAbilitys();
            master.SysMsg(
                $"恭喜{master.m_sCharName}的英雄{m_sCharName}学会了4级合击，威力大幅提升",
                MsgColor.Green, MsgType.Hint);
            return true;
        }

        // Native word table @0x7D4DD8 indexed by next union level (sample values).
        private static readonly ushort[] NativeUnionLevelGate =
            { 0, 43, 44, 45, 46, 47, 48, 49, 50, 51 };
    }
}
