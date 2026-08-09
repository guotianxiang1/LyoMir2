using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @ClearAP 人物名称  (ClearActivePoint, dispatch id 724, perm 2, case@0x0062AAAB
    // -> sub_6F9220(player,0) THEN sub_6CE1B8(player,"3")). Reversed 1:1 from
    // M2Server_reunpacked_20260803.i64 (staging/idat_R_ap_skillexp_reload_20260803.md §ITEM A).
    // GATE = player-found only. sub_6F9220(player,0): [player+0x0AE4] = 0 (m_nActivePoint = 0).
    // THEN sub_6CE1B8(player,"3") = MapRandomMove to town map named "3" (盟重). No clamp.
    [GameCommand("ClearAP", "清空玩家信用分并传送回盟重", "人物名称", 2)]
    public class ClearAPCommand : BaseCommond
    {
        [DefaultCommand]
        public void ClearAP(string[] @Params, TPlayObject PlayObject)
        {
            var sHumName = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sHumName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var target = M2Share.UserEngine.GetPlayObject(sHumName);
            if (target == null)
            {
                PlayObject.SysMsg($"玩家{sHumName}不在线，无法操作", MsgColor.Red, MsgType.Hint);
                return;
            }
            // sub_6F9220(player,0): clear m_nActivePoint.
            target.m_nActivePoint = 0;
            // sub_6CE1B8(player,"3"): MapRandomMove to town map "3" (盟重).
            target.MapRandomMove("3", 0);
            PlayObject.SysMsg($"清空玩家 {sHumName} 的信用分并且传送回盟重", MsgColor.Green, MsgType.Hint);
        }
    }
}
