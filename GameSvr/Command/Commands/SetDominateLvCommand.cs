using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SetDominateLv", "设置主宰等级", "人物名称 等级", 5)]
    public class SetDominateLvCommand : BaseCommond
    {
        [DefaultCommand]
        public void SetDominateLv(string[] @Params, TPlayObject PlayObject)
        {
            // 原版 @SetDominateLv (id 365, perm 5) 派发命中 empty-exit loc_62B64C 静默 no-op：
            // jpt_622B15[365] = 0x0062B64C (xor eax,eax -> 清理 epilogue，不发任何消息)。
            // 原版此 GM 动词落在空退出分支，不改任何状态、不回消息；此前 C# 的
            // 失败红字上报属 over-send，已按 1:1 改为纯静默 no-op。
            // 证据: staging/update_clothes_4637_ida_work/big622820.txt (case 365 落入 loc_62B64C 空退出列表);
            //       staging/gm_address_qa_20260801.md (idx 365 -> 0x0062B64C MATCH)。
        }
    }
}
