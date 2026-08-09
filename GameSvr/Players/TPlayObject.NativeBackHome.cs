using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const string NativeRedHomeMap = "3";
        private const short NativeRedHomeX = 845;
        private const short NativeRedHomeY = 674;

        internal void ClientClickBackHome()
        {
            if (!TryResolveClientClickBackHome(out var mapName, out var x,
                    out var y, out var showFoxMapMessage))
            {
                if (showFoxMapMessage)
                {
                    SysMsg("在这里无法使用", MsgColor.Red, MsgType.Hint);
                }
                return;
            }

            SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, 0, 0, 0, string.Empty);
            BaseObjectMove(mapName, x, y);
        }

        internal bool TryResolveClientClickBackHome(out string mapName,
            out short x, out short y, out bool showFoxMapMessage)
        {
            mapName = string.Empty;
            x = 0;
            y = 0;
            showFoxMapMessage = false;

            var mapFlag = m_PEnvir?.Flag;
            if (mapFlag == null || mapFlag.boBLACKROOM ||
                mapFlag.boLIMITITEMMOVE)
            {
                return false;
            }
            // 战神 inlines the mount gate here (fn~0x6D7CB0): 0x6D9E33 mov dl,0x33 /
            // 0x6D9E38 call sub_772960 / 0x6D9E3F je (allow) / 0x6D9E48
            // cmp dword [eax+0x3c0],0 / 0x6D9E4F jne (reject), then the 0x34 leg.
            // [+0x3C0] is the two-seat mount partner POINTER (all 9 writers are in the
            // horse cluster; 0x6C5A99 derefs it and copies the name at +0x106), so the
            // int m_nNativeUnionActivationCarrier used here before could never hold it
            // and the whole first leg was dead — a solo-mounted player got through.
            if (HasNativeActiveState(0x33) && m_NativeHorsePartner != null)
            {
                return false;
            }
            if (HasNativeActiveState(0x34))
            {
                return false;
            }
            if (mapFlag.boFOXMAP)
            {
                showFoxMapMessage = true;
                return false;
            }

            if (m_nPkPoint >= M2Share.g_Config.nPKPunishPoint)
            {
                mapName = NativeRedHomeMap;
                x = NativeRedHomeX;
                y = NativeRedHomeY;
            }
            else
            {
                mapName = m_sHomeMap;
                x = m_nHomeX;
                y = m_nHomeY;
            }
            return true;
        }
    }
}
