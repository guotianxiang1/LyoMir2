using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native GM command 89, case 0x00624EA3 -> sub_6BFD58.
    /// The native core accepts one character name, resolves only a non-ghost
    /// ReadyRun player, clears that player's PK DWORD, refreshes the name
    /// colour, and replies only to the invoking GM.
    /// </summary>
    [GameCommand("ChgPkZero", "将某角色的PK值清零", "角色名", 4)]
    public sealed class ChgPkZeroCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgPkZero(string[] @Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;

            var targetName = @Params != null && @Params.Length > 0
                ? @Params[0] ?? string.Empty
                : string.Empty;
            var target = string.IsNullOrEmpty(targetName)
                ? null
                : M2Share.UserEngine?.GetNativeReadyPlayObject(targetName);
            if (target == null)
            {
                // sub_6BFD58 still calls the GM's SysMsg vtbl slot on the
                // empty/missing/ghost/not-ReadyRun branches.
                PlayObject.SysMsg(NativeGmPlayerAdminCommands.ChgPkZeroNotFoundMessage,
                    MsgColor.Green, MsgType.Hint);
                return;
            }

            target.m_nPkPoint = 0;
            target.RefNameColor();
            // VA 0x006BFDEC: leading space + full-width colon is part of the
            // original Delphi string and must not be normalized.
            PlayObject.SysMsg(targetName + NativeGmPlayerAdminCommands.ChgPkZeroSuccessSuffix,
                MsgColor.Green, MsgType.Hint);
        }
    }
}
