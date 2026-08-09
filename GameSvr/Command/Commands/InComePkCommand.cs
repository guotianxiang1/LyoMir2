using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @InComePk (idx91, perm4) —— 给【自身】增加 100 点 PK 值(硬编码)。
    /// 原版 case@0x00624EC3 (inline): sub_73F4BC(self, 0x64) —— self[+0x160] += 100；
    /// 若 (新值/100) 的 bucket 相对旧值改变【且】新 bucket <= 2 → 刷新外观 sub_767548；无 SysMsg。仅作用于自身。
    /// 字段: self[+0x160]=m_nPkPoint(int, TBaseObject.cs:69)；sub_767548=RefNameColor()。
    /// 证据: staging/gm_player_admin_commands_20260731.md
    ///       (InComePk 91: "self[+0x160]+=100; if /100 bucket changed AND new bucket<=2 → refresh; No SysMsg; +100 hard-coded; self only")。
    /// 补齐原因: census 有、C# 此前缺；与 @IncPkPoint("按名字按量调整+发消息")不同 —— 本命令是"自身+100静默"。
    ///           纯自身数值字段写，无经济/物品风险。(niche 自测命令，供复核取舍。)
    /// </summary>
    [GameCommand("InComePk", "自身增加100点PK值", "", 4)]
    public class InComePkCommand : BaseCommond
    {
        [DefaultCommand]
        public void InComePk(TPlayObject PlayObject)
        {
            var nOld = PlayObject.m_nPkPoint;
            PlayObject.m_nPkPoint += 100;   // 原版硬编码 +100 (sub_73F4BC(self, 0x64))
            var nNew = PlayObject.m_nPkPoint;
            // 原版仅当 /100 的 bucket 变化【且】新 bucket <= 2 时刷新外观(RefNameColor)。
            if ((nNew / 100) != (nOld / 100) && (nNew / 100) <= 2)
            {
                PlayObject.RefNameColor();
            }
        }
    }
}
