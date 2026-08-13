using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native @LesCoin idx=203 perm=5, case@0x00625D63 → sub_6C69EC.
    /// Empty p1 silent; Str_ToInt(p2,0) jle silent; GetPlayObject miss →
    /// p1 + " 不在线或不在本服务器"(0x006C6B28) cx=0xFFDB (green, not red);
    /// amount &gt; gold → clamp to gold; DecGold 0x006C7D64; log dx=0x0D;
    /// concat p1 + " 金币被减少 "(0x006C6B10) + amt, cx=0xFFDB.
    /// </summary>
    [GameCommand("LesCoin", "减少角色的金币数量", "角色名 金币数量", 5)]
    public class LesCoinCommand : BaseCommond
    {
        [DefaultCommand]
        public void LesCoin(string[] @Params, TPlayObject PlayObject)
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
                PlayObject.SysMsg(sHumName + " 不在线或不在本服务器", MsgColor.Green, MsgType.Hint);
                return;
            }
            if (nCount > target.m_nGold)
                nCount = target.m_nGold;
            target.DecGold(nCount);
            PlayObject.SysMsg(sHumName + " 金币被减少 " + nCount, MsgColor.Green, MsgType.Hint);
            if (M2Share.g_boGameLogGold)
            {
                M2Share.AddGameDataLog("13" + "\09" + PlayObject.m_sMapName + "\09" + PlayObject.m_nCurrX +
                    "\09" + PlayObject.m_nCurrY + "\09" + PlayObject.m_sCharName + "\09" +
                    Grobal2.sSTRING_GOLDNAME + "\09" + nCount + "\09" + "1" + "\09" + sHumName);
            }
        }
    }
}
