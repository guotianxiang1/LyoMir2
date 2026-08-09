using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to reload TBB (Treasure/Boss/Buff) configuration.
    /// Usage: @ReloadTBBConfig
    /// </summary>
    [GameCommand("ReloadTBBConfig", "重新加载TBB配置", 4)]
    public class ReloadTBBConfigCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadTBBConfig(TPlayObject PlayObject)
        {
            // 原版 @ReloadTBBConfig (id 380, perm 4) 派发命中 def_622B15 静默 no-op：
            // jpt_622B15[380] = 0x0062B648 (mov [ebp+var_D],0 -> 清理 epilogue，不发任何消息)。
            // 原版此 GM 动词落在 switch 默认空分支，不改任何状态、不回消息；此前 C# 的
            // 失败红字上报属 over-send，已按 1:1 改为纯静默 no-op。
            // 证据: staging/update_clothes_4637_ida_work/big622820.txt (0x00622B0F ja def_622B15 case 列表含 380-389);
            //       staging/ida_award_case584_command_registry_20260720.txt (id 380 = ReloadTBBConfig)。
        }
    }
}
