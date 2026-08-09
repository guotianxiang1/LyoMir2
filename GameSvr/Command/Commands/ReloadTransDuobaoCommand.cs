using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadTransDuobao", "重新加载传送夺宝配置", "", 4)]
    public class ReloadTransDuobaoCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadTransDuobao(TPlayObject PlayObject)
        {
            // 原版 @ReloadTransDuobao (id 580, perm 4) 派发命中 def_622B15 静默 no-op：
            // jpt_622B15[580] = table[0x0062342C] = 0x0062B648（mov [ebp+var_D],0 -> 清理 epilogue，
            // 不发任何消息），无真实 handler body。原版此 GM 动词不改任何状态、不回消息；此前 C# 的
            // 失败红字上报属 over-send，已按 1:1 改为纯静默 no-op。
            // 证据: staging/idat_R_ap_skillexp_reload_20260803.md §ITEM D（table[0x0062342C]=0x0062B648==DEF_SINK）。
        }
    }
}
