using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 注册表记录 0x007BF034 `0E "ReloadNpcPrize"`，+0x18 = 159，+0x1C = 4。
    // 该记录的帮助文本是「重载NPC脚本奖励配置文件NormalPrize.ini \t @NormalPrize.ini」——
    // 用法行是原作者的笔误，哈希键取的是记录名 UpperCase("ReloadNpcPrize")，
    // 所以 @NormalPrize.ini 在原版里敲不出来。
    // jt[159] @0x00622D98 = bf 58 62 00 -> 0x006258BF，两条回话与下面逐字一致：
    //   0x006258CF mov cx,0xFFDB / edx=0x0062C0E8 "重载Npc脚本奖励配置文件成功"
    //   0x006258E8 mov cx,0x38FF / edx=0x0062C10C "重载奖励配置文件 NormalPrize.ini 失败，请检查。"
    [GameCommand("ReloadNpcPrize", "重载NPC脚本奖励配置文件NormalPrize.ini", "", 4)]
    public class NormalPrizeIniCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReloadNormalPrize(TPlayObject playObject)
        {
            if (GameApp.ReloadNormalPrize(out _))
            {
                playObject.SendMsg(playObject, Grobal2.RM_SYSMESSAGE,
                    0, 0xDB, 0xFF, 0, "重载Npc脚本奖励配置文件成功");
            }
            else
            {
                playObject.SendMsg(playObject, Grobal2.RM_SYSMESSAGE,
                    0, 0xFF, 0x38, 0,
                    "重载奖励配置文件 NormalPrize.ini 失败，请检查。");
            }
        }
    }
}
