using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadLeitaiBlock", "重新加载擂台阻挡配置", "", 4)]
    public class ReloadLeitaiBlockCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadLeitaiBlock(TPlayObject PlayObject)
        {
            // 原版 @ReloadLeitaiBlock (id 563, perm 4) 派发命中 empty-exit loc_62B64C 静默 no-op：
            // jpt_622B15[563] = 0x0062B64C (xor eax,eax -> 清理 epilogue，不发任何消息)。
            // 原版此 GM 动词落在空退出分支，不改任何状态、不回消息；此前 C# 的
            // 失败红字上报属 over-send，已按 1:1 改为纯静默 no-op。
            // 证据: staging/update_clothes_4637_ida_work/big622820.txt (case 563 落入 loc_62B64C 空退出列表);
            //       staging/ida_award_case584_command_registry_20260720.txt (id 563 = ReloadLeitaiBlock)。
        }
    }
}
