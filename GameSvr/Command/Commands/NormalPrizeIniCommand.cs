using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("NormalPrize.ini", "重新加载NormalPrize奖励池", "", 4)]
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
