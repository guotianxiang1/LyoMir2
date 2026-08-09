using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to reload daily activity configuration.
    /// Usage: @ReloadDailyActiveCfg
    /// </summary>
    [GameCommand("ReloadDailyActiveCfg", "重新加载每日活动配置", 4)]
    public class ReloadDailyActiveCfgCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadDailyActiveCfg(TPlayObject PlayObject)
        {
            // 原版 @ReloadDailyActiveCfg (id 561, perm 4) 派发命中 def_622B15 静默 no-op：
            // jpt_622B15[561] = table[0x006233E0] = 0x0062B648（mov [ebp+var_D],0 -> 清理 epilogue，
            // 不发任何消息），无真实 handler body。原版此 GM 动词不改任何状态、不回消息；此前 C# 的
            // 失败红字上报属 over-send，已按 1:1 改为纯静默 no-op。
            // 证据: staging/idat_R_ap_skillexp_reload_20260803.md §ITEM D（table[0x006233E0]=0x0062B648==DEF_SINK）。
        }
    }
}
