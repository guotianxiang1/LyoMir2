using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @GetAP 人物名称  (GetActivePoint, dispatch id 729, perm 2, case@0x0062AE42 -> sub_6F9170).
    // Reversed 1:1 from M2Server_reunpacked_20260803.i64 (staging/idat_R_ap_skillexp_reload_20260803.md
    // §ITEM A). READ-ONLY (no mutation). GATE = player-found only.
    // sub_6F9170(player) = sub_61997C(player) + [player+0x0AE4] = tempBonus + base. sub_61997C is the
    // job-tier "临时信用分" config bonus == C# NativeActivityPointManager.Calculate (the exact source
    // the PAS GetActivePoint/GetTmpActivePoint bridge uses). Report fmt (color 0xFCFF):
    // "玩家%s的信用分为：%d, 其中,临时信用分为: %d" = (name, total=base+temp, temp).
    // Not online -> "X不在线, 无法操作!".
    [GameCommand("GetAP", "查看玩家信用分", "人物名称", 2)]
    public class GetAPCommand : BaseCommond
    {
        [DefaultCommand]
        public void GetAP(string[] @Params, TPlayObject PlayObject)
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
                PlayObject.SysMsg($"{sHumName}不在线, 无法操作!", MsgColor.Red, MsgType.Hint);
                return;
            }
            var nBase = target.m_nActivePoint;
            // sub_61997C: temporary (job-tier) active-point bonus == ActivityPointManager.Calculate.
            var nTemp = M2Share.ActivityPointManager?.Calculate(target) ?? 0;
            // sub_6F9170 returns temp + base (Int32, wraps like the PAS GetActivePoint bridge).
            var nTotal = unchecked(nBase + nTemp);
            PlayObject.SysMsg($"玩家{sHumName}的信用分为：{nTotal}, 其中,临时信用分为: {nTemp}",
                MsgColor.Green, MsgType.Hint);
        }
    }
}
