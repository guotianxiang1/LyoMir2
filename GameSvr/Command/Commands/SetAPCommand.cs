using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @SetAP 人物名称 点数  (SetActivePoint, dispatch id 723, perm 2, case@0x0062A985 -> sub_6F9220).
    // Reversed 1:1 from M2Server_reunpacked_20260803.i64 (staging/idat_R_ap_skillexp_reload_20260803.md
    // §ITEM A). GATE = arg-nonempty + player-found only (no v548/config/state gate — CONFIRMED absent).
    // sub_6F9220(player,value): `mov [player+0x0AE4],value` = m_nActivePoint = value, NO clamp, no teleport.
    // value parse = StrToIntDef(str,0). Not online -> "玩家X不在线，无法操作".
    [GameCommand("SetAP", "设置玩家信用分", "人物名称 点数", 2)]
    public class SetAPCommand : BaseCommond
    {
        [DefaultCommand]
        public void SetAP(string[] @Params, TPlayObject PlayObject)
        {
            var sHumName = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            var sValueStr = @Params != null && @Params.Length > 1 ? @Params[1] : "";
            if (string.IsNullOrEmpty(sHumName) || string.IsNullOrEmpty(sValueStr))
            {
                PlayObject.SysMsg("命令格式：@SetActivePoint  玩家  点数", MsgColor.Red, MsgType.Hint);
                return;
            }
            var target = M2Share.UserEngine.GetPlayObject(sHumName);
            if (target == null)
            {
                PlayObject.SysMsg($"玩家{sHumName}不在线，无法操作", MsgColor.Red, MsgType.Hint);
                return;
            }
            var nValue = HUtil32.Str_ToInt(sValueStr, 0);
            // sub_6F9220: [player+0x0AE4] = value (m_nActivePoint), NO clamp.
            target.m_nActivePoint = nValue;
            PlayObject.SysMsg($"设置成功：{sHumName}当前信用分为 {nValue}", MsgColor.Green, MsgType.Hint);
        }
    }
}
