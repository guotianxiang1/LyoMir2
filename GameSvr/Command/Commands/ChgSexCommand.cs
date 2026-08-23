using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Restores native case 98 (0x00625018 -> sub_6D77F0).
    /// The original toggles only bit 0 of the character's gender byte and then
    /// sends the fixed green text "职业变更成功".  The odd text is part of the
    /// 2.08 binary contract and is intentionally preserved.
    /// </summary>
    [GameCommand("ChgSex", "更改自身性别", "", 4)]
    public sealed class ChgSexCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgSex(TPlayObject player)
        {
            if (player == null)
                return;

            player.m_btGender = (PlayGender)(((byte)player.m_btGender & 1) ^ 1);
            player.SysMsg("职业变更成功", MsgColor.Green, MsgType.Hint);
        }
    }
}
