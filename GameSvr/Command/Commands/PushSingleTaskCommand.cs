using GameSvr.CommandSystem;
using GameSvr.PasEngine;
using System.Globalization;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native @PushSingleTask (index 197, permission 4, case 0x006287F1).
    /// Positive activity IDs are sent to every non-ghost, non-dead online player;
    /// the invoking GM then receives the native fixed-colour confirmation.
    /// </summary>
    [GameCommand("PushSingleTask", "向在线玩家推送活动", "活动ID", 4)]
    public sealed class PushSingleTaskCommand : BaseCommond
    {
        private const string NativeMessagePrefix = "向在线玩家推送活动";

        [DefaultCommand]
        public void PushSingleTask(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || @Params.Length == 0 ||
                !PasApiBridge.TryParseNativeDelphiInteger(@Params[0], out var actId) ||
                actId <= 0)
            {
                return;
            }

            var userEngine = M2Share.UserEngine;
            if (userEngine != null)
            {
                foreach (var player in userEngine.PlayObjects)
                {
                    if (player == null || player.m_boGhost || player.m_boDeath)
                    {
                        continue;
                    }

                    player.SendDefMessage((short)Grobal2.SM_PUSH_SINGLE_TASK,
                        0, actId, 0, 0, string.Empty);
                }
            }

            // Native SysMsg passes the packed 0x38FF colour word through RM_SYSMESSAGE.
            // SendMsg's byte pair preserves that word on the existing C# wire path.
            PlayObject?.SendMsg(PlayObject, Grobal2.RM_SYSMESSAGE, 0,
                0xFF, 0x38, 0,
                NativeMessagePrefix + actId.ToString(CultureInfo.InvariantCulture));
        }
    }
}
