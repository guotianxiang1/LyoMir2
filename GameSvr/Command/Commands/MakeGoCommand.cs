using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Restores native case 60 (0x00624269 -> sub_6BF02C).
    /// With no name the GM is moved; with a name the native ReadyRun player
    /// on this GS is moved.  Both successful paths use the target's current
    /// map and a random return point and remain silent.
    /// </summary>
    [GameCommand("MakeGo", "送人回城(回城点坐标随机，不指定角色名则送自己回城)", "角色名", 3)]
    public sealed class MakeGoCommand : BaseCommond
    {
        private const string NativeMissingMessage = "该角色不在本GS，或不在线";

        [DefaultCommand]
        public void MakeGo(string[] @params, TPlayObject player)
        {
            if (player == null)
                return;

            var name = @params != null && @params.Length > 0 ? @params[0] : string.Empty;
            var target = string.IsNullOrEmpty(name)
                ? player
                : M2Share.UserEngine?.GetNativeReadyPlayObject(name);
            if (target == null)
            {
                player.SysMsg(NativeMissingMessage, MsgColor.Red, MsgType.Hint);
                return;
            }

            target.MapRandomMove(target.m_sMapName, 0);
        }
    }
}
