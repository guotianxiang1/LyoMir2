using SystemModule;

namespace GameSvr.CommandSystem.Commands
{
    // 注册表记录 0x007C7734 `0C "ReLoadGmFile"`，+0x18 = 206，+0x1C = 5，
    // 帮助文本「重载GM列表 AdminList.txt \t @ReLoadGmFile」。
    // jt[206] @0x00622E54 = a4 5d 62 00 -> 0x00625DA4：
    //   0x00625DAB call 0x006554FC                      ; 重读 AdminList.txt
    //   0x00625DBD mov dx,0xD5 / call 0x00713890        ; 组播 ident 213
    //   0x00625DC6 mov cx,0xFFDB / edx=0x0062C2E4       ; 下面那句原文
    [GameCommand("ReLoadGmFile", "重载GM列表 AdminList.txt", 5)]
    public class ReLoadAdminCommand : BaseCommond
    {
        [DefaultCommand]
        public void ReLoadAdmin(TPlayObject playObject)
        {
            M2Share.LocalDB.LoadAdminList();
            M2Share.UserEngine.SendServerGroupMsg(213, M2Share.nServerIndex, "");
            playObject.SysMsg("这条指令将应用到全组服务器上", MsgColor.Green, MsgType.Hint);
        }
    }
}