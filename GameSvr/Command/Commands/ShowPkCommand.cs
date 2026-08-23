using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native GM command 90, case 0x00624EB3 -> sub_6BFE20.
    /// It reads the named non-ghost ReadyRun player's PK DWORD and sends the
    /// result only to the invoking GM.
    /// </summary>
    [GameCommand("ShowPk", "查询角色PK值", "角色名", 4)]
    public sealed class ShowPkCommand : BaseCommond
    {
        private const string MissingText = "该角色不在本GS，或不在线";

        [DefaultCommand]
        public void ShowPk(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var targetName = @Params != null && @Params.Length > 0
                ? @Params[0]
                : string.Empty;
            var target = M2Share.UserEngine?.GetNativeReadyPlayObject(targetName);
            if (target == null)
            {
                // The native miss/empty/ghost/not-ReadyRun branch uses the
                // same fixed text and confirm colour as ChgPkZero.
                PlayObject.SysMsg(MissingText, MsgColor.Green, MsgType.Hint);
                return;
            }

            // Native format string at 0x006BFECC is exactly " PK: ".
            PlayObject.SysMsg(targetName + " PK: " + target.m_nPkPoint,
                MsgColor.Green, MsgType.Hint);
        }
    }
}
