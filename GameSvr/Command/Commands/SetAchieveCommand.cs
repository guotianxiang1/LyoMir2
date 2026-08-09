using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("SetAchieve", "设置成就", "人物名称 成就ID 进度", 5)]
    public class SetAchieveCommand : BaseCommond
    {
        [DefaultCommand]
        public void SetAchieve(string[] @Params, TPlayObject PlayObject)
        {
            // 原版 @SetAchieve (id 543, perm 5) 派发命中 def_622B15 静默 no-op (0x0062B648: mov var_D,0 -> 清理 epilogue，不发任何消息)。
            // 543 号由消去法确认走 def 汇：switch 上界 cmp esi,2EEh(=750) 故 543 在跳表内有槽 (非越界默认);
            // 543 不在 loc_62B64C 空退出 case 列表 (该列表完整可见，无 543); 全 disasm 中无 "case 543" 真
            // handler 标注 (邻居 540/544/545 各自为独立真 handler，紧邻的 542=ReloadComposeConfig 已在
            // gm_address_qa 证为 def@0x0062B648 且同样落在被截断的 def 列表尾部——证明 def 列表延伸过 500)。
            // 三者取交集，543 唯一可能目的地即 def_622B15。原版不改任何状态、不回消息；此前 C# 的
            // 失败红字上报属 over-send，已按 1:1 改为纯静默 no-op。
            // 证据: staging/update_clothes_4637_ida_work/big622820.txt (0x00622B09 switch 751 cases; 全文无 case 543 handler 标注);
            //       staging/gm_address_qa_20260801.md 行241 (542 def 佐证 def 列表过 500);
            //       staging/ida_award_case584_command_registry_20260720.txt (id 543 = SETACHIEVE)。
        }
    }
}
