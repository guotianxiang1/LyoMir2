using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native @AddCoin idx=204 perm=5, case@0x00625D76 → sub_6C6B40.
    /// 0x006C6B65 test p1 / je silent; GetPlayObject; miss → 0x38FF
    /// "角色不在线或不在本服务器上"(0x006C6C90); Str_ToInt(p2,0) jle silent;
    /// clamp amount so gold+amt &lt;= [+0x68C]; vtbl+0x28C IncGold (0x006D791C);
    /// concat p1 + " 的金币被增加 "(0x006C6C68) + amt, cx=0xFFDB; log dx=0x0E.
    /// </summary>
    [GameCommand("AddCoin", "增加角色的金币数量", "角色名 金币数量", 5)]
    public class AddCoinCommand : BaseCommond
    {
        [DefaultCommand]
        public void AddCoin(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
                return;
            var sHumName = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sHumName))
                return;
            var nCount = @Params.Length > 1 ? HUtil32.Str_ToInt(@Params[1], 0) : 0;
            if (nCount <= 0)
                return;
            var target = M2Share.UserEngine.GetPlayObject(sHumName);
            if (target == null)
            {
                PlayObject.SysMsg("角色不在线或不在本服务器上", MsgColor.Red, MsgType.Hint);
                return;
            }
            if (target.m_nGold + nCount > target.m_nGoldMax)
                nCount = target.m_nGoldMax - target.m_nGold;
            target.IncGold(nCount);
            PlayObject.SysMsg(sHumName + " 的金币被增加 " + nCount, MsgColor.Green, MsgType.Hint);
            if (M2Share.g_boGameLogGold)
            {
                M2Share.AddGameDataLog("14" + "\09" + PlayObject.m_sMapName + "\09" + PlayObject.m_nCurrX +
                    "\09" + PlayObject.m_nCurrY + "\09" + PlayObject.m_sCharName + "\09" +
                    Grobal2.sSTRING_GOLDNAME + "\09" + nCount + "\09" + "1" + "\09" + sHumName);
            }
        }
    }
}
