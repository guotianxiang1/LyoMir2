using System;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        protected bool TryApplyNativeQuickDrug(GoodItem stdItem,
            out bool refreshRequired)
        {
            refreshRequired = false;

            var canUse = m_PEnvir == null || !m_PEnvir.Flag.boNODRUG;
            if (!canUse)
                SysMsg(M2Share.sCanotUseDrugOnThisMap, MsgColor.Red, MsgType.Hint);

            if (m_WAbil.HP == 0)
                return false;

            if (HasNativeActiveState(62))
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0,
                    "你被凝冰,无法使用");
                return false;
            }

            if (!canUse)
                return false;

            CalculateNativeQuickDrugRestore(stdItem.Ac, stdItem.Mac,
                m_wNativeDrugHealthBonus, m_wNativeDrugSpellBonus,
                m_wNativeDrugJobBonus, m_btJob, out var health, out var spell);

            if (health < 0 || spell < 0)
                return true;

            if (HasNativeActiveState(102))
            {
                health /= 2;
                spell /= 2;
            }

            var nextHealth = unchecked(m_WAbil.HP + health);
            m_WAbil.HP = nextHealth >= m_WAbil.MaxHP
                ? m_WAbil.MaxHP
                : nextHealth;

            var nextSpell = unchecked(m_WAbil.MP + spell);
            m_WAbil.MP = nextSpell >= m_WAbil.MaxMP
                ? m_WAbil.MaxMP
                : nextSpell;

            refreshRequired = true;
            return true;
        }

        internal static void CalculateNativeQuickDrugRestore(ushort baseHealth,
            ushort baseSpell, ushort healthBonus, ushort spellBonus,
            ushort jobBonus, byte job, out int health, out int spell)
        {
            health = baseHealth;
            spell = baseSpell;

            if (jobBonus <= 10000)
            {
                switch (job)
                {
                    case 0:
                        health = NativeQuickDrugIntegerScale(baseHealth,
                            jobBonus + healthBonus + 100);
                        spell = NativeQuickDrugIntegerScale(baseSpell,
                            spellBonus + 100);
                        break;
                    case 1:
                        health = NativeQuickDrugIntegerScale(baseHealth,
                            healthBonus + 100);
                        spell = NativeQuickDrugIntegerScale(baseSpell,
                            jobBonus + spellBonus + 100);
                        break;
                    case 2:
                    case 3:
                        health = NativeQuickDrugHalfScale(baseHealth,
                            healthBonus, jobBonus);
                        spell = NativeQuickDrugHalfScale(baseSpell,
                            spellBonus, jobBonus);
                        break;
                }
                return;
            }

            var curve = 10000.0 / jobBonus *
                ((jobBonus - 10000) / 100.0) + 100.0;
            var healthFactor = (uint)(healthBonus + 100) / 100;
            var spellFactor = (uint)(spellBonus + 100) / 100;

            switch (job)
            {
                case 0:
                    health = (int)Math.Truncate(
                        (curve + healthFactor) * baseHealth);
                    spell = NativeQuickDrugIntegerScale(baseSpell,
                        spellBonus + 100);
                    break;
                case 1:
                    health = NativeQuickDrugIntegerScale(baseHealth,
                        healthBonus + 100);
                    spell = (int)Math.Truncate(
                        (curve + spellFactor) * baseSpell);
                    break;
                case 2:
                case 3:
                    health = (int)Math.Truncate(
                        (curve * 0.5 + healthFactor) * baseHealth);
                    spell = (int)Math.Truncate(
                        (curve * 0.5 + spellFactor) * baseSpell);
                    break;
            }
        }

        private static int NativeQuickDrugIntegerScale(ushort value, int factor)
        {
            return unchecked(value * factor) / 100;
        }

        private static int NativeQuickDrugHalfScale(ushort value,
            ushort bonus, ushort jobBonus)
        {
            var numerator = (long)Math.Truncate(
                (bonus + jobBonus * 0.5 + 100.0) * value);
            return (int)(numerator / 100);
        }
    }
}
