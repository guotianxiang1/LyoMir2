using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to reload boss monster configuration.
    /// Usage: @ReloadBossMon
    /// </summary>
    [GameCommand("ReloadBossMon", "重新加载Boss怪物配置", 4)]
    public class ReloadBossMonCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadBossMon(TPlayObject PlayObject)
        {
            // 原版 @ReloadBossMon (id 521, perm 4) 派发命中 def_622B15 静默 no-op：
            // jpt_622B15[521] = 0x0062B648 (mov [ebp+var_D],0 -> 清理 epilogue，不发任何消息)。
            // 原版此 GM 动词落在 switch 默认空分支，不改任何状态、不回消息；此前 C# 的
            // 失败红字上报属 over-send，已按 1:1 改为纯静默 no-op。
            // 证据: staging/update_clothes_4637_ida_work/big622820.txt (case 521 def_622B15);
            //       staging/gm_address_qa_20260801.md (idx 521 reloadbossmon -> 0x0062B648 MATCH)。
        }
    }
}
