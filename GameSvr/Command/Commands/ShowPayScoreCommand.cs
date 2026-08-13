using GameSvr.CommandSystem;

namespace GameSvr
{
    [GameCommand("ShowPayScore", "显示充值积分信息", "", 4)]
    public class ShowPayScoreCommand : BaseCommond
    {
        [DefaultCommand]
        public void ShowPayScore(TPlayObject PlayObject)
        {
            // 注册表记录 0x007C0FB4 `0C "showPayScore"`，+0x18 = 310，+0x1C = 4。
            // jt[310] @0x00622FF4 = 48 b6 62 00 -> 0x0062B648：原版不回任何消息。
        }
    }
}
