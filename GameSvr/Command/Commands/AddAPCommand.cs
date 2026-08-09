using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @AddAP 人物名称 点数  (AddActivePoint, dispatch id 726, perm 2, case@0x0062AC6A -> sub_6F91A4).
    // Reversed 1:1 from M2Server_reunpacked_20260803.i64 (staging/idat_R_ap_skillexp_reload_20260803.md
    // §ITEM A). GATE = arg-nonempty + player-found only.
    // sub_6F91A4(player,value): old=[player+0x0AE4]; if(old+value >= 0x7FFFFFFF) cap at INT_MAX,
    // else [player+0x0AE4] += value. NO floor, no teleport. This is exactly C# IncActivePoint
    // (verified by ActivePointCompatCheck: cap-at-INT_MAX + unchecked wrap otherwise).
    [GameCommand("AddAP", "增加玩家信用分", "人物名称 点数", 2)]
    public class AddAPCommand : BaseCommond
    {
        [DefaultCommand]
        public void AddAP(string[] @Params, TPlayObject PlayObject)
        {
            var sHumName = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            var sValueStr = @Params != null && @Params.Length > 1 ? @Params[1] : "";
            if (string.IsNullOrEmpty(sHumName) || string.IsNullOrEmpty(sValueStr))
            {
                PlayObject.SysMsg("命令格式：@AddActivePoint  玩家  点数", MsgColor.Red, MsgType.Hint);
                return;
            }
            var target = M2Share.UserEngine.GetPlayObject(sHumName);
            if (target == null)
            {
                PlayObject.SysMsg($"玩家{sHumName}不在线，无法操作", MsgColor.Red, MsgType.Hint);
                return;
            }
            var nValue = HUtil32.Str_ToInt(sValueStr, 0);
            // sub_6F91A4: [player+0x0AE4] += value, capped at INT_MAX, no floor.
            target.IncActivePoint(nValue);
            PlayObject.SysMsg($"增加成功：{sHumName}当前信用分为 {target.m_nActivePoint}", MsgColor.Green, MsgType.Hint);
        }
    }
}
