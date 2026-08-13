using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    [GameCommand("ReloadMonitemsTreeCfg", "重新加载怪物爆率树配置", "", 4)]
    public class ReloadMonitemsTreeCfgCommand : BaseCommond
    {
        /// <summary>
        /// 战神 @ReloadMonitemsTreeCfg @0x624002-0x624012:
        /// <code>
        /// 00624002  A1 9C 5D 7D 00     mov eax,[0x7D5D9C]      ; g_UserEngine
        /// 00624007  8B 00              mov eax,[eax]
        /// 00624009  E8 B2 6E 05 00     call 0x67AEC0           ; the loader
        /// 0062400E  66 B9 DB FF        mov cx,0xFFDB           ; green on transparent
        /// 00624012  BA 3C BA 62 00     mov edx,0x62BA3C        ; the reply string
        /// </code>
        /// 0x62BA3C carries Delphi length prefix 25 and bytes
        /// <c>4D 6F 6E 49 74 65 6D 73 54 72 65 65 2E 74 78 74 D6 D8 D4 D8 B3 C9 B9 A6 21</c>
        /// = "MonItemsTree.txt重载成功!" in GBK.  Native reports success unconditionally —
        /// there is no test of the loader's result.
        /// </summary>
        [DefaultCommand]
        public void ReloadMonitemsTreeCfg(TPlayObject PlayObject)
        {
            M2Share.UserEngine.ReloadMonItemsTree();
            PlayObject.SysMsg("MonItemsTree.txt重载成功!", MsgColor.Green, MsgType.Hint);
        }
    }
}
