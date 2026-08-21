using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native GM command 153, case 0x00625690 -> sub_6D440C. The core
    /// resolves a non-ghost ReadyRun player through sub_652784, clears the
    /// two anti-cheat fields for day 0, or stores tier 3 and currentDay+7-days.
    /// It does not move the target or emit a game-data log.
    /// </summary>
    [GameCommand("HackFlag", "设置/清除角色使用非法外挂的惩罚天数(天数,@0就是清除)", "角色名 天数", 4)]
    public sealed class HackFlagCommand : BaseCommond
    {
        [DefaultCommand]
        public void HackFlag(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var targetName = @Params != null && @Params.Length > 0
                ? @Params[0]
                : string.Empty;
            var daysText = @Params != null && @Params.Length > 1
                ? @Params[1]
                : string.Empty;
            var target = string.IsNullOrEmpty(targetName)
                ? null
                : M2Share.UserEngine?.GetNativeReadyPlayObject(targetName);
            var currentDay = target?.GetNativeTruncDaysOnline() ?? 0;
            var outcome = NativeGmHackFlag.Evaluate(
                targetName, daysText, target != null, currentDay);

            if (outcome.MutatesTarget)
            {
                target.m_btNativeCheatPenaltyTier = outcome.StoredTier;
                target.m_nNativeCheatPenaltyExpiryDay = outcome.StoredExpiryDay;
            }

            if (outcome.SendsSysMsg)
            {
                var color = outcome.MessageColor ==
                    NativeGmAntiCheatCommands.ColorNotice
                    ? MsgColor.Green
                    : MsgColor.Red;
                PlayObject.SysMsg(outcome.Message, color, MsgType.Hint);
            }
        }
    }
}
