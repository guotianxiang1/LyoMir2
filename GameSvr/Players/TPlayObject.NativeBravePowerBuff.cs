using System;
using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 勇者之力限时 buff — native sub_7490E4 (grant) / sub_749294 (upgrade).
    /// Both call sub_7494AC to find/create a buff node on [player+4] list,
    /// arm seconds at [record+8] (0x749171 div 1000), Recalc (+0x8C), SysMsg:
    ///   grant:  "获得勇者之力：%s  剩余%d秒" @0x7491EC
    ///   upgrade:"勇者之力得到提升：%s  剩余%d秒" @0x749294
    /// </summary>
    public sealed class NativeBravePowerBuffEntry
    {
        public string Name { get; init; }
        public int RemainingSeconds { get; set; }
        public int TotalSeconds { get; set; }
    }

    public partial class TPlayObject
    {
        private readonly List<NativeBravePowerBuffEntry> _nativeBravePowerBuffs =
            new List<NativeBravePowerBuffEntry>();

        internal const uint NativeBravePowerGrantEa = 0x007490E4;
        internal const uint NativeBravePowerUpgradeEa = 0x00749294;

        internal bool TryGrantNativeBravePower(string buffName, int durationSeconds)
        {
            if (string.IsNullOrEmpty(buffName) || durationSeconds <= 0)
                return false;

            var entry = FindOrCreateBravePowerEntry(buffName);
            entry.RemainingSeconds = durationSeconds;
            entry.TotalSeconds = durationSeconds;
            RecalcAbilitys();
            SysMsg($"获得勇者之力：{buffName}  剩余{durationSeconds}秒",
                MsgColor.Green, MsgType.Hint);
            return true;
        }

        internal bool TryUpgradeNativeBravePower(string buffName, int newDurationSeconds)
        {
            if (string.IsNullOrEmpty(buffName) || newDurationSeconds <= 0)
                return false;

            var entry = FindBravePowerEntry(buffName);
            if (entry == null)
                return TryGrantNativeBravePower(buffName, newDurationSeconds);

            entry.RemainingSeconds = newDurationSeconds;
            entry.TotalSeconds = Math.Max(entry.TotalSeconds, newDurationSeconds);
            RecalcAbilitys();
            SysMsg($"勇者之力得到提升：{buffName}  剩余{newDurationSeconds}秒",
                MsgColor.Green, MsgType.Hint);
            return true;
        }

        internal void TickNativeBravePowerBuffs(int currentTick)
        {
            if (_nativeBravePowerBuffs.Count == 0)
                return;

            var changed = false;
            for (var i = _nativeBravePowerBuffs.Count - 1; i >= 0; i--)
            {
                var entry = _nativeBravePowerBuffs[i];
                if (entry.RemainingSeconds <= 0)
                    continue;

                entry.RemainingSeconds--;
                if (entry.RemainingSeconds <= 0)
                {
                    _nativeBravePowerBuffs.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
                RecalcAbilitys();
        }

        private NativeBravePowerBuffEntry FindBravePowerEntry(string name)
        {
            for (var i = 0; i < _nativeBravePowerBuffs.Count; i++)
            {
                if (string.Equals(_nativeBravePowerBuffs[i].Name, name,
                        StringComparison.Ordinal))
                    return _nativeBravePowerBuffs[i];
            }

            return null;
        }

        private NativeBravePowerBuffEntry FindOrCreateBravePowerEntry(string name)
        {
            var entry = FindBravePowerEntry(name);
            if (entry != null)
                return entry;

            entry = new NativeBravePowerBuffEntry { Name = name };
            _nativeBravePowerBuffs.Add(entry);
            return entry;
        }
    }
}
