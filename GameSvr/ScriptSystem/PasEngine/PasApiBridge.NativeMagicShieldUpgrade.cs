namespace GameSvr.PasEngine
{
    public partial class PasApiBridge
    {
        private bool ExecuteNativeMagicShieldUpgrade(
            IReadOnlyList<PasValue> args, bool heroUpgrade)
        {
            if (CurrentNpc == null || args?.Count != 1 ||
                args[0].ObjVal is not TPlayObject player)
                return false;

            var outcome = player.UpgradeNativeMagicShield(heroUpgrade);
            CurrentNpc.GotoLable(player,
                GetNativeMagicShieldLabel(outcome, heroUpgrade), false);
            return true;
        }

        private static string GetNativeMagicShieldLabel(
            NativeMagicShieldUpgradeOutcome outcome, bool heroUpgrade)
        {
            return outcome switch
            {
                NativeMagicShieldUpgradeOutcome.Job => "@MagicShield_job",
                NativeMagicShieldUpgradeOutcome.Level => "@MagicShield_Level",
                NativeMagicShieldUpgradeOutcome.MagicLevel =>
                    "@MagicShield_mglevel",
                NativeMagicShieldUpgradeOutcome.Finished => heroUpgrade
                    ? "@MagicShield_finish2"
                    : "@MagicShield_finish1",
                NativeMagicShieldUpgradeOutcome.Item => "@MagicShield_Item",
                NativeMagicShieldUpgradeOutcome.Success => heroUpgrade
                    ? "@MagicShield_OK2"
                    : "@MagicShield_OK1",
                _ => throw new ArgumentOutOfRangeException(nameof(outcome))
            };
        }
    }
}
