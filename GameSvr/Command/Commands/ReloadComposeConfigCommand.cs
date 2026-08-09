using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to reload compose (crafting/combination) configuration.
    /// Usage: @ReloadComposeConfig
    /// </summary>
    [GameCommand("ReloadComposeConfig", "重新加载合成配置", 4)]
    public class ReloadComposeConfigCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadComposeConfig(TPlayObject PlayObject)
        {
            // 原版 @ReloadComposeConfig (id 542, perm 4) 派发命中 def_622B15 静默 no-op：
            // jpt_622B15[542] = 0x0062B648 (mov [ebp+var_D],0 -> 清理 epilogue，不发任何消息)。
            // 原版此 GM 动词落在 switch 默认空分支，不改任何状态、不回消息；此前 C# 的
            // 失败红字上报属 over-send，已按 1:1 改为纯静默 no-op。
            // 证据: staging/update_clothes_4637_ida_work/big622820.txt (case 542 def_622B15);
            //       staging/gm_address_qa_20260801.md (idx 542 -> 0x0062B648 MATCH)。
        }
    }
}
