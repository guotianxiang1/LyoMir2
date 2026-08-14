using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // sub_6995E4 @0x006995E4 — LogoutQuest GotoLabel(player, label)
        internal void ExecuteNativeLogoutQuest()
        {
            M2Share.PasEngine?.TryCallScriptLabel(
                NativeAntiCheatHostRuntime.LogoutQuestScriptName,
                "@Main",
                this);
        }

        internal void OnNativeHostPlayerDeath()
        {
            NativeAntiCheatHostRuntime.RecordRapidDeath(this);
        }

        internal void OnNativeHostPlayerLogout()
        {
            ExecuteNativeLogoutQuest();
        }
    }
}
