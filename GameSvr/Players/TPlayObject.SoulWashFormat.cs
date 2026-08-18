using System.Text;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// SoulWash attribute display — native sub_7455E4 (player) and the hero
    /// wrapper sub_6868F4 that calls it then appends hero-only lines.
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>obj+0x3C6 — 击破值, formatted at 0x745694.</summary>
        internal ushort m_wNativeBreakValue;

        /// <summary>
        /// sub_7455E4 — append four lines to <paramref name="lines"/>:
        ///   "ID：" + map coords (0x7693F0 over [+0x588]/[+0x58C])
        ///   sub_744C80 status line
        ///   "灵佑点: " + [+0x5A4]
        ///   "击破值：" + [+0x3C6]
        /// </summary>
        internal void AppendNativeSoulWashFormatLines(StringBuilder lines)
        {
            if (lines == null)
                return;

            lines.Append("ID：");
            lines.Append(m_nCurrX);
            lines.Append("  ");
            lines.Append(m_nCurrY);
            lines.AppendLine();

            lines.Append(NativeFormatSoulWashStatusLine());
            lines.AppendLine();

            lines.Append("灵佑点: ");
            lines.Append(GetSoulWashCurrent());
            lines.AppendLine();

            lines.Append("击破值：");
            lines.Append(m_wNativeBreakValue);
            lines.AppendLine();
        }

        /// <summary>sub_744C80 — compact map/level summary for the second line.</summary>
        private string NativeFormatSoulWashStatusLine()
        {
            var map = m_PEnvir?.sMapDesc ?? m_sMapName ?? string.Empty;
            return $"{map}  Lv.{m_Abil.Level}";
        }

        /// <summary>
        /// sub_6868F4 hero attribute block — calls 7455E4 on the master player,
        /// then appends:
        ///   "英雄攻击时间间隔值：" + [hero+0x320]  (via 0x40DCC0 %d formatter)
        ///   "神圣防御:%d   " + [hero+0x400]
        /// </summary>
        internal static void AppendNativeHeroSoulWashExtraLines(
            StringBuilder lines, HeroObject hero)
        {
            if (lines == null || hero == null)
                return;

            lines.Append("英雄攻击时间间隔值：");
            lines.Append(hero.m_nNativeHeroAttackInterval);
            lines.AppendLine();

            lines.Append("神圣防御:");
            lines.Append(hero.m_nNativeHeroHolyDefense);
            lines.Append("   ");
            lines.AppendLine();
        }
    }
}
