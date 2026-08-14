using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 战神 <b>property-9 商人</b>(脚本 <c>AddNpcProp(9)</c>;原生为 NPC 16 位属性集
    /// <c>[npc+0x454..0x455]</c> 的 bit 9,即 <c>test byte [npc+0x455],2</c>)的
    /// 买/卖分派器与两个「免费 + GM 限定」变体。
    ///
    /// 原生 CM_USERBUYITEM / CM_USERSELLITEM 并不直接进 <c>ClientBuyItem</c>/<c>ClientSellItem</c>,
    /// 而是各经过一个按 bit 9 二选一的瘦分派器:
    /// <code>
    /// sub_63EDE8 (买分派器, EDX/ECX 原样透传给被选中的实现)
    ///   0063EDF2  f6 86 55 04 00 00 02  test byte [esi+0x455],2
    ///   0063EDF9  74 0a                 je   0x63EE05
    ///   0063EDFB  57 / 8b c6            push edi / mov eax,esi
    ///   0063EDFE  e8 41 54 00 00        call 0x644244        ; property-9 变体
    ///   0063EE05  57 / 8b c6 / e8 ...   call 0x63EB34        ; ClientBuyItem
    ///   0063EE10  c2 04 00              ret 4
    /// sub_63F35C (卖分派器)
    ///   0063F362  f6 86 55 04 00 00 02  test byte [esi+0x455],2
    ///   0063F369  74 0a                 je   0x63F375
    ///   0063F36D  e8 16 51 00 00        call 0x644488        ; property-9 变体
    ///   0063F377  e8 84 fe ff ff        call 0x63F200        ; ClientSellItem
    /// </code>
    /// 唯一调用者分别是 <c>sub_6BAD98</c> @0x6BAE21 与 <c>sub_6B9298</c> @0x6B92EA
    /// (= C# <see cref="TPlayObject.ClientUserBuyItem"/> / <c>ClientUserSellItem</c>)。
    ///
    /// property-9 商人的货物表 <c>[npc+0x56C]</c> 是【持久化】的:脏标记 <c>[npc+0x5D0]</c>
    /// 由这两个变体置位,商人 Run 循环 @0x63E73A 每 60s(<c>0xEA60</c>)调 <c>sub_644044</c>
    /// 写 <c>NpcSave\&lt;脚本名&gt;-&lt;地图名&gt;.Sav</c>(= <see cref="SaveNativeGoodsIfDue"/>)。
    /// 同族的 <c>sub_64392C</c>(StorageAllBagItems)、<c>sub_643B20</c>(列表查询)
    /// 与本文件这两个变体【四者全部】以 <c>m_btPermission &gt; 3</c> 为前置门 ——
    /// 这是一整套 <b>GM 专用</b>的 NPC 物品寄存子系统,不是玩家可用的商店。
    /// </summary>
    public partial class Merchant
    {
        /// <summary>原生 NPC 属性集 bit 9 = <c>test byte [npc+0x455],2</c>。</summary>
        internal const int NativeStorageNpcProperty = 9;

        /// <summary>
        /// <c>sub_63EDE8</c> — CM_USERBUYITEM 的真实入口。
        /// </summary>
        public void ClientBuyItemDispatch(TPlayObject PlayObject, string sItemName, int nInt)
        {
            if (HasNativePasProperty(NativeStorageNpcProperty))
            {
                NativeStorageTakeItem(PlayObject, sItemName, nInt);
            }
            else
            {
                ClientBuyItem(PlayObject, sItemName, nInt);
            }
        }

        /// <summary>
        /// <c>sub_63F35C</c> — CM_USERSELLITEM 的真实入口。
        /// </summary>
        public bool ClientSellItemDispatch(TPlayObject PlayObject, TUserItem UserItem)
        {
            return HasNativePasProperty(NativeStorageNpcProperty)
                ? NativeStorageStoreItem(PlayObject, UserItem)
                : ClientSellItem(PlayObject, UserItem);
        }

        /// <summary>
        /// <c>sub_644244</c> — property-9 商人的「取回」:与 <see cref="ClientBuyItem"/>
        /// 逐句同构,但<b>整段没有定价、没有余额判定、没有 DecGold、没有城堡税</b>,
        /// 即无偿发货;代价是入口的 GM 门。
        /// <code>
        /// 00644271  8b 45 fc              mov eax,[ebp-4]              ; PlayObject
        /// 00644274  80 b8 75 06 00 00 03  cmp byte [eax+0x675],3       ; m_btPermission
        /// 0064427B  0f 86 d6 01 00 00     jbe 0x644457                 ; &lt;=3 静默返回(不发任何消息)
        /// 00644281  c7 45 ec 01 00 00 00  mov [ebp-0x14],1             ; n1C := 1
        /// 00644288  8b 86 6c 05 00 00     mov eax,[esi+0x56c]          ; m_GoodsList
        /// 0064432E  8b 43 1c / 8a 40 14   mov eax,[ebx+0x1c] / al,[eax+0x14]   ; StdItem.StdMode
        /// 00644334  2c 05 / 72 / 2c 1a / 74 / 2c 0b / 74  ; &lt;5 || ==0x1F || ==0x2A 则跳过 MakeIndex 比较
        /// 00644343  3b 45 08              cmp eax,[ebp+8]              ; UserItem.MakeIndex vs 请求句柄
        /// 00644357  ff 97 48 02 00 00     call [edi+0x248]             ; AddItemToBag(item, cl=1, push 0)
        /// 0064435F  0f 84 83 00 00 00     je  0x6443E8                 ; 入包失败 -> n1C := 2
        /// 0064436E  e8 bd 07 de ff        call 0x424B30                ; 内层 TList.Delete(j)
        /// 006443A9  e8 86 55 12 00        call 0x769934                ; AddGameDataLog 动作 9(无 NeedIdentify 门)
        /// 006443D5  e8 56 07 de ff        call 0x424B30                ; 空组 -> m_GoodsList.Delete(i)
        /// 006443DA  33 c0 / 89 45 ec      xor eax,eax / mov [ebp-0x14],eax     ; n1C := 0
        /// 006443DF  c6 86 d0 05 00 00 01  mov byte [esi+0x5d0],1       ; 货物表脏 -> 触发 NpcSave
        /// 0064442C  e8 37 1a 12 00        call 0x765E68  cx=0x2795     ; RM_BUYITEM_SUCCESS(10133)
        /// 00644434  e8 ab 8a 0f 00        call 0x73CEE4                ; WeightChanged
        /// 00644452  e8 11 1a 12 00        call 0x765E68  cx=0x2796     ; RM_BUYITEM_FAIL(10134)
        /// </code>
        /// 与 <see cref="ClientBuyItem"/> 的另两处差异(逐字节核对 0x644288-0x644407):
        /// 原生本函数<b>没有</b> <c>IsAddWeightAvailable</c> 前置门(0x63EBD9 的
        /// <c>call 0x73C950</c> 在此不存在),且空组回收<b>没有</b>外层的
        /// <c>cmp [grp+8],0</c> 二次判定(0x63ED06 在此不存在)。
        /// </summary>
        internal void NativeStorageTakeItem(TPlayObject PlayObject, string sItemName,
            int nInt)
        {
            // 0x644274/0x64427B:无符号 jbe,即 permission 必须 >= 4 才继续。
            // 拒绝路径直落 SEH 收尾,不回任何 RM_BUYITEM_* 消息。
            if (PlayObject.m_btPermission <= 3)
            {
                return;
            }

            var n1C = 1;
            var detailItem = ResolveShopDetailItem(PlayObject, nInt);
            for (var i = 0; i < m_GoodsList.Count; i++)
            {
                var List20 = m_GoodsList[i];
                if (List20 == null || List20.Count <= 0) continue;
                if (ItmUnit.GetItemName(List20[0]) != sItemName) continue;

                for (var j = 0; j < List20.Count; j++)
                {
                    var UserItem = List20[j];
                    // 0x64432E:原生逐件重取 StdItem(不像 ClientBuyItem 那样只取组首件)。
                    var StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                    if (StdItem == null) continue;
                    var isStaticGoods = StdItem.StdMode <= 4 ||
                                        StdItem.StdMode == 31 || StdItem.StdMode == 42;
                    if (!isStaticGoods && !ReferenceEquals(UserItem, detailItem)) continue;

                    // 0x644357:cl=1 表示允许盖印;但入口已保证 permission > 3,
                    // 而盖印器自身的门是 permission <= 3(0x6B73A3 `ja` 跳过),故必然不盖印。
                    if (!PlayObject.AddItemToBag(UserItem, 0, true))
                    {
                        n1C = 2;
                        break;
                    }

                    List20.RemoveAt(j);
                    M2Share.AddGameDataLog('9' + "\t" + PlayObject.m_sMapName + "\t" +
                        PlayObject.m_nCurrX + "\t" + PlayObject.m_nCurrY + "\t" +
                        PlayObject.m_sCharName + "\t" + StdItem.Name + "\t" +
                        UserItem.MakeIndex + "\t" + '1' + "\t" + m_sCharName);
                    if (List20.Count <= 0)
                    {
                        m_GoodsList.RemoveAt(i);
                    }
                    if (!isStaticGoods)
                    {
                        RemoveShopDetailHandle(PlayObject, nInt);
                    }
                    n1C = 0;
                    MarkNativeGoodsDirty();
                    break;
                }
                // 0x6443E6/0x6443EF/0x6443FB:名字一旦匹配,内层无论何种出口都直落函数尾,
                // 不会回到外层继续找下一组。
                break;
            }

            if (n1C == 0)
            {
                PlayObject.SendMsg(this, Grobal2.RM_BUYITEM_SUCCESS, 0,
                    PlayObject.m_nGold, nInt, 0, "");
                PlayObject.WeightChanged();
            }
            else
            {
                PlayObject.SendMsg(this, Grobal2.RM_BUYITEM_FAIL, 0, n1C, 0, 0, "");
            }
        }

        /// <summary>
        /// <c>sub_644488</c> — property-9 商人的「寄存」:与 <see cref="ClientSellItem"/>
        /// 同构,但<b>没有 GetSellItemPrice、没有 IncGold、没有城堡税</b>,即无偿收货。
        /// <code>
        /// 006444A8  c6 45 ff 00           mov byte [ebp-1],0           ; Result := False
        /// 006444AC  80 be 75 06 00 00 03  cmp byte [esi+0x675],3       ; m_btPermission
        /// 006444B3  76 6b                 jbe 0x644520                 ; &lt;=3 -> 0x2794 失败
        /// 006444C6  66 b9 93 27           mov cx,0x2793                ; RM_USERSELLITEM_OK(10131)
        /// 006444CE  e8 95 19 12 00        call 0x765E68                ; 携带 [esi+0x15c]=m_nGold
        /// 006444D7  e8 40 a5 ff ff        call 0x63EA1C                ; AddItemToGoodsList(无条件)
        /// 00644507  e8 d4 46 12 00        call 0x768BE0  dx=0x0A       ; AddGameDataLog 动作 10
        /// 0064450C  c6 45 ff 01           mov byte [ebp-1],1           ; Result := True
        /// 00644510  c6 83 d0 05 00 00 01  mov byte [ebx+0x5d0],1       ; 货物表脏
        /// 00644519  e8 c6 89 0f 00        call 0x73CEE4                ; WeightChanged
        /// 0064452C  66 b9 94 27           mov cx,0x2794                ; RM_USERSELLITEM_FAIL(10132),六参全 0
        /// </code>
        /// 对比 <see cref="ClientSellItem"/>:原生本函数的入库<b>没有</b> 0x63F2B5 的
        /// <c>sub_617A38(cl=4)</c> 前置门,数据日志也<b>没有</b> NeedIdentify 门。
        /// </summary>
        internal bool NativeStorageStoreItem(TPlayObject PlayObject, TUserItem UserItem)
        {
            var result = false;
            if (PlayObject.m_btPermission <= 3)
            {
                PlayObject.SendMsg(this, Grobal2.RM_USERSELLITEM_FAIL, 0, 0, 0, 0, "");
                return result;
            }

            PlayObject.SendMsg(this, Grobal2.RM_USERSELLITEM_OK, 0,
                PlayObject.m_nGold, 0, 0, "");
            AddItemToGoodsList(UserItem);
            var StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
            if (StdItem != null)
            {
                M2Share.AddGameDataLog("10" + "\t" + PlayObject.m_sMapName + "\t" +
                    PlayObject.m_nCurrX + "\t" + PlayObject.m_nCurrY + "\t" +
                    PlayObject.m_sCharName + "\t" + StdItem.Name + "\t" +
                    UserItem.MakeIndex + "\t" + '1' + "\t" + m_sCharName);
            }
            result = true;
            MarkNativeGoodsDirty();
            PlayObject.WeightChanged();
            return result;
        }
    }
}
