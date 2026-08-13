using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // 注册表 idx 201 perm 5，名 "make"。jt[201] case@0x00625D32：
    //   00625D32  mov ecx,[ebp-0x38]   ; p2 数量串
    //   00625D35  mov edx,[ebp-0x34]   ; p1 物品名
    //   00625D38  mov eax,[ebp-8]      ; self
    //   00625D3B  call 0x6BDA34
    //   00625D40  jmp 0x62B64C         ; 成功路径静默，case 内无 SysMsg
    //
    // sub_6BDA34 已整段反完（0x006BDA34..0x006BDB90）：
    //   空名 / Str_ToInt(p2,1)<=0 / GetStdItem 失败 / 背包 Count>=0x30(48)
    //     → 全部静默返回，没有任何提示串。
    //   堆叠：cmp byte [item+0x14],7（pile 标记，ctor 0x788118 强制写 7）
    //     DuraMax>=剩余 → Dura=剩余并一次结束；否则 Dura=DuraMax、剩余-=DuraMax、继续循环。
    //   非堆叠：按 count 造 count 件，无 10 件上限、无 RandomUpgrade、无价格门、
    //     无攻城/安全区门、无允许/禁止制造列表。
    //   入包：push 0 / xor ecx,ecx / vtbl+0x248（不盖获取印）。
    //   日志：call 0x784568 取物品名 + call 0x768BE0 dx=5，末列字面 "0"（0x006BDB9C len=1）。
    // 「[制造物品]」全镜像 GBK/UTF-8/UTF-16LE 0 命中。
    [GameCommand("Make", "制造指定物品", "物品名 数量", 5)]
    public class MakeItemCommond : BaseCommond
    {
        private const int NativeMakeBagCap = Grobal2.MAXBAGITEM; // 0x30

        [DefaultCommand]
        public void CmdMakeItem(string[] Params, TPlayObject PlayObject)
        {
            if (PlayObject == null)
                return;
            var sItemName = Params != null && Params.Length > 0 ? Params[0] : "";
            var sCount = Params != null && Params.Length > 1 ? Params[1] : "";
            if (string.IsNullOrEmpty(sItemName))
                return;

            var remaining = HUtil32.Str_ToInt(sCount, 1);
            if (remaining <= 0)
                return;

            var loopLeft = remaining;
            var stackedDone = false;
            while (loopLeft > 0)
            {
                if (PlayObject.m_ItemList.Count >= NativeMakeBagCap)
                    return;

                TUserItem userItem = null;
                if (!M2Share.UserEngine.CopyToUserItemFromName(sItemName, ref userItem)
                    || userItem == null)
                    return;

                var stdItem = M2Share.UserEngine.GetStdItem(userItem.wIndex);
                if (stdItem == null)
                    return;

                var pile = NativeItemFactory.IsPileItem(stdItem) || stdItem.StdMode == 7;
                if (pile)
                {
                    if (userItem.DuraMax >= remaining)
                    {
                        userItem.Dura = (ushort)remaining;
                        stackedDone = true;
                    }
                    else
                    {
                        userItem.Dura = userItem.DuraMax;
                        remaining -= userItem.DuraMax;
                    }
                }

                // vtbl+0x248 with ecx=0, push 0 → 不盖获取印的内层 AddItemToBag。
                if (!PlayObject.AddItemToBag(userItem))
                    return;
                PlayObject.SendAddItem(userItem);

                var loggedCount = pile ? userItem.Dura : 1;
                M2Share.AddGameDataLog(string.Join('\t', 5,
                    PlayObject.m_sMapName, PlayObject.m_nCurrX, PlayObject.m_nCurrY,
                    PlayObject.m_sCharName, stdItem.Name, userItem.MakeIndex,
                    loggedCount, "0"));

                if (stackedDone)
                    return;
                loopLeft--;
            }
        }
    }
}
