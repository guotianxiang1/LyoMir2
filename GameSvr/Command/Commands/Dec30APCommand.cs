using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @Dec30AP 人物名称  (dispatch id 725, perm 2, case@0x0062AB7B -> sub_6F9110(player,30)
    // THEN sub_6CE1B8(player,"3")). Reversed 1:1 from M2Server_reunpacked_20260803.i64
    // (staging/idat_R_ap_skillexp_reload_20260803.md §ITEM A). GATE = player-found only.
    // sub_6F9110(player,30): `test edx,edx; jle skip; sub [player+0x0AE4],edx` = if(30>0)
    // m_nActivePoint -= 30, NO floor clamp (may go negative). This is exactly C# DecActivePoint(30).
    // THEN sub_6CE1B8(player,"3") = MapRandomMove to town map "3" (盟重).
    [GameCommand("Dec30AP", "扣除玩家30点信用分并传送回盟重", "人物名称", 2)]
    public class Dec30APCommand : BaseCommond
    {
        [DefaultCommand]
        public void Dec30AP(string[] @Params, TPlayObject PlayObject)
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
            // sub_6F9110(player,30): m_nActivePoint -= 30, no floor.
            target.DecActivePoint(30);
            // sub_6CE1B8(player,"3"): MapRandomMove to town map "3" (盟重).
            target.MapRandomMove("3", 0);
            PlayObject.SysMsg($"扣分成功，玩家 {sHumName} 的信用分为{target.m_nActivePoint} 并且传送回盟重",
                MsgColor.Green, MsgType.Hint);
        }
    }
}
