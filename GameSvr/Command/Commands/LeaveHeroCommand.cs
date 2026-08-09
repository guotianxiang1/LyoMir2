using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 原版 @LeaveHero (idx442, perm4) 派发命中 empty case (loc_62B64C) —— 什么都不做、不回消息。
    // 此前的 C# 实现 RemoveHero(self) + 日志 + SysMsg 比原版多发，与原版发散，现改为纯 no-op。
    // 证据: staging/gm_overimpl_drift_20260801.md #4; 记忆 gm-hero-pet-commands-reversed。
    [GameCommand("LeaveHero", "召回/释放英雄", "", 4)]
    public class LeaveHeroCommand : BaseCommond
    {
        [DefaultCommand]
        public void LeaveHero(TPlayObject PlayObject)
        {
            // 原版 empty case loc_62B64C 静默 no-op（不回收英雄、不回消息）。见上方证据。
        }
    }
}
