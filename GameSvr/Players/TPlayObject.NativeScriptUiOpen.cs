using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // 战神 TPsNpc script methods that do nothing but tell the clicking player's
        // client to open a UI panel. Six handlers share one byte-identical body; only
        // the ident immediate differs. sub_64001C @0x0064001C (Click_Open_Mosaic_Hole):
        //   0064001D  8B EC                 (eax = TPsNpc Self, edx = Clicker: TPlayer)
        //   00640021  8B DA                 mov ebx,edx            ; ebx = Clicker
        //   00640023  8B F0                 mov esi,eax            ; esi = the NPC
        //   00640025  85 DB / 74 29         if Clicker = nil -> ret
        //   0064002B  E8 78 2D 13 00        call 0x772DA8          ; = byte[Clicker+0x74]
        //   00640030  84 C0 / 75 1E         if death flag <> 0 -> ret
        //   00640034  80 7B 73 00 / 75 18   if byte[Clicker+0x73] (m_boGhost) <> 0 -> ret
        //   0064003A  6A 00 x4              sMsg = Series = Tag = Param = 0
        //   00640042  8B CE                 mov ecx,esi            ; Recog = Integer(NPC)
        //   00640044  66 BA EB 10           mov dx,0x10EB          ; ident 4331
        //   0064004A  8B 18                 mov ebx,[eax]
        //   0064004C  FF 93 50 02 00 00     call [ebx+0x250]       ; SendDefMessage, no body
        // The receiver is the Clicker (slot 0x250 is taken off the Clicker's vtbl), and
        // Recog is the script method's hidden Self: the registry declares these under
        // "TPsNpc / method" as `procedure Click_Open_X(Clicker: TPlayer);`, so Borland
        // register order puts Self in eax and Clicker in edx. Click_RepairEx @0x0064016C
        // pins the same order from the other side — `mov [edx+0x185C],cl` writes the
        // repair mode (3rd register = 2nd declared param) onto edx, the Clicker.
        internal const short SM_CLICK_OPEN_MOSAIC_HOLE = 4331;      // 0x10EB sub_64001C
        internal const short SM_CLICK_OPEN_DUIHUAN_CONTRI = 4339;   // 0x10F3 sub_640058
        internal const short SM_CLICK_OPEN_MYOFFIRANKUI = 4340;     // 0x10F4 sub_640094
        internal const short SM_CLICK_OPEN_ATTACHABILUI = 4348;     // 0x10FC sub_6400D0
        internal const short SM_CLICK_OPEN_FREERETRIEVE = 4351;     // 0x10FF sub_647F08
        internal const short SM_CLICK_OPEN_MIRTIANTIORDER = 4361;   // 0x1109 sub_64010C

        internal void SendNativeScriptUiOpen(short wIdent, int nNpcRecog)
        {
            if (m_boDeath || m_boGhost)
            {
                return;
            }

            SendDefMessage(wIdent, nNpcRecog, 0, 0, 0, "");
        }

        internal void SendNativeScriptRepair(TBaseObject npc, byte repairMode)
        {
            // sub_640148/sub_64016C/sub_64018C write +0x185C before the
            // shared message sender applies its ghost gate.
            m_btNativeRepairMode = repairMode;
            SendMsg(npc, Grobal2.RM_SENDUSERREPAIR,
                0, npc.ObjectId, 0, 0, string.Empty);
        }
    }
}
