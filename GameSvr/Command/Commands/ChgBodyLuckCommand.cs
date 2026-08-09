using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to change a player's body luck value.
    /// Usage: @ChgBodyLuck PlayerName LuckValue
    /// The luck value is ADD+clamped [-10,+5] into m_nBodyLuckLevel (native [+0x164]).
    /// </summary>
    [GameCommand("ChgBodyLuck", "调整玩家身体幸运值", "人物名称 幸运值", 4)]
    public class ChgBodyLuckCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgBodyLuck(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || @Params.Length < 2)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var sHumName = @Params[0];
            var nLuck = HUtil32.Str_ToInt(@Params[1], 0);
            if (string.IsNullOrEmpty(sHumName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var m_PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
            if (m_PlayObject == null)
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumName), MsgColor.Red, MsgType.Hint);
                return;
            }
            // 原版 @ChgBodyLuck→sub_7698BC: [+0x164] += nLuck 后 clamp[-10,+5]。native [+0x164] 即
            // 消费端读取的小值——武器升级(Merchant.cs:1045)与防御幸运(NativeMagicDamage.cs:246)都读
            // m_nBodyLuckLevel == native [+0x164]。故对 level 做 ADD+clamp；仅给 GM 调用者发消息(原版不通知目标)。
            // (逆向证据: gm-playerattr staging/gm_player_attr_commands_20260801.md — sub_7698BC + [+0x164] 消费端确认。)
            var newLuck = m_PlayObject.m_nBodyLuckLevel + nLuck;
            if (newLuck > 5) newLuck = 5;
            else if (newLuck < -10) newLuck = -10;
            m_PlayObject.m_nBodyLuckLevel = newLuck;
            PlayObject.SysMsg($"{sHumName} 的身体幸运值已调整为 {newLuck}。", MsgColor.Green, MsgType.Hint);
        }
    }
}
