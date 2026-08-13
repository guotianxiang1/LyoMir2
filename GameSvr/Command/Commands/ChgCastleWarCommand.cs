using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to start a castle siege war.
    /// Usage: @ChgCastleWar CastleName
    /// Calls CastleManager to find the castle and start the wall conquest war.
    /// </summary>
    [GameCommand("ChgCastleWar", "开启攻城战役", "城堡名称", 5)]
    public class ChgCastleWarCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChgCastleWar(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sCASTLENAME = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sCASTLENAME))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            var Castle = M2Share.CastleManager.Find(sCASTLENAME);
            if (Castle != null)
            {
                // 0x625EDC cmp byte [eax+0x29],0 / jne StopWall
                // else 0x625EE9 mov byte [eax+0x2B],1  (force; Run skips the clock window)
                if (Castle.m_boUnderWar)
                {
                    Castle.StopWallconquestWar();
                }
                else
                {
                    Castle.m_boForceWar = true;
                    PlayObject.SysMsg("强制攻城设置成功，稍候生效...", MsgColor.Green, MsgType.Hint);
                }
            }
            else
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sGameCommandSbkGoldCastleNotFoundMsg, sCASTLENAME), MsgColor.Red, MsgType.Hint);
            }
        }
    }
}
