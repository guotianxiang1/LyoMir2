using GameSvr.CommandSystem;

namespace GameSvr
{
    [GameCommand("PayScore", "扣除玩家积分", "人物名称 积分数量", 4)]
    public class PayScoreCommand : BaseCommond
    {
        [DefaultCommand]
        public void PayScore(string[] @Params, TPlayObject PlayObject)
        {
            // 注册表记录 0x007C0E94 `08 "PayScore"`，+0x18 = 295，+0x1C = 4。
            // jt[295] @0x00622FB8 = 48 b6 62 00 -> 0x0062B648 (mov byte [ebp-0x0D],0)：
            // 原版既不改积分也不发任何消息。此前的 C# 会真的扣 m_nGamePoint 并回消息。
        }
    }
}
