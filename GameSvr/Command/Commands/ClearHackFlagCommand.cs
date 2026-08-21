using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native GM command 151, case 0x006255EE -> sub_6D321C. It uses
    /// sub_652784's non-ghost ReadyRun lookup and preserves the original
    /// privileged-target branch, which sets rather than clears the flag.
    /// </summary>
    [GameCommand("ClearHackFlag", "设置/清除角色使用非法外挂的限制", "角色名", 4)]
    public sealed class ClearHackFlagCommand : BaseCommond
    {
        [DefaultCommand]
        public void ClearHackFlag(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var targetName = @Params != null && @Params.Length > 0
                ? @Params[0]
                : string.Empty;
            if (string.IsNullOrEmpty(targetName))
                return;

            var engine = M2Share.UserEngine;
            if (engine == null)
                return;

            var target = engine.GetNativeReadyPlayObject(targetName);
            var invokerCurrentDay = target != null &&
                                    target.m_btNativeCheatPenaltyTier == 0 &&
                                    target.m_btPermission > 3
                ? PlayObject.GetNativeTruncDaysOnline()
                : 0;
            var outcome = NativeGmClearHackFlag.Evaluate(targetName,
                target != null, target?.m_btNativeCheatPenaltyTier ?? 0,
                target?.m_btPermission ?? 0, invokerCurrentDay);

            if (outcome.MutatesTarget)
            {
                target.m_btNativeCheatPenaltyTier = outcome.StoredTier;
                target.m_nNativeCheatPenaltyExpiryDay =
                    outcome.StoredExpiryDay;
            }

            if (outcome.ClearsQuizState)
            {
                target.m_nNativeQuizCooldown = 0;
                target.m_nNativeQuizAnswerCount = 0;
            }

            if (outcome.RemovesTimedState25)
                target.RemoveNativeTimedAbilityByInternalType(25);

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
