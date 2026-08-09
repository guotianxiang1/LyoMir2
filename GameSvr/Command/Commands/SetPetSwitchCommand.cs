using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command @SetPetSwitch (idx341, perm4).
    /// 原版派发命中 def_622B15 静默 no-op —— 不翻转任何开关、不回消息。养宠物活动开关
    /// 并未通过此 GM 动词暴露。此前的 C# 实现翻转全局 g_Config.boPetSwitch(其它子系统会读)
    /// 与原版发散，现改为纯 no-op。
    /// 证据: staging/gm_overimpl_drift_20260801.md #3; 记忆 gm-hero-pet-commands-reversed
    ///       ("SetPetSwitch/LeaveHero 的 C# 存根比原版多发，原版为 no-op")。
    /// </summary>
    [GameCommand("SetPetSwitch", "切换宠物开关", 4)]
    public class SetPetSwitchCommand : BaseCommond
    {
        [DefaultCommand]
        public void SetPetSwitch(TPlayObject PlayObject)
        {
            // 原版 def_622B15 静默 no-op（不翻转任何开关、不回消息）。见上方证据。
        }
    }
}
