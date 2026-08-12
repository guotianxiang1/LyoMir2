using System;
using System.Collections;
using System.IO;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private void ClientQueryUserName(int targetId, int x, int y)
        {
            var BaseObject = M2Share.ObjectManager.Get(targetId);
            if (BaseObject != null && CretInNearXY(BaseObject, x, y))
            {
                var tagColor = GetCharColor(BaseObject);
                var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_USERNAME, BaseObject.ObjectId, tagColor, 0, 0);
                var uname = BaseObject.GetShowName();
                SendSocket(defMsg, uname);
            }
            else
            {
                SendDefMessage(Grobal2.SM_GHOST, targetId, x, y, 0, "");
            }
        }

        public void ClientQueryBagItems()
        {
            using var memoryStream = new MemoryStream();
            var itemCount = 0;
            // 战神 sub_64392C @ 0x64392C ("StorageAllBagItems") iterates BACKWARDS (HIGH->LOW):
            // 0x6439A3: mov esi,[eax+8]; dec esi  → esi = count-1
            // 0x643A26: dec esi; 0x643A27: cmp esi,0FFFFFFFFh; jnz → loop while esi >= 0
            for (var i = m_ItemList.Count - 1; i >= 0; i--)
            {
                var userItem = m_ItemList[i];
                if (M2Share.UserEngine.GetStdItem(userItem.wIndex) == null)
                {
                    continue;
                }

                var itemRecord = EncodeOwnedClientItemRecord(userItem);
                memoryStream.Write(itemRecord, 0, itemRecord.Length);
                itemCount++;
            }

            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_BAGITEMS, ObjectId, 0, itemCount, 0);
            SendSocket(m_DefMsg, memoryStream.ToArray());
        }

        private void ClientQueryUserSet(TProcessMessage ProcessMsg)
        {
            var sPassword = ProcessMsg.sMsg;
            if (sPassword != EDcode.DeCodeString("NbA_VsaSTRucMbAjUl"))
            {
                return;
            }
            m_nClientFlagMode = ProcessMsg.wParam;
        }

        private void ClientQueryUserState(int charId, int nX, int nY)
        {
            if (M2Share.ObjectManager.Get(charId) is not TPlayObject PlayObject)
            {
                return;
            }
            if (!CretInNearXY(PlayObject, nX, nY))
            {
                return;
            }
            foreach (var item in PlayObject.m_UseItems)
                PlayObject.EnsureClientItemId(item);
            var userState = EncodeClientUserState(
                PlayObject.GetFeature(this),
                PlayObject.m_sCharName,
                GetCharColor(PlayObject),
                PlayObject.m_MyGuild?.sGuildName,
                PlayObject.m_sGuildRankName,
                PlayObject.m_UseItems);
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SENDUSERSTATE,
                0, 1, 0, 0);
            SendSocket(m_DefMsg, userState);
        }

        internal static byte[] EncodeClientUserState(int feature, string userName,
            ushort nameColor, string guildName, string clanName, TUserItem[] useItems)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(feature);
            WriteClientShortString(writer, userName, 15);
            writer.Write((byte)nameColor);
            writer.Write((byte)0); // milrankSW
            writer.Write((byte)0); // reserved b2
            writer.Write((byte)0); // vipFlag
            WriteClientShortString(writer, guildName, 15);
            WriteClientShortString(writer, clanName, 15);

            for (var slot = 0; slot < 16; slot++)
            {
                var item = useItems != null && slot < useItems.Length ? useItems[slot] : null;
                if (item?.wIndex > 0 && M2Share.UserEngine?.GetStdItem(item.wIndex) != null)
                    writer.Write(EncodeClientItemRecord(item));
                else
                    writer.Write(new byte[16]);
            }
            return stream.ToArray();
        }

        private static void WriteClientShortString(BinaryWriter writer, string value, int maxBytes)
        {
            value ??= string.Empty;
            var bytes = HUtil32.GbkEncoding.GetBytes(value);
            while (bytes.Length > maxBytes && value.Length > 0)
            {
                value = value.Substring(0, value.Length - 1);
                bytes = HUtil32.GbkEncoding.GetBytes(value);
            }
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
            if (bytes.Length < maxBytes)
                writer.Write(new byte[maxBytes - bytes.Length]);
        }

        private void ClientMerchantDlgSelect(int nParam1, string sMsg)
        {
            if (m_boDeath || m_boGhost)
            {
                return;
            }
            NormNpc npc = (NormNpc)M2Share.UserEngine.FindMerchant(nParam1);
            if (npc == null)
            {
                npc = (NormNpc)M2Share.UserEngine.FindNPC(nParam1);
            }
            if (npc == null && M2Share.g_FunctionNPC != null)
            {
                npc = M2Share.g_FunctionNPC;
            }
            if (npc == null)
            {
                return;
            }
            if (npc.m_PEnvir == m_PEnvir && Math.Abs(npc.m_nCurrX - m_nCurrX) < 15 && Math.Abs(npc.m_nCurrY - m_nCurrY) < 15 || npc.m_boIsHide)
            {
                var selectMsg = (sMsg ?? string.Empty).Trim();
                npc.UserSelect(this, selectMsg);
            }
        }

        private NormNpc GetMerchantQueryNpc(int npcId)
        {
            var npc = M2Share.ObjectManager.Get(npcId) as NormNpc;
            if (npc == null && M2Share.g_FunctionNPC != null && M2Share.g_FunctionNPC.ObjectId == npcId)
            {
                npc = M2Share.g_FunctionNPC;
            }
            return npc;
        }

        private bool IsMerchantQueryTarget(int npcId)
        {
            return GetMerchantQueryNpc(npcId) != null;
        }

        private void ClientMerchantQuery(int nParam1, int inputType, int resultCode, string sMsg)
        {
            if (m_boDeath || m_boGhost)
            {
                return;
            }

            var npc = GetMerchantQueryNpc(nParam1);
            if (npc == null)
            {
                return;
            }

            if (!(npc.m_PEnvir == m_PEnvir && Math.Abs(npc.m_nCurrX - m_nCurrX) < 15 && Math.Abs(npc.m_nCurrY - m_nCurrY) < 15 || npc.m_boIsHide))
            {
                return;
            }

            var inputText = sMsg ?? string.Empty;
            var inputOk = resultCode != 0;

            if (M2Share.PasEngine == null)
            {
                return;
            }

            M2Share.PasEngine.TryCallNpcInputDialog(npc, inputType,
                inputText, inputOk, this, out _);
        }

        private void ClientMerchantQuerySellPrice(int nParam1, int nMakeIndex, string sMsg)
        {
            TUserItem UserItem;
            string sUserItemName;
            TUserItem UserItem18 = null;
            for (var i = 0; i < m_ItemList.Count; i++)
            {
                UserItem = m_ItemList[i];
                if (ClientItemIdMatches(UserItem, nMakeIndex))
                {
                    sUserItemName = ItmUnit.GetItemName(UserItem); 
                    if (string.Compare(sUserItemName, sMsg, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        UserItem18 = UserItem;
                        break;
                    }
                }
            }
            if (UserItem18 == null)
            {
                return;
            }
            Merchant merchant = (Merchant)M2Share.UserEngine.FindMerchant(nParam1);
            if (merchant == null)
            {
                return;
            }
            if (merchant.m_PEnvir == m_PEnvir && merchant.m_boSell && Math.Abs(merchant.m_nCurrX - m_nCurrX) < 15 && Math.Abs(merchant.m_nCurrY - m_nCurrY) < 15)
            {
                merchant.ClientQuerySellPrice(this, UserItem18);
            }
        }

        private void ClientUserSellItem(int nParam1, int nMakeIndex, string sMsg)
        {
            TUserItem UserItem;
            Merchant Merchant;
            for (var i = 0; i < m_ItemList.Count; i++)
            {
                UserItem = m_ItemList[i];
                if (ClientItemIdMatches(UserItem, nMakeIndex))
                {
                    var sUserItemName = ItmUnit.GetItemName(UserItem);
                    if (string.Compare(sUserItemName, sMsg, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        Merchant = (Merchant)M2Share.UserEngine.FindMerchant(nParam1);
                        if (Merchant != null && Merchant.m_boSell && Merchant.m_PEnvir == m_PEnvir && Math.Abs(Merchant.m_nCurrX - m_nCurrX) < 15 && Math.Abs(Merchant.m_nCurrY - m_nCurrY) < 15)
                        {
                            if (Merchant.ClientSellItem(this, UserItem))
                            {
                                if (UserItem.btValue[13] == 1)
                                {
                                    M2Share.ItemUnit.DelCustomItemName(UserItem.MakeIndex, UserItem.wIndex);
                                    UserItem.btValue[13] = 0;
                                }
                                UserItem = null; 
                                m_ItemList.RemoveAt(i);
                                WeightChanged();
                            }
                        }
                        break;
                    }
                }
            }
        }

        private void ClientUserBuyItem(int nIdent, int nParam1, int nInt, int nZz, string sMsg)
        {
            try
            {
                if (m_boDealing)
                {
                    return;
                }
                var merchant = (Merchant)M2Share.UserEngine.FindMerchant(nParam1);
                if (merchant == null || !merchant.m_boBuy || merchant.m_PEnvir != m_PEnvir || Math.Abs(merchant.m_nCurrX - m_nCurrX) > 15 || Math.Abs(merchant.m_nCurrY - m_nCurrY) > 15)
                {
                    return;
                }
                switch (nIdent)
                {
                    case Grobal2.CM_USERBUYITEM:
                        merchant.ClientBuyItem(this, sMsg, nInt);
                        break;
                    case Grobal2.CM_USERGETDETAILITEM:
                        merchant.ClientGetDetailGoodsList(this, sMsg, nZz);
                        break;
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage("TUserHumah.ClientUserBuyItem wIdent = " + nIdent);
                M2Share.ErrorMessage(e.Message);
            }
        }

        private bool ClientDropGold(int nGold)
        {
            var yanshenNoDrop = new YanshenApi(this, null, M2Share.PluginManager).IsSafeNoDrop();
            if ((M2Share.g_Config.boInSafeDisableDrop || yanshenNoDrop) && InSafeZone())
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sCanotDropInSafeZoneMsg);
                return false;
            }
            if (M2Share.g_Config.boControlDropItem && nGold < M2Share.g_Config.nCanDropGold)
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sCanotDropGoldMsg);
                return false;
            }
            if (!m_boCanDrop || m_PEnvir.Flag.boNOTHROWITEM)
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sCanotDropItemMsg);
                return false;
            }
            if (nGold >= m_nGold)
            {
                return false;
            }
            // TRADE-09: Cancel active trade before gold reduction (战神 behavior).
            if (m_DealCreat != null)
            {
                DealCancel();
            }
            m_nGold -= nGold;
            if (!DropGoldDown(nGold, false, null, this))
            {
                m_nGold += nGold;
            }
            GoldChanged();
            return true;
        }

        private bool ClientDropItem(string sItemName, int nItemIdx)
        {
            TUserItem UserItem;
            GoodItem StdItem;
            string sUserItemName;
            var result = false;
            if (!m_boClientFlag)
            {
                if (m_nStep == 8)
                {
                    m_nStep++;
                }
                else
                {
                    m_nStep = 0;
                }
            }
            var yanshenNoDrop = new YanshenApi(this, null, M2Share.PluginManager).IsSafeNoDrop();
            if ((M2Share.g_Config.boInSafeDisableDrop || yanshenNoDrop) && InSafeZone())
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sCanotDropInSafeZoneMsg);
                return result;
            }
            if (!m_boCanDrop || m_PEnvir.Flag.boNOTHROWITEM)
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sCanotDropItemMsg);
                return result;
            }
            // ⚠️ UNVERIFIED —— 来源=GameOfMir 参考分支(非战神),仅算术形态线索,未经战神字节验证。
            // 原引用(保留,勿删): ObjBase.pas:16240 `if Pos(' ', sItemName) > 0`——Delphi Pos 是【1 基】，
            // 首字符命中返回 1，故 ">0" 的语义是"任意位置找到空格"。C# IndexOf 是【0 基】，
            // 沿用 ">0" 会漏掉空格在首位的情况，等价写法是 ">= 0"。
            // 该修正属于【语言语义类】(Delphi Pos 1 基 vs C# IndexOf 0 基),这一层可靠;
            // 但"战神此处确有这个 Pos 分支"本身未经字节验证 —— 战神 CM_DROPITEM = sub_73CC98
            // (staging/discovery_itemlifecycle_20260803.md 行 25/33-37),那轮 dump 只覆盖了
            // 传输权限门 sub_78389C(mode 5)、未认证/赠品销毁分支与倒序遍历,【没有】出现物品名
            // 空格切分。故本分支在战神是否存在仍未知。列入活风险清单(物品, 低)。
            if (sItemName.IndexOf(' ') >= 0)
            {
                
                HUtil32.GetValidStr3(sItemName, ref sItemName, new string[] { " " });
            }
            if ((HUtil32.GetTickCount() - m_DealLastTick) > 3000)
            {
                for (var i = 0; i < m_ItemList.Count; i++)
                {
                    UserItem = m_ItemList[i];
                    if (ClientItemIdMatches(UserItem, nItemIdx))
                    {
                        StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                        if (StdItem == null)
                        {
                            continue;
                        }
                        sUserItemName = ItmUnit.GetItemName(UserItem);// 鍙栬嚜瀹氫箟鐗╁搧鍚嶇О
                        if (string.Compare(sUserItemName, sItemName, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (M2Share.g_Config.boControlDropItem && StdItem.Price < M2Share.g_Config.nCanDropPrice)
                            {
                                Dispose(UserItem);
                                m_ItemList.RemoveAt(i);
                                result = true;
                                break;
                            }
                            // 战神 sub_73CC98 @0x73CD23-0x73CDFB — the auth + gift DESTROY
                            // branch that C# was missing entirely.  Native:
                            //   0x73CD23  cmp byte [esi+0x178],0 / jne 0x73CDFD   ; non-player -> normal drop
                            //   0x73CD37  mov cl,4 / call sub_617A38              ; authenticated?
                            //   0x73CD42  test al,al / je 0x73CD51                ; NOT authed -> destroy
                            //   0x73CD44  cmp byte [ebx+0xD8],0 / je 0x73CDFD     ; authed + not gift -> normal drop
                            //   0x73CD73  call sub_424B30                         ; remove from bag
                            //   0x73CDE4  call sub_768BE0(dx=0x5E)                ; the GBK notice
                            //   0x73CDEB  call sub_404690                         ; TObject.Free — NEVER DropItemDown
                            // Without it a mule can throw a gift/unverified item on the
                            // floor for an alt to collect (bind & gift laundering).
                            var dropAuthenticated = NativeItemDropDestroyAuthenticated();
                            if (NativeItemDropDestroy.ShouldDestroy(
                                    m_btRaceServer == Grobal2.RC_PLAYOBJECT,
                                    dropAuthenticated, UserItem))
                            {
                                var notice = NativeItemDropDestroy.BuildDestroyNotice(
                                    dropAuthenticated, UserItem,
                                    NativeItemDropDestroy.DropUnverifiedNotice,
                                    NativeItemDropDestroy.DropGiftNotice);
                                m_ItemList.RemoveAt(i);         // 0x73CD73
                                if (!string.IsNullOrEmpty(notice))
                                {
                                    // 0x73CDDE `mov dx,0x5E` / 0x73CDE4 sub_768BE0
                                    SysMsg(notice + " " + sUserItemName,
                                        MsgColor.Red, MsgType.Hint);
                                }
                                Dispose(UserItem);              // 0x73CDEB sub_404690 (Free)
                                result = true;
                                break;
                            }
                            // 战神 sub_73CC98 @0x73CD51: `mov cl,[esi+0x4B7]; mov edx,5;
                            // call sub_78389C; test eax,eax; jne 0x73CE36` — a non-zero
                            // classification is a PER-ITEM continue (0x73CE36 is the loop
                            // back-edge `dec edi; cmp edi,-1; jne`), not an abort.
                            if (NativeItemDropDestroy.CheckTransferPermission(UserItem,
                                    StdItem, NativeItemDropDestroy.TransferModeDrop) != 0)
                            {
                                continue;
                            }
                            if (DropItemDown(UserItem, 1, false, null, this))
                            {
                                Dispose(UserItem);
                                m_ItemList.RemoveAt(i);
                                result = true;
                                break;
                            }
                        }
                    }
                }
                if (result)
                {
                    WeightChanged();
                }
            }
            return result;
        }

        private bool ClientChangeDir(short wIdent, int nX, int nY, int nDir, ref int dwDelayTime)
        {
            var result = false;
            // MOVE-15 — turn (case 3010) opens with the can-act call
            // `call [ecx+0x40]` at 0x6D9B6C, arg dl=0 (`xor edx,edx` at
            // 0x6D9B65), before it even reads the requested direction from
            // byte[msg+0xA] at 0x6D9B76. So the cast lock blocks a turn too.
            // Native's refusal here pushes FOUR ZEROS before `mov dx,0x276`
            // (0x6D9B94-0x6D9B9E) — the turn correction carries no
            // coordinates, unlike walk/run.
            if (IsNativeCanActBlockedByForcedMove())
            {
                return result;
            }
            if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0)
            {
                return result;
            }
            if (!CheckActionStatus(wIdent, ref dwDelayTime))
            {
                m_boFilterAction = false;
                return result;
            }
            m_boFilterAction = true;
            if (!M2Share.g_Config.boSpeedHackCheck)
            {
                var dwCheckTime = HUtil32.GetTickCount() - m_dwTurnTick;
                if (dwCheckTime < M2Share.g_Config.dwTurnIntervalTime)
                {
                    dwDelayTime = M2Share.g_Config.dwTurnIntervalTime - dwCheckTime;
                    return result;
                }
            }
            if (nX == m_nCurrX && nY == m_nCurrY)
            {
                m_btDirection = (byte)nDir;
                if (Walk(Grobal2.RM_TURN))
                {
                    m_dwTurnTick = HUtil32.GetTickCount();
                    result = true;
                }
            }
            return result;
        }

        private bool ClientSitDownHit(int nX, int nY, int nDir, ref int dwDelayTime)
        {
            // MOVE-15 — pose/sit (case 3012) is the fourth and last native
            // vmt+0x40 site: `mov dl,1` at 0x6D9C7D then `call [ecx+0x40]` at
            // 0x6D9C84, and it is the ONLY gate that case has. Its refusal
            // also pushes four zeros (0x6D9C8B) before `mov dx,0x276`
            // at 0x6D9C95.
            if (IsNativeCanActBlockedByForcedMove())
            {
                return false;
            }
            if (m_boDeath || m_wStatusTimeArr[Grobal2.POISON_STONE] != 0)// 闃查夯
            {
                return false;
            }
            if (!M2Share.g_Config.boSpeedHackCheck)
            {
                var dwCheckTime = HUtil32.GetTickCount() - m_dwTurnTick;
                if (dwCheckTime < M2Share.g_Config.dwTurnIntervalTime)
                {
                    dwDelayTime = M2Share.g_Config.dwTurnIntervalTime - dwCheckTime;
                    return false;
                }
                m_dwTurnTick = HUtil32.GetTickCount();
            }
            SendRefMsg(Grobal2.RM_SPELL2, 0, 0, 0, 0, "");
            return true;
        }

        private void ClientOpenDoor(int nX, int nY)
        {
            var door = m_PEnvir.GetDoor(nX, nY);
            if (door == null)
            {
                return;
            }
            var Castle = M2Share.CastleManager.IsCastleEnvir(m_PEnvir);
            if (Castle == null || Castle.m_DoorStatus != door.Status || m_btRaceServer != Grobal2.RC_PLAYOBJECT || Castle.CheckInPalace(m_nCurrX, m_nCurrY, this))
            {
                M2Share.UserEngine.OpenDoor(m_PEnvir, nX, nY);
            }
            else
            {
                SendDefMessage(Grobal2.SM_OPENDOOR_LOCK, nX, nY, 0, 0, "");
            }
        }

        private void ClientTakeOnItems(byte btWhere, int nItemIdx, string sItemName)
        {
            var n14 = -1;
            var n18 = 0;
            TUserItem UserItem = null;
            TUserItem TakeOffItem = null;
            GoodItem StdItem = null;
            GoodItem StdItem20 = null;
            TStdItem StdItem58 = null;
            string sUserItemName;
            for (var i = 0; i < m_ItemList.Count; i++)
            {
                UserItem = m_ItemList[i];
                if (ClientItemIdMatches(UserItem, nItemIdx))
                {
                    StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                    sUserItemName = ItmUnit.GetItemName(UserItem);
                    if (StdItem != null)
                    {
                        if (string.Compare(sUserItemName, sItemName, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            n14 = i;
                            break;
                        }
                    }
                }
                UserItem = null;
            }
            if (StdItem != null && UserItem != null)
            {
                if (M2Share.CheckUserItems(btWhere, StdItem))
                {
                    StdItem.GetStandardItem(ref StdItem58);
                    StdItem.GetItemAddValue(UserItem, ref StdItem58);
                    StdItem58.Name = ItmUnit.GetItemName(UserItem);
                    if (CheckTakeOnItems(btWhere, ref StdItem58) && CheckItemBindUse(UserItem))
                    {
                        TakeOffItem = null;
                        if (btWhere < m_UseItems.Length)
                        {
                            if (m_UseItems[btWhere] != null && m_UseItems[btWhere].wIndex > 0)
                            {
                                StdItem20 = M2Share.UserEngine.GetStdItem(m_UseItems[btWhere].wIndex);
                                if (StdItem20 != null && new ArrayList(new byte[] { 15, 19, 20, 21, 22, 23, 24, 26 }).Contains(StdItem20.StdMode))
                                {
                                    if (!m_boUserUnLockDurg && m_UseItems[btWhere].btValue[7] != 0)
                                    {
                                        
                                        SysMsg(M2Share.g_sCanotTakeOffItem, MsgColor.Red, MsgType.Hint);
                                        n18 = -4;
                                        goto FailExit;
                                    }
                                }
                                if (!m_boUserUnLockDurg && (StdItem20.Reserved & 2) != 0)
                                {
                                    
                                    SysMsg(M2Share.g_sCanotTakeOffItem, MsgColor.Red, MsgType.Hint);
                                    n18 = -4;
                                    goto FailExit;
                                }
                                if ((StdItem20.Reserved & 4) != 0)
                                {
                                    
                                    SysMsg(M2Share.g_sCanotTakeOffItem, MsgColor.Red, MsgType.Hint);
                                    n18 = -4;
                                    goto FailExit;
                                }
                                if (M2Share.InDisableTakeOffList(m_UseItems[btWhere].wIndex))
                                {
                                    
                                    SysMsg(M2Share.g_sCanotTakeOffItem, MsgColor.Red, MsgType.Hint);
                                    goto FailExit;
                                }
                                TakeOffItem = m_UseItems[btWhere];
                            }
                            if (new ArrayList(new byte[] { 15, 19, 20, 21, 22, 23, 24, 26 }).Contains(StdItem.StdMode) && UserItem.btValue[8] != 0)
                            {
                                UserItem.btValue[8] = 0;
                            }
                            // 战神 sub_6B7E9C @0x6B7F8F: `call sub_75F044(&displaced)` — the
                            // executor does slot-clear (sub_75E9EC) and slot-write
                            // (0x75F085 `mov [ebx+eax*4+8],esi`) ATOMICALLY inside one
                            // call, returning the displaced item through the out-param.
                            // Then @0x6B7FA2 `call sub_73D140` removes the new item from
                            // the bag (`sub_425020` TList.Remove; `inc eax; setne al`), and
                            // only afterwards @0x6B8041/@0x6B804C does
                            // `push 0; xor ecx,ecx; call [vmt+0x248]` = AddItemToBag(displaced).
                            //
                            // C# previously wrote m_UseItems[btWhere] BEFORE DelBagItem,
                            // so an exception in between left the item in BOTH containers
                            // (a dupe), and it DISCARDED the AddItemToBag result, so a full
                            // bag mid-swap silently destroyed the displaced gear.
                            var previousSlotItem = m_UseItems[btWhere];
                            m_UseItems[btWhere] = UserItem;         // 0x75F085
                            DelBagItem(n14);                        // 0x6B7FA2 sub_73D140
                            if (TakeOffItem != null)
                            {
                                // 0x6B804C `test al,al` — native CHECKS the add.  On failure
                                // roll the whole swap back rather than lose the old gear:
                                // restore the slot and put the new item back in the bag,
                                // then report the native takeon-fail code.
                                if (!AddItemToBag(TakeOffItem,
                                        NativeItemAcquisitionStamp.Reason.None, false))
                                {
                                    m_UseItems[btWhere] = previousSlotItem;
                                    m_ItemList.Insert(Math.Min(n14, m_ItemList.Count),
                                        UserItem);
                                    WeightChanged();
                                    n18 = -3;
                                    goto FailExit;
                                }
                                SendAddItem(TakeOffItem);
                            }
                            RecalcAbilitys();
                            SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_TAKEON_OK, GetFeatureToLong(), GetFeatureEx(), 0, 0);
                            SendSocket(m_DefMsg, GetMobileFeature());
                            WeightChanged();
                            FeatureChanged();
                            n18 = 1;
                        }
                    }
                    else
                    {
                        n18 = -1;
                    }
                }
                else
                {
                    n18 = -1;
                }
            }
        FailExit:
            if (n18 <= 0)
            {
                SendDefMessage(Grobal2.SM_TAKEON_FAIL, n18, 0, 0, 0, "");
            }
        }

        private void ClientTakeOffItems(byte btWhere, int nItemIdx, string sItemName)
        {
            var n10 = 0;
            GoodItem StdItem = null;
            TUserItem UserItem = null;
            string sUserItemName;
            if (!m_boDealing && btWhere < m_UseItems.Length)
            {
                if (m_UseItems[btWhere]?.wIndex > 0)
                {
                    if (ClientItemIdMatches(m_UseItems[btWhere], nItemIdx))
                    {
                        StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[btWhere].wIndex);
                        if (StdItem != null && new ArrayList(new byte[] { 15, 19, 20, 21, 22, 23, 24, 26 }).Contains(StdItem.StdMode))
                        {
                            if (!m_boUserUnLockDurg && m_UseItems[btWhere].btValue[7] != 0)
                            {
                                
                                SysMsg(M2Share.g_sCanotTakeOffItem, MsgColor.Red, MsgType.Hint);
                                n10 = -4;
                                goto FailExit;
                            }
                        }
                        if (!m_boUserUnLockDurg && (StdItem.Reserved & 2) != 0)
                        {
                            
                            SysMsg(M2Share.g_sCanotTakeOffItem, MsgColor.Red, MsgType.Hint);
                            n10 = -4;
                            goto FailExit;
                        }
                        if ((StdItem.Reserved & 4) != 0)
                        {
                            
                            SysMsg(M2Share.g_sCanotTakeOffItem, MsgColor.Red, MsgType.Hint);
                            n10 = -4;
                            goto FailExit;
                        }
                        if (M2Share.InDisableTakeOffList(m_UseItems[btWhere].wIndex))
                        {
                            
                            SysMsg(M2Share.g_sCanotTakeOffItem, MsgColor.Red, MsgType.Hint);
                            goto FailExit;
                        }
                        
                        sUserItemName = ItmUnit.GetItemName(m_UseItems[btWhere]);
                        if (string.Compare(sUserItemName, sItemName, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            // 战神 sub_6B8188 @0x6B81F0: `mov dl,1; call [vmt+0x244]`
                            // (= sub_6D0AE8, `Count+1 <= 48`) `; test al,al; je 0x6B82DD`
                            // where 0x6B82DD does `mov esi,0xFFFFFFFD` — the BAG-SPACE GATE
                            // RUNS FIRST and rejects with -3 while NOTHING has been mutated
                            // (the slot is only cleared later, inside sub_75E9EC at
                            // 0x6B8213).  C# had no pre-gate: it called AddItemToBag and on
                            // failure ran `Dispose(UserItem)` — native has no free there.
                            if (!IsEnoughBag())
                            {
                                n10 = -3;
                                goto FailExit;
                            }
                            UserItem = m_UseItems[btWhere];
                            // 0x6B822D `push 0; xor ecx,ecx; call [vmt+0x248]` — the outer
                            // AddItemToBag, reason 0, stamper disabled.
                            if (AddItemToBag(UserItem,
                                    NativeItemAcquisitionStamp.Reason.None, false))
                            {
                                SendAddItem(UserItem);

                                m_UseItems[btWhere] = null;
                                RecalcAbilitys();
                                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                                m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_TAKEOFF_OK, GetFeatureToLong(), GetFeatureEx(), 0, 0);
                                SendSocket(m_DefMsg, GetMobileFeature());
                                FeatureChanged();
                                n10 = 1;
                                if (M2Share.g_FunctionNPC != null)
                                {
                                    M2Share.g_FunctionNPC.GotoLable(this, "@TakeOff" + sItemName, false);
                                }
                            }
                            else
                            {
                                // 0x6B82C3: `mov esi,0xFFFFFFFD` then
                                // `call sub_75F174` = RESTORE the slot.  Native NEVER frees
                                // the player's item on a failed move, so the previous
                                // `Dispose(UserItem)` is deleted; the slot still holds the
                                // item (it is only nulled in the success branch), which is
                                // exactly what sub_75F174 re-establishes natively.
                                n10 = -3;
                            }
                        }
                    }
                }
                else
                {
                    n10 = -2;
                }
            }
            else
            {
                n10 = -1;
            }
        FailExit:
            if (n10 <= 0)
            {
                SendDefMessage(Grobal2.SM_TAKEOFF_FAIL, n10, 0, 0, 0, "");
            }
        }

        private string ClientUseItems_GetUnbindItemName(int nShape)
        {
            var result = string.Empty;
            if (M2Share.g_UnbindList.TryGetValue(nShape, out result))
            {
                return result;
            }
            return result;
        }

        private bool ClientUseItems_GetUnBindItems(string sItemName, int nCount)
        {
            var result = false;
            TUserItem UserItem;
            for (var i = 0; i < nCount; i++)
            {
                UserItem = new TUserItem();
                if (M2Share.UserEngine.CopyToUserItemFromName(sItemName, ref UserItem))
                {
                    m_ItemList.Add(UserItem);
                    if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        SendAddItem(UserItem);
                    }
                    result = true;
                }
                else
                {
                    Dispose(UserItem);
                    break;
                }
            }
            return result;
        }

        private void ClientUseItems(int itemId, int useMode)
        {
            if (!m_boCanUseItem)
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sCanotUseItemMsg);
                SendDefMessage(Grobal2.SM_EAT_FAIL, itemId, 0, 0, 0, "");
                return;
            }

            if (m_boDeath)
            {
                SendDefMessage(Grobal2.SM_EAT_FAIL, itemId, 0, 0, 0, "");
                return;
            }

            var item = FindClientItemIn(m_ItemList, itemId)
                       ?? FindClientItemIn(m_ItemList, itemId, true);
            var stdItem = item == null ? null : M2Share.UserEngine.GetStdItem(item.wIndex);
            if (item == null || stdItem == null)
            {
                SendDefMessage(Grobal2.SM_EAT_FAIL, itemId, 0, 0, 0, "");
                return;
            }

            // Native sub_6B8380 switches on the wire Param[+6] use-mode (fixed at the CM_EAT dispatch
            // to nParam2). The three "off_75Exxx" operands of sub_404828 are NOT name strings (the
            // 5mode-doc's original blocker) -- they are Delphi class-reference VMTs and sub_404828 is
            // the `is` operator (RTTI-confirmed via staging/ida_eng047_vmt_rtti_dump.txt), so the C#
            // equivalent is NativeItemFactory.GetClassName(...)==<class>:
            //   mode2 off_75E5DC -> TVessel ; mode3 off_75E6CC -> TUnionItem ; mode4 off_75E200 -> TMarkStoneCharm.
            // Full analysis (all four mode bodies + offsets): staging/item_use_5mode_fix_20260802.md.
            bool itemUsed;
            switch (useMode)
            {
                case 0:
                    itemUsed = TryUseItemEffect(stdItem, item);
                    if (itemUsed)
                    {
                        NotifyItemActivePoint(stdItem.Name);
                    }
                    break;
                case 2:
                    // sub_6B8380 case 2 -> sub_763704: refill the worn U_BUJUK(9) TVessel by a FIXED
                    // +100 when <consumed> is its reciprocal refill token. Gate predicate is byte-for-byte
                    // the CM_1017 merge candidate test; on success push RM_DURACHANGE(9) then the shared tail.
                    itemUsed = TryClientUseVesselRefill(item);
                    break;
                case 3:
                    // sub_6B8380 case 3 -> sub_7637D8: refill the worn U_BUJUK(9) TUnionItem by
                    // consumed.Dura when amulet.wIndex == consumed.wIndex (native primary branch). The
                    // native alternate (amulet item+0x108 != 0 && == consumed.wIndex) reads an UNMODELED
                    // runtime field, so it is fail-closed (rejected) -- a safe subset, never a false consume.
                    itemUsed = TryClientUseUnionRefill(item);
                    break;
                case 4:
                    // sub_6B8380 case 4 -> sub_763B64: "charge" the worn U_CHARM(12) TMarkStoneCharm gem with a
                    // StdMode-7/Shape-3/Sc-0 charger token. The charge CORE is fully grounded (K = gemStd.Mac ?:
                    // gemStd.Ac ?: 10; delta = consumedStd.Ac*consumed.Dura; gem.Dura = round((delta+gem.Dura*K)/K,
                    // ToEven); std Ac/Mac/Sc @ +0x24/+0x28/+0x34; player+0x178 = m_btRaceServer). CONSERVATION
                    // RESOLVED (idat): the consumed charger's runtime KIND byte (+0x14) is 0 -- the base ctor
                    // sub_783788 writes +0x14=0, and ONLY the pile ctor sub_7880F0 overwrites +0x14=7 (StdMode>=150).
                    // So the shared consume tail removes the charger WHOLESALE (1 per use), which is exactly what
                    // C# IsNativePileItem(StdMode>=150)=false already yields -> no stack destruction. Live.
                    itemUsed = TryClientUseCharmCharge(item);
                    break;
                case 1:
                    // sub_6B8380 case 1 (0x006B845C): eax = [Self+0xBB0] = m_HeroObject; a null hero falls to
                    // the default no-op (fail). Otherwise sub_6866BC(a1=hero, consumed) refills the MASTER'S
                    // HERO's worn U_BUJUK(9) TDragonHeart amulet from the powerupItem.ini {wIndex->refill}
                    // table (sub_763840 -> sub_74E0A0), sends the hero's RM_DURACHANGE(9) (=SM_HERO_DURACHANGE),
                    // and returns true so the shared consume tail removes the token from the MASTER'S bag. No
                    // hero / no amulet / amulet full / absent key => false (no mutation, no consume) = native.
                    itemUsed = m_HeroObject != null && m_HeroObject.TryNativeHeroAmuletRefill(item);
                    break;
                default: // mode > 4 is a native no-op
                    itemUsed = false;
                    break;
            }

            if (!itemUsed)
            {
                SendDefMessage(Grobal2.SM_EAT_FAIL, itemId, 0, 0, 0, "");
                return;
            }

            var removeWholeItem = true;
            if (IsNativePileItem(stdItem) && item.Dura > 0)
            {
                item.Dura--;
                removeWholeItem = item.Dura == 0;
            }

            if (removeWholeItem)
            {
                m_ItemList.Remove(item);
                Dispose(item);
                SendDefMessage(Grobal2.SM_EAT_OK, itemId, 0, 0, 0, "");
            }
            else
            {
                // The native M2 sends EAT_FAIL to cancel the client's optimistic removal,
                // followed by an item durability refresh for pile items.
                SendDefMessage(Grobal2.SM_EAT_FAIL, itemId, 0, 0, 0, "");
                SendDefMessage(Grobal2.SM_BAGITEMDURACHG,
                    itemId, item.Dura, item.DuraMax, 0, "");
            }

            WeightChanged();
            if (stdItem.NeedIdentify == 1)
            {
                M2Share.AddGameDataLog("11" + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + stdItem.Name + "\t" + itemId + "\t" + '1' + "\t" + '0');
            }
        }

        // Native sub_6B8380 case 2 gate `sub_404828(GetUseItem(9), off_75E5DC=TVessel)` + body sub_763704.
        // Refills the worn U_BUJUK(9) vessel by a FIXED +100 when <consumed> is its reciprocal refill token.
        // sub_763704's three inner gates (btValue[10..11]==0, NOT(consumed StdMode2/Shape10/Dura!=0),
        // reciprocal AniCount<->wIndex pair) are byte-identical to the CM_1017 merge candidate test, so the
        // audited IsNativeItemMergeCandidate(vessel, refill) is reused verbatim. Conservation: mutates ONLY
        // the worn vessel's Dura (never creates/destroys an item); the consumed token is handled solely by
        // the shared consume tail, and only after this returns true. All gates are checked before any write.
        private bool TryClientUseVesselRefill(TUserItem consumed)
        {
            if (m_UseItems == null || Grobal2.U_BUJUK >= m_UseItems.Length)
                return false;
            var amulet = m_UseItems[Grobal2.U_BUJUK];               // sub_75EC20(*(Self+0x4C0), 9)
            if (amulet == null)                                     // native `is` on nil vessel => false
                return false;
            var amuletStd = M2Share.UserEngine.GetStdItem(amulet.wIndex);
            if (NativeItemFactory.GetClassName(amuletStd) != "TVessel")   // sub_404828(amulet, off_75E5DC)
                return false;
            if (amulet.Dura >= amulet.DuraMax)                      // sub_763704: requires dura < duramax
                return false;
            if (!IsNativeItemMergeCandidate(amulet, consumed))      // reciprocal pair + StdMode2/Shape10 excl + gate word
                return false;
            var refilled = (ushort)(amulet.Dura + NativeItemMerge.MergeIncrement);  // += 100, native u16 add
            if (refilled > amulet.DuraMax)                          // native unsigned clamp to duramax
                refilled = amulet.DuraMax;
            amulet.Dura = refilled;
            // sub_765E68(...,9): RM_DURACHANGE for the refilled slot-9 vessel (cx=0x278D=10125).
            SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_BUJUK, amulet.Dura, amulet.DuraMax, 0, "");
            return true;
        }

        // Native sub_6B8380 case 3 gate `sub_404828(GetUseItem(9), off_75E6CC=TUnionItem)` + body sub_7637D8.
        // Refills the worn U_BUJUK(9) union item by consumed.Dura when it is the same union index (primary
        // branch: amulet.StdItem.wIndex == consumed.item+0x24). item+0x24 is the item's wIndex -- confirmed
        // by the runtime layout {wIndex@+0x24, Dura@+0x26, DuraMax@+0x28, btValue@+0x2A} whose btValue[10..11]
        // lands at +0x34, matching the CM_1017 merge-gate word. The native alternate branch
        // (amulet.item+0x108 != 0 && == consumed.wIndex) reads an UNMODELED runtime field, so it is
        // fail-closed here (rejected) -- a strict, conservation-safe subset (no false consume, no item loss).
        private bool TryClientUseUnionRefill(TUserItem consumed)
        {
            if (m_UseItems == null || Grobal2.U_BUJUK >= m_UseItems.Length)
                return false;
            var amulet = m_UseItems[Grobal2.U_BUJUK];               // sub_75EC20(*(Self+0x4C0), 9); native requires !=0
            if (amulet == null)
                return false;
            var amuletStd = M2Share.UserEngine.GetStdItem(amulet.wIndex);
            if (NativeItemFactory.GetClassName(amuletStd) != "TUnionItem")   // sub_404828(amulet, off_75E6CC)
                return false;
            if (amulet.Dura >= amulet.DuraMax)                      // sub_7637D8: requires dura < duramax
                return false;
            if (amulet.wIndex != consumed.wIndex)                   // native primary branch (alt +0x108 fail-closed)
                return false;
            var sum = (ushort)(consumed.Dura + amulet.Dura);        // sub_7637D8: v4 = consumed.Dura + amulet.Dura
            if (sum > amulet.DuraMax)                               // only when it fits (native: v4 <= duramax)
                return false;
            amulet.Dura = sum;
            SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_BUJUK, amulet.Dura, amulet.DuraMax, 0, "");
            return true;
        }

        // Native sub_6B8380 case 4 gate `sub_404828(GetUseItem(12), off_75E200=TMarkStoneCharm)` + body sub_763B64.
        // "Charges" the worn U_CHARM(12) gem: consumes a StdMode-7/Shape-3/Sc-0 charger token to raise the gem's
        // persistent charge (Dura) by delta = consumedStd.Ac * consumed.Dura, scaled by K = gemStd.Mac ?: Ac ?: 10.
        // Byte-derived from staging/idat_bounded_close.txt (sub_763B64 @0x763B64, sub_764560 @0x764560,
        // sub_765E68 RM_DURACHANGE @0x765E68). All native branches reproduced:
        //   a3(self).m_btRaceServer in {0,54}  (always true for a CM_EAT player, kept for fidelity)
        //   gem.Dura >= DuraMax                 -> "您的%s持久已满,无需填充!" (cx 0x38FF), no charge
        //   NOT(StdMode7 && Shape3 && Sc0)      -> "不能使用此物品来填充" (cx 0x38FF), no charge
        //   gem.DuraMax*K < delta + gem.Dura*K  -> "%s持久超过%s需填充的持久,无法填充" (cx 0xFFDB), no charge
        //   else: gem.Dura = round((delta + gem.Dura*K)/K, ToEven) clamp DuraMax; RM_DURACHANGE(12);
        //         success "您为%s中填充了1个%s，持久增加了%d！" (cx 0x38FF); WeightChanged iff old gem.Dura < 2.
        // CONSERVATION: gem.Dura is the ONLY in-place mutation; the consumed charger is touched solely by the
        // shared consume tail on a true return. Its runtime KIND byte (+0x14) is 0 (base ctor sub_783788), NOT 7
        // (only the pile ctor sub_7880F0 writes 7 for StdMode>=150), so the tail removes it wholesale (1 per use) =
        // C# IsNativePileItem(StdMode>=150)=false. long math avoids C# overflow; the fit-gate keeps it in range.
        private bool TryClientUseCharmCharge(TUserItem consumed)
        {
            if (m_UseItems == null || Grobal2.U_CHARM >= m_UseItems.Length)
                return false;
            var gem = m_UseItems[Grobal2.U_CHARM];                     // sub_75EC20(*(Self+0x4C0), 12); native a1 (!=0)
            if (gem == null)
                return false;
            if (m_btRaceServer != 0 && m_btRaceServer != 54)           // native gate `!n54 || n54==54` on self+0x178
                return false;
            var gemStd = M2Share.UserEngine.GetStdItem(gem.wIndex);
            if (gemStd == null)
                return false;
            if (NativeItemFactory.GetClassName(gemStd) != "TMarkStoneCharm")   // native sub_404828(gem,off_75E200): 充能槽须 IS-A TMarkStoneCharm(与 mode-1 门 TDragonHeart 对称)。漏此门=太宽松(2026-08-03 idatP 揪出的真背离)。
                return false;
            if (gem.Dura >= gem.DuraMax)                               // gem already full: feedback, no charge/consume
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0,
                    "您的" + gemStd.Name + "持久已满,无需填充!");
                return false;
            }
            var consumedStd = M2Share.UserEngine.GetStdItem(consumed.wIndex);
            if (consumedStd == null || consumedStd.StdMode != 7 || consumedStd.Shape != 3 || consumedStd.Sc != 0)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0, "不能使用此物品来填充");
                return false;
            }
            int k = gemStd.Mac != 0 ? gemStd.Mac : (gemStd.Ac != 0 ? gemStd.Ac : 10);   // sub_764560: Mac(+0x28) ?: Ac(+0x24) ?: 10
            long delta = (long)consumedStd.Ac * consumed.Dura;                            // consumedStd.Ac(+0x24) * consumed.Dura
            long gemDuraTimesK = (long)gem.Dura * k;
            long numerator = delta + gemDuraTimesK;                                       // delta + gem.Dura*K
            if ((long)gem.DuraMax * k < numerator)                                        // native: fits iff DuraMax*K >= numerator
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xDB, 0xFF, 0,
                    consumedStd.Name + "持久超过" + gemStd.Name + "需填充的持久,无法填充");
                return false;
            }
            var newDura = (int)Math.Round((double)numerator / k, MidpointRounding.ToEven);   // fistp = round-half-to-even
            if (newDura > gem.DuraMax)                                                    // native DuraMax clamp
                newDura = gem.DuraMax;
            gem.Dura = (ushort)newDura;
            SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_CHARM, gem.Dura, gem.DuraMax, 0, "");   // sub_765E68(...,0x278D,...,DuraMax,Dura,12)
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0,
                "您为" + gemStd.Name + "中填充了1个" + consumedStd.Name + "，持久增加了" + delta + "！");
            if (gemDuraTimesK < 2L * k)                                                   // native: WeightChanged when old gem.Dura*K < 2*K (i.e. old Dura 0/1)
                WeightChanged();
            return true;
        }

        private void NotifyItemActivePoint(string itemName)
        {
            NotifyPlayerActivePoint(0, itemName, 0, 0);
        }

        private void NotifyPlayerActivePoint(int payType, string payName,
            int payNum, int payNo)
        {
            var scriptHost = M2Share.PasEngine;
            var scriptPath = scriptHost?.FindScriptFile("RunQuest");
            if (scriptPath == null) return;

            scriptHost.CallProcedure(scriptPath, "PlayerActivePoint", this, null,
                PasEngine.PasValue.FromInt(payType),
                PasEngine.PasValue.FromInt(payNo),
                PasEngine.PasValue.FromInt(payNum),
                PasEngine.PasValue.FromString(payName ?? string.Empty));
        }

        private bool TryUseItemEffect(GoodItem stdItem, TUserItem item)
        {
            var nativeClass = NativeItemFactory.GetClassName(stdItem);
            if (nativeClass == null) return false;

            var scriptHost = M2Share.PasEngine;
            var scriptPath = scriptHost?.FindItemScriptFile(stdItem.Name);
            if (scriptPath != null)
            {
                return scriptHost.TryCallItemProcedure(scriptPath, "UseItem", this, item, out var result)
                       && result.AsBool();
            }

            switch (nativeClass)
            {
                case "TNoEffectItem":
                    return true;
                case "TSlowDrug":
                    return UseNativeSlowDrug(stdItem);
                case "TQuickDrug":
                    return UseNativeQuickDrug(stdItem);
                case "TShengShui":
                    return UseNativeShengShui(stdItem);
                case "TMoveScroll":
                    return EatUseItems(stdItem.Shape);
                case "TLuckOil":
                    return EatUseItems(4);
                case "TRepairOil":
                    return EatUseItems(stdItem.Shape);
                case "TGoldActCred":
                    return UseNativeGoldActCredential();
                case "TSkillBook":
                    if (!ReadBook(stdItem)) return false;
                    EnableNewlyLearnedSkills();
                    return true;
                case "TFixedCoordStone":
                    // 定位石 recall. Native reaches this through the class VMT slot +0x18
                    // (pointer at 0x7827D4 in VMT 0x7827BC) = sub_78A014; StdMode 1 /
                    // Shape 35 (=0x23) is what NativeItemFactory maps to this class,
                    // matching the setter's own gate at 0x6E9C6D/0x6E9C77.
                    return UseNativeFixedCoordStone(item);
                case "TDoubleExpProp":
                    // 倍经验卷轴. VMT 0x77F288 slot +0x18 = sub_786390. Multiplier
                    // from byte[std+0x17] clamped 2..0x40 (0x7863D6/0x7863DB, else
                    // 2); hours from word[std+0x1C]/1000 (0x786483/0x78648E).
                    return UseNativeDoubleExpProp(stdItem);
                case "TAntiDecExpProp":
                    // 防经验衰减卷轴. VMT 0x77F3A8 slot +0x18 = sub_7865B4. Sends
                    // NO message on any path (there is no vmt+0xD4 call in the
                    // whole 0x6B-byte function), so this really is silent.
                    return GrantNativeAntiDecExp(stdItem.DuraMax);
                case "TColorSayProp":
                    // 彩色文字. VMT 0x77F7C8 slot +0x18 (SelfPtr self-checked,
                    // class name reads TColorSayProp) = sub_786800. Shapes
                    // 23/24/25 are exactly what NativeItemFactory maps here, and
                    // the granter's own `sub al,0x16` turns them into tiers
                    // 1/2/3 -- matching the three-way select in the say path at
                    // 0x6C9448/0x6C9454. Duration comes from DuraMax, not Shape.
                    return GrantNativeColorSay(stdItem.Shape, stdItem.DuraMax);
                default:
                    return false;
            }
        }

        /// <summary>
        /// <c>TDoubleExpProp.Use</c> = <c>sub_786390</c> (VMT 0x77F288 slot +0x18).
        /// Wraps <see cref="GrantNativeExpBuff"/> with the item-derived arguments
        /// and the two user-visible messages.
        /// <para>
        /// Colours are NOT the same on both paths and that is deliberate: the
        /// refusal uses <c>0x786430 mov cx,0x38FF</c> (FColor 0xFF / BColor 0x38 =
        /// Red) while the success notice uses <c>0x7864EF mov cx,0xFCFF</c>
        /// (BColor 0xFC = Blue). Both go out through <c>vmt+0xD4</c>, i.e. to self
        /// only. The success path additionally emits packet <c>dx=0x277D</c> /
        /// <c>cx=0x4C</c> through <c>vmt+0xD8</c> at 0x7864BF, which carries no
        /// text.
        /// </para>
        /// <para>
        /// The conflict message formats the ACTIVE multiplier
        /// (<c>0x7863FA mov eax,[ebx+0xBBC]</c>) and the item name, not the
        /// attempted multiplier. The success message formats hours then the NEW
        /// multiplier (<c>0x7864C9</c> reads the spilled hours, <c>0x7864D3</c>
        /// takes esi).
        /// </para>
        /// </summary>
        private bool UseNativeDoubleExpProp(GoodItem stdItem)
        {
            // 0x7863D2 movzx esi,byte [std+0x17] then the 2..0x40 clamp.
            // GoodItem.AniCount is a ushort in C# (the loader was deliberately
            // widened) but the native StdItem field at +0x17 is a single BYTE, so
            // the faithful reduction is TRUNCATION, not clamping -- the same
            // `unchecked((byte)AniCount)` convention GoodItem.cs:82 already uses
            // when it packs the wire struct.
            var multiplier = NativeResolveGrantMultiplier(
                unchecked((byte)stdItem.AniCount));
            // 0x786483 movzx eax,word [std+0x1C] / 0x78648E div 1000 (unsigned,
            // truncating -- a DuraMax below 1000 grants zero hours but still
            // succeeds, exactly as with the colour-say granter)
            var hours = stdItem.DuraMax / 1000;

            switch (GrantNativeExpBuff(hours, multiplier))
            {
                case NativeExpBuffGrantOutcome.MultiplierConflict:
                    // 0x786428 Format(0x78653C, activeMultiplier, itemName)
                    // The literal is Delphi `%d`/`%s`, so string.Format would be
                    // a silent no-op -- substitute positionally instead.
                    SysMsg(NativeFormatSequential(M2Share.g_sNativeExpBuffConflict,
                            m_nNativeExpBuffMultiplier, stdItem.Name),
                        MsgColor.Red, MsgType.Hint);
                    return false;
                case NativeExpBuffGrantOutcome.NetCafeRefusal:
                    // 0x78645C mov cx,0x38FF / 0x786460 mov edx,0x786564. The
                    // literal has no format specifiers, so it goes out verbatim.
                    SysMsg(M2Share.g_sNativeExpBuffNetCafeRefusal,
                        MsgColor.Red, MsgType.Hint);
                    return false;
                case NativeExpBuffGrantOutcome.OverCap:
                    // 0x78647E jg -> False with NO message at all.
                    return false;
                default:
                    // 0x7864E7 Format(0x786598, hours, newMultiplier), colour 0xFCFF
                    SysMsg(NativeFormatSequential(M2Share.g_sNativeExpBuffGranted,
                            hours, multiplier),
                        MsgColor.Blue, MsgType.Hint);
                    return true;
            }
        }

        private bool UseNativeGoldActCredential()
        {
            if (m_btGoldActNextLevel != 0)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0,
                    "你已经是热血勇士");
                return false;
            }

            m_btGoldActNextLevel = 1;
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0,
                "本角色成功升级为热血勇士！");
            return true;
        }

        private bool CanUseNativeDrug()
        {
            if (m_PEnvir == null || !m_PEnvir.Flag.boNODRUG) return true;
            SysMsg(M2Share.sCanotUseDrugOnThisMap, MsgColor.Red, MsgType.Hint);
            return false;
        }

        private bool UseNativeSlowDrug(GoodItem stdItem)
        {
            if (!CanUseNativeDrug() || m_WAbil.HP == 0) return false;
            m_nIncHealth = HUtil32._MIN(500, m_nIncHealth + stdItem.Ac);
            m_nIncSpell = HUtil32._MIN(500, m_nIncSpell + stdItem.Mac);
            return true;
        }

        private bool UseNativeQuickDrug(GoodItem stdItem)
        {
            if (!TryApplyNativeQuickDrug(stdItem, out var refreshRequired))
                return false;
            if (refreshRequired)
                HealthSpellChanged();
            return true;
        }

        private bool UseNativeShengShui(GoodItem stdItem)
        {
            if (!CanUseNativeDrug() || m_WAbil.HP == 0) return false;
            m_boUserUnLockDurg = true;
            return true;
        }

        private void EnableNewlyLearnedSkills()
        {
            if (m_MagicArr[SpellsDef.SKILL_ERGUM] != null && !m_boUseThrusting)
            {
                ThrustingOnOff(true);
                SendSocket("+LNG");
            }
            if (m_MagicArr[SpellsDef.SKILL_BANWOL] != null && !m_boUseHalfMoon)
            {
                HalfMoonOnOff(true);
                SendSocket("+WID");
            }
            if (m_MagicArr[SpellsDef.SKILL_REDBANWOL] != null && !m_boRedUseHalfMoon)
            {
                RedHalfMoonOnOff(true);
                SendSocket("+WID");
            }
        }

        private static bool IsNativePileItem(GoodItem stdItem)
        {
            return NativeItemFactory.IsPileItem(stdItem);
        }

        private bool ClientGetButchItem(int charId, int nX, int nY, byte btDir, ref int dwDelayTime)
        {
            var result = false;
            dwDelayTime = 0;
            var BaseObject = M2Share.ObjectManager.Get(charId);
            if (!M2Share.g_Config.boSpeedHackCheck)
            {
                var dwCheckTime = HUtil32.GetTickCount() - m_dwTurnTick;
                if (dwCheckTime < HUtil32._MAX(150, M2Share.g_Config.dwTurnIntervalTime - 150))
                {
                    dwDelayTime = HUtil32._MAX(150, M2Share.g_Config.dwTurnIntervalTime - 150) - dwCheckTime;
                    return result;
                }
                m_dwTurnTick = HUtil32.GetTickCount();
            }
            if (Math.Abs(nX - m_nCurrX) <= 2 && Math.Abs(nY - m_nCurrY) <= 2)
            {
                if (m_PEnvir.IsValidObject(nX, nY, 2, BaseObject))
                {
                    if (BaseObject.m_boDeath && !BaseObject.m_boSkeleton && BaseObject.m_boAnimal)
                    {
                        var n10 = M2Share.RandomNumber.Random(16) + 5;
                        var n14 = M2Share.RandomNumber.Random(201) + 100;
                        BaseObject.m_nBodyLeathery -= n10;
                        BaseObject.m_nMeatQuality -= (ushort)n14;
                        if (BaseObject.m_nMeatQuality < 0)
                        {
                            BaseObject.m_nMeatQuality = 0;
                        }
                        if (BaseObject.m_nBodyLeathery <= 0)
                        {
                            if (BaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL && BaseObject.m_btRaceServer < Grobal2.RC_MONSTER)
                            {
                                BaseObject.m_boSkeleton = true;
                                ApplyMeatQuality();
                                BaseObject.SendRefMsg(Grobal2.RM_SKELETON, BaseObject.m_btDirection, BaseObject.m_nCurrX, BaseObject.m_nCurrY, 0, "");
                            }
                            if (!TakeBagItems(BaseObject))
                            {
                                SysMsg(M2Share.sYouFoundNothing, MsgColor.Red, MsgType.Hint);
                            }
                            BaseObject.m_nBodyLeathery = 50;
                        }
                        BaseObject.m_dwDeathTick = HUtil32.GetTickCount();
                    }
                }
                m_btDirection = btDir;
            }
            SendRefMsg(Grobal2.RM_BUTCH, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
            return result;
        }

        private void ClientChangeMagicKey(int nSkillIdx, int nKey)
        {
            TUserMagic UserMagic;
            for (var i = 0; i < m_MagicList.Count; i++)
            {
                UserMagic = m_MagicList[i];
                if (UserMagic.MagicInfo.wMagicID == nSkillIdx)
                {
                    UserMagic.btKey = (byte)nKey;
                    break;
                }
            }
        }

        private void ClientGroupClose()
        {
            if (m_GroupOwner == null)
            {
                m_boAllowGroup = false;
                return;
            }
            if (m_GroupOwner != this)
            {
                m_GroupOwner.DelMember(this);
                m_boAllowGroup = false;
            }
            else
            {
                SysMsg("如果你想退出，使用编组功能（删除按钮）", MsgColor.Red, MsgType.Hint);
            }
            if (M2Share.g_FunctionNPC != null)
            {
                M2Share.g_FunctionNPC.GotoLable(this, "@GroupClose", false);
            }
        }

        // 战神 sub_6C341C = ident 1020 CM_CREATEGROUP (dispatch 0x6D9072 -> 6D9092 call 0x6c341c).
        // The native body's ONLY state-changing call is sub_6F39B4 = "queue a pending request";
        // exhaustive E8-callee enumeration of sub_6C341C = {405500 4059C0 40C140 652784 6C3380
        // 6C33CC 6F39B4} contains neither sub_726B80 (allocate group) nor sub_7272EC (insert
        // member) nor sub_6C3648 (create-on-accept). The group therefore materialises ONLY from
        // the 4412 accept (sub_6F3EA8: 6F3F2E call 0x6c3648). Previously C# force-joined the
        // target here, so any player could be dragged into a party by one packet.
        private void ClientCreateGroup(string sHumName)
        {
            // 6C3449 call sub_6C3380 = self precheck; it also zero-inits the code slot at 6C338E.
            if (!CheckNativeGroupSelfEligibility(out var error))
            {
                SendDefMessage(Grobal2.SM_CREATEGROUP_FAIL, error, 0, 0, 0, "");
                return;
            }
            var PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
            if (PlayObject == null)
            {
                // 6C3470 je 0x6c349a -> 6C349A mov [ebp-8],0xFFFFFFFE
                SendDefMessage(Grobal2.SM_CREATEGROUP_FAIL, -2, 0, 0, 0, "");
                return;
            }
            if (PlayObject == this)
            {
                // 6C3472 cmp ebx,esi / je 0x6c3491 -> 6C3491 mov [ebp-8],0xFFFFFFF6 = -10
                SendDefMessage(Grobal2.SM_CREATEGROUP_FAIL, -10, 0, 0, 0, "");
                return;
            }
            // 6C347B call sub_6C33CC = target precheck (-4 before -3, see the helper).
            if (!CheckNativeGroupTargetEligibility(PlayObject, out error))
            {
                SendDefMessage(Grobal2.SM_CREATEGROUP_FAIL, error, 0, 0, 0, "");
                return;
            }
            // 6C3484 xor ecx,ecx / 6C3486 mov edx,ebx / 6C3488 mov eax,esi / 6C348A call 0x6f39b4
            // => sub_6F39B4(target=esi, requester=ebx=self, type=cl=0). Success is SILENT: there is
            // no SM_CREATEGROUP_OK (0x294) anywhere in sub_6C341C; 0x294 is sent only by
            // sub_6C3648 (6C36E5 mov dx,0x294) once the invitee accepts.
            PlayObject.QueueNativeGroupRequest(this, 0);
            if (M2Share.g_FunctionNPC != null)
            {
                M2Share.g_FunctionNPC.GotoLable(this, "@GroupCreate", false);
            }
        }

        // 战神 sub_6C34EC = ident 1021 CM_ADDGROUPMEMBER (dispatch 0x6D909C -> 6D90BC call 0x6c34ec).
        // Same queue-an-invite shape as 1020; callee set = {405500 4059C0 40C140 652784 6B7BAC
        // 6BBE84 6C33CC 6F39B4}, again with no group-mutating callee.
        private void ClientAddGroupMember(string sHumName)
        {
            // 6C3516 call sub_6BBE84 / 6C351D jne 0x6c35ba => restricted self returns SILENTLY
            // (the jump lands past the whole reply block).
            if (IsNativeGroupRestricted(this))
            {
                return;
            }
            // 6C3525 call sub_6B7BAC / 6C352C je 0x6c3594 -> 6C3594 mov [ebp-8],0xFFFFFFFF
            if (m_GroupOwner != this)
            {
                SendDefMessage(Grobal2.SM_GROUPADDMEM_FAIL, -1, 0, 0, 0, "");
                return;
            }
            // 6C352E mov eax,[ebx+0xA80] / 6C3534 cmp dword [eax+0x44],0xB / 6C3538 jge 0x6c358b
            // -> 6C358B mov [ebp-8],0xFFFFFFFB = -5. Native's bound is the hard 11-slot array.
            if (m_GroupMembers.Count >= NativeGroupMaxMembers)
            {
                SendDefMessage(Grobal2.SM_GROUPADDMEM_FAIL, -5, 0, 0, 0, "");
                return;
            }
            var PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
            if (PlayObject == null)
            {
                // 6C3558 je 0x6c3582 -> 6C3582 mov [ebp-8],0xFFFFFFFE
                SendDefMessage(Grobal2.SM_GROUPADDMEM_FAIL, -2, 0, 0, 0, "");
                return;
            }
            if (PlayObject == this)
            {
                // 6C355A cmp ebx,esi / je 0x6c3579 -> 6C3579 mov [ebp-8],0xFFFFFFF6 = -10
                SendDefMessage(Grobal2.SM_GROUPADDMEM_FAIL, -10, 0, 0, 0, "");
                return;
            }
            // 6C3563 call sub_6C33CC
            if (!CheckNativeGroupTargetEligibility(PlayObject, out var error))
            {
                SendDefMessage(Grobal2.SM_GROUPADDMEM_FAIL, error, 0, 0, 0, "");
                return;
            }
            // 6C356C xor ecx,ecx / 6C3570 mov eax,esi / 6C3572 call 0x6f39b4 => type-0 invite.
            //
            // ⚠️ THE FOLLOWING SILENCE IS *NOT* A DECODED NATIVE VALUE — IT IS A CHOSEN
            //    DEVIATION FROM UNDEFINED BEHAVIOUR. Do not cite it as a 战神 fact.
            //    sub_6C34EC never initialises its [ebp-8] result slot: 1020's sub_6C3380 zeroes
            //    the slot for free (6C338E mov [esi],eax), but sub_6C33CC does NOT zero [edi].
            //    So on 1021's queue-success path (6C3577 jmp 0x6c359b) the native test
            //    6C359B cmp [ebp-8],0 reads an UNINITIALISED stack slot, and whether native
            //    emits a garbage SM_GROUPADDMEM_FAIL(0x298) depends on leftover stack contents —
            //    it is not reproducible and there is no "correct" value to port.
            //    We deliberately choose 1020's deterministic behaviour (silent success). Any
            //    future "native sends X here" claim must come from a fresh trace, not from this
            //    comment.
            PlayObject.QueueNativeGroupRequest(this, 0);
            if (M2Share.g_FunctionNPC != null)
            {
                M2Share.g_FunctionNPC.GotoLable(this, "@GroupAddMember", false);
            }
        }

        // 战神 sub_6C33CC (target eligibility). Gate ORDER matters: -4 covers three distinct
        // conditions and is emitted BEFORE the already-grouped -3.
        //   6C33D8 cmp byte [esi+0xBA1],0 / je 0x6c33f8          => !m_boAllowGroup      -> -4
        //   6C33E1 mov eax,[esi+0x128] / 6C33E7 cmp byte [eax+0x7C],0 / jne 0x6c33f8
        //                                                        => map "no group" flag  -> -4
        //   6C33EF call sub_6BBE84 / je 0x6c3402                 => restricted           -> -4
        //   6C3402 cmp dword [esi+0xA80],0 / je 0x6c3413 -> 6C340B mov [edi],0xFFFFFFFD  -> -3
        private static bool CheckNativeGroupTargetEligibility(TPlayObject target,
            out int error)
        {
            if (!target.m_boAllowGroup || IsNativeGroupMapDenied(target)
                || IsNativeGroupRestricted(target))
            {
                error = -4;
                return false;
            }
            if (target.m_GroupOwner != null)
            {
                error = -3;
                return false;
            }
            error = 0;
            return true;
        }

        // 战神 sub_6C3380 (self eligibility).
        //   6C338C xor eax,eax / 6C338E mov [esi],eax            => code := 0
        //   6C3392 call sub_6BBE84 / jne 0x6c33a7                => restricted           -> -6
        //   6C339B mov eax,[edi+0x128] / 6C33A1 cmp byte [eax+0x7C],0 / je 0x6c33b1
        //                                                        => map "no group" flag  -> -6
        //   6C33B1 cmp dword [edi+0xA80],0 / je -> 6C33BA mov [esi],0xFFFFFFFF           -> -1
        private bool CheckNativeGroupSelfEligibility(out int error)
        {
            if (IsNativeGroupRestricted(this) || IsNativeGroupMapDenied(this))
            {
                error = -6;
                return false;
            }
            if (m_GroupOwner != null)
            {
                error = -1;
                return false;
            }
            error = 0;
            return true;
        }

        // 战神 sub_6C3CF0 = ident 1022 CM_DELGROUPMEMBER.
        //   6C3D18 mov byte [ebp-5],0        ; allowed := False
        //   6C3D1C mov esi,0xFFFFFF9D        ; code := -99
        //   6C3D23 call sub_6B7BAC / je 0x6c3d32                  => leader -> allowed := True
        //   6C3D32 lea eax,[ebp-0xC] / 6C3D35 lea edx,[ebx+0x106] / 6C3D3B call 0x405774
        //   6C3D46 call 0x40591c / 6C3D4B jne 0x6c3d53            => own char name == argument
        //                                                           -> allowed := True (self-leave)
        //   6C3D53 or esi,0xFFFFFFFF                              => else code := -1
        //   6C3D61 call sub_6B7B8C / 6C3D68 je 0x6c3d96           => not a group member -> -3
        //   6C3D73 call sub_726E68                                => group.DelMember(arg)
        //   6C3D84 mov dx,0x297 (663) recog=0 msg=arg ; then esi := 0
        //   6C3D9B test esi,esi / jne -> 6C3DA9 mov dx,0x299 (665) recog=esi
        // The previous C# rejected EVERY non-leader with -1, so a member who clicked "leave" in the
        // party panel was told "you are not the leader" and was stuck in the party.
        private void ClientDelGroupMember(string sHumName)
        {
            var allowed = m_GroupOwner == this
                || string.Compare(m_sCharName, sHumName,
                    StringComparison.OrdinalIgnoreCase) == 0;
            if (!allowed)
            {
                SendDefMessage(Grobal2.SM_GROUPDELMEM_FAIL, -1, 0, 0, 0, "");
                return;
            }
            var PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
            // 6C3D61 call sub_6B7B8C is a name/member lookup on the group; a name that resolves to
            // nobody online is likewise "not a member" -> -3. Native has no separate -2 branch here.
            if (PlayObject == null || !IsGroupMember(PlayObject))
            {
                SendDefMessage(Grobal2.SM_GROUPDELMEM_FAIL, -3, 0, 0, 0, "");
                return;
            }
            // Native routes through the GROUP object (sub_726E68), not through the caller, so a
            // non-leader self-removal removes exactly that member.
            (m_GroupOwner as TPlayObject)?.DelMember(PlayObject);
            SendDefMessage(Grobal2.SM_GROUPDELMEM_OK, 0, 0, 0, 0, sHumName);
            if (M2Share.g_FunctionNPC != null)
            {
                M2Share.g_FunctionNPC.GotoLable(this, "@GroupDelMember", false);
            }
        }

        private void ClientDealTry(string sHumName)
        {
            TPlayObject TargetPlayObject;
            var yanshenTradeBanned = new YanshenApi(this, null, M2Share.PluginManager).IsTradeBanned();
            if (M2Share.g_Config.boDisableDeal || yanshenTradeBanned)
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sDisableDealItemsMsg);
                return;
            }
            if (m_boDealing)
            {
                return;
            }
            if ((HUtil32.GetTickCount() - m_DealLastTick) < M2Share.g_Config.dwTryDealTime)
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sPleaseTryDealLaterMsg);
                return;
            }
            if (!m_boCanDeal)
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sCanotTryDealMsg);
                return;
            }
            TargetPlayObject = (TPlayObject)GetPoseCreate();
            if (TargetPlayObject != null && TargetPlayObject != this)
            {
                if (TargetPlayObject.GetPoseCreate() == this && !TargetPlayObject.m_boDealing)
                {
                    if (TargetPlayObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        if (TargetPlayObject.m_boAllowDeal && TargetPlayObject.m_boCanDeal)
                        {
                            TargetPlayObject.SysMsg(m_sCharName + M2Share.g_sOpenedDealMsg, MsgColor.Green, MsgType.Hint);
                            SysMsg(TargetPlayObject.m_sCharName + M2Share.g_sOpenedDealMsg, MsgColor.Green, MsgType.Hint);
                            this.OpenDealDlg(TargetPlayObject);
                            TargetPlayObject.OpenDealDlg(this);
                        }
                        else
                        {
                            SysMsg(M2Share.g_sPoseDisableDealMsg, MsgColor.Red, MsgType.Hint);
                        }
                    }
                }
                else
                {
                    SendDefMessage(Grobal2.SM_DEALTRY_FAIL, 0, 0, 0, 0, "");
                }
            }
            else
            {
                SendDefMessage(Grobal2.SM_DEALTRY_FAIL, 0, 0, 0, 0, "");
            }
        }

        private void ClientAddDealItem(int nItemIdx, string sItemName)
        {
            bool bo11;
            TUserItem UserItem;
            string sUserItemName;
            if (m_DealCreat == null || !m_boDealing)
            {
                return;
            }
            if (sItemName.IndexOf(' ') >= 0)
            {
                
                HUtil32.GetValidStr3(sItemName, ref sItemName, new string[] { " " });
            }
            bo11 = false;
            if (!m_DealCreat.m_boDealOK)
            {
                for (var i = 0; i < m_ItemList.Count; i++)
                {
                    UserItem = m_ItemList[i];
                    if (ClientItemIdMatches(UserItem, nItemIdx))
                    {
                        sUserItemName = ItmUnit.GetItemName(UserItem);// 鍙栬嚜瀹氫箟鐗╁搧鍚嶇О
                        if (string.Compare(sUserItemName, sItemName, StringComparison.OrdinalIgnoreCase) == 0 && m_DealItemList.Count < 12)
                        {
                            // TRADE-11: 战神 sub_78389C mode 2 transfer permission gate at 0x6C4230.
                            // Native: BA 02 00 00 00 (mov edx,2) / 8B C6 (mov eax,esi=UserItem)
                            //         E8 67 F6 0B 00 (call sub_78389C) / 89 45 F0 (mov [ebp-0x10],eax)
                            //         83 7D F0 00 (cmp dword [ebp-0x10],0) / 7F 56 (jg reject)
                            // Returns non-zero when stdItem.Reserved02 & 0x02 (the no-trade flag).
                            var stdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                            if (NativeItemDropDestroy.CheckTransferPermission(UserItem, stdItem,
                                NativeItemDropDestroy.TransferModeTrade) != 0)
                            {
                                // Native rejects by jumping to 0x6C4294, which breaks out of the item
                                // loop and falls through to send SM_DEALADDITEM_FAIL. No other message.
                                break;
                            }

                            m_DealItemList.Add(UserItem);
                            this.SendAddDealItem(UserItem);
                            m_ItemList.RemoveAt(i);
                            bo11 = true;
                            break;
                        }
                    }
                }
            }
            if (!bo11)
            {
                SendDefMessage(Grobal2.SM_DEALADDITEM_FAIL, 0, 0, 0, 0, "");
            }
        }

        private void ClientDelDealItem(int nItemIdx, string sItemName)
        {
            if (m_DealCreat == null || !m_boDealing || m_DealCreat.m_boDealOK)
            {
                return;
            }

            for (var i = 0; i < m_DealItemList.Count; i++)
            {
                if (ClientItemIdMatches(m_DealItemList[i], nItemIdx))
                {
                    DealCancel();
                    return;
                }
            }
        }

        private void ClientCancelDeal()
        {
            DealCancel();
        }

        private void ClientChangeDealGold(int nGold)
        {
            bool bo09;
            if (m_nDealGolds > 0 && M2Share.g_Config.boCanNotGetBackDeal)
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sDealItemsDenyGetBackMsg);
                SendDefMessage(Grobal2.SM_DEALDELITEM_FAIL, 0, 0, 0, 0, "");
                return;
            }
            if (nGold < 0)
            {
                SendDefMessage(Grobal2.SM_DEALCHGGOLD_FAIL, m_nDealGolds, HUtil32.LoWord(m_nGold), HUtil32.HiWord(m_nGold), 0, "");
                return;
            }
            bo09 = false;
            if (m_DealCreat != null && GetPoseCreate() == m_DealCreat)
            {
                if (!m_DealCreat.m_boDealOK)
                {
                    if (m_nGold + m_nDealGolds >= nGold)
                    {
                        m_nGold = m_nGold + m_nDealGolds - nGold;
                        m_nDealGolds = nGold;
                        SendDefMessage(Grobal2.SM_DEALCHGGOLD_OK, m_nDealGolds, HUtil32.LoWord(m_nGold), HUtil32.HiWord(m_nGold), 0, "");
                        (m_DealCreat as TPlayObject).SendDefMessage(Grobal2.SM_DEALREMOTECHGGOLD, m_nDealGolds, 0, 0, 0, "");
                        m_DealCreat.m_DealLastTick = HUtil32.GetTickCount();
                        bo09 = true;
                        m_DealLastTick = HUtil32.GetTickCount();
                    }
                }
            }
            if (!bo09)
            {
                SendDefMessage(Grobal2.SM_DEALCHGGOLD_FAIL, m_nDealGolds, HUtil32.LoWord(m_nGold), HUtil32.HiWord(m_nGold), 0, "");
            }
        }

        private void ClientDealEnd()
        {
            bool bo11;
            TUserItem UserItem;
            GoodItem StdItem;
            TPlayObject PlayObject;
            // 战神 sub_6C4580 @0x6C45A2-0x6C45EF：**6 道前置门全部先跑，然后才置 m_boDealOK**。
            // 逐条（全部 `je/jne 0x6C49EB` = 纯 epilogue，`xor eax,eax` 后 SEH 收尾 → **静默返回，不发任何消息**）：
            //   1) @0x6C45A2 `cmp byte[ebx+0x461],0` / `je`      → 自己 m_boDealing 为假 → 静默返回
            //   2) @0x6C45AF `cmp dword[ebx+0xBAC],0` / `je`     → m_DealCreat 为空 → 静默返回
            //   3) @0x6C45BC `cmp byte[ebx+0x73],0` / `jne`      → 自己 m_boDeath 为真 → 静默返回
            //   4) @0x6C45CC `cmp byte[eax+0x461],0` / `je`      → 对端 m_boDealing 为假 → 静默返回
            //   5) @0x6C45D9 `cmp byte[eax+0x73],0` / `jne`      → 对端 m_boDeath 为真 → 静默返回
            //   6) @0x6C45E3 `cmp ebx,[eax+0xBAC]` / `jne`       → 对端 m_DealCreat 不指向自己 → 静默返回
            //   然后 @0x6C45EF `mov byte ptr [ebx+0x684], 1` = m_boDealOK := true
            // 第 6 条（互指一致性）+ 第 1/4 条（双边 dealing）+ 第 3/5 条（双边存活）共同保证：
            // 押金释放只在「双方都活着、双方都在交易中、两个指针互指」时可达。
            // 旧 C# 一条都没有（只有 m_DealCreat==null），因此一个单边悬挂的 m_DealCreat
            // （DealCancel 遗留，见 TPlayObject.cs DealCancel 注释）足以对已取回押金的一方再成交一次 = 复制。
            if (!m_boDealing)
            {
                return;
            }
            if (m_DealCreat == null)
            {
                return;
            }
            if (m_boDeath)
            {
                return;
            }
            if (!m_DealCreat.m_boDealing)
            {
                return;
            }
            if (m_DealCreat.m_boDeath)
            {
                return;
            }
            if (m_DealCreat.m_DealCreat != this)
            {
                return;
            }
            m_boDealOK = true;
            if (((HUtil32.GetTickCount() - m_DealLastTick) < M2Share.g_Config.dwDealOKTime) || ((HUtil32.GetTickCount() - m_DealCreat.m_DealLastTick) < M2Share.g_Config.dwDealOKTime))
            {
                SysMsg(M2Share.g_sDealOKTooFast, MsgColor.Red, MsgType.Hint);
                DealCancel();
                return;
            }
            // TRADE-22: Authentication verification script (战神 sub_6C4580 Phase C authority gates)
            // Self gate @0x6C4650: call sub_617A38(cfg, self, cl=2) tests bit 2 in obj+0x193C bitset
            // Partner gate @0x6C4693: call sub_617A38(cfg, partner, cl=2) on partner's bitset
            // On failure with escrow: runs @PlayerActiveValidate script (@0x69B254) then DealCancel
            // When boAuthOpen=false (default): CheckNativeAuthentication returns true (bypassed)
            if (M2Share.g_Config.boAuthOpen)
            {
                // Self authority gate @0x6C4650
                bool selfAuthenticated = CheckNativeAuthentication(1, 2) || CheckNativeAuthentication(2, 2);
                if (!selfAuthenticated && (m_DealItemList.Count > 0 || m_nDealGolds > 0))
                {
                    M2Share.g_FunctionNPC?.GotoLable(this, "@PlayerActiveValidate", false);
                    DealCancel();
                    return;
                }
                // Partner authority gate @0x6C4693
                var partner = m_DealCreat as TPlayObject;
                bool partnerAuthenticated = partner != null &&
                    (partner.CheckNativeAuthentication(1, 2) || partner.CheckNativeAuthentication(2, 2));
                if (!partnerAuthenticated && (m_DealCreat.m_DealItemList.Count > 0 || m_DealCreat.m_nDealGolds > 0))
                {
                    M2Share.g_FunctionNPC?.GotoLable(partner, "@PlayerActiveValidate", false);
                    DealCancel();
                    return;
                }
            }
            if (m_DealCreat.m_boDealOK)
            {
                bo11 = true;
                if (Grobal2.MAXBAGITEM - m_ItemList.Count < m_DealCreat.m_DealItemList.Count)
                {
                    bo11 = false;
                    SysMsg(M2Share.g_sYourBagSizeTooSmall, MsgColor.Red, MsgType.Hint);
                }
                if (m_nGoldMax - m_nGold < m_DealCreat.m_nDealGolds)
                {
                    SysMsg(M2Share.g_sYourGoldLargeThenLimit, MsgColor.Red, MsgType.Hint);
                    bo11 = false;
                }
                if (Grobal2.MAXBAGITEM - m_DealCreat.m_ItemList.Count < m_DealItemList.Count)
                {
                    SysMsg(M2Share.g_sDealHumanBagSizeTooSmall, MsgColor.Red, MsgType.Hint);
                    bo11 = false;
                }
                if (m_DealCreat.m_nGoldMax - m_DealCreat.m_nGold < m_nDealGolds)
                {
                    SysMsg(M2Share.g_sDealHumanGoldLargeThenLimit, MsgColor.Red, MsgType.Hint);
                    bo11 = false;
                }
                if (bo11)
                {
                    for (var i = 0; i < m_DealItemList.Count; i++)
                    {
                        UserItem = m_DealItemList[i];
                        // 战神 sub_6C4580 @0x6C47A1: `push 1; mov cl,1; call [vmt+0x248]`
                        // — DEAL completion hands the item over through the OUTER
                        // AddItemToBag with acquisitionReason = 1 (stamper enabled).
                        m_DealCreat.AddItemToBag(UserItem,
                            NativeItemAcquisitionStamp.Reason.Deal, true);
                        (m_DealCreat as TPlayObject)?.ReassignClientItemId(UserItem);
                        (m_DealCreat as TPlayObject).SendAddItem(UserItem);
                        StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                        if (StdItem != null)
                        {
                            if (!M2Share.IsCheapStuff(StdItem.StdMode))
                            {
                                if (StdItem.NeedIdentify == 1)
                                {
                                    M2Share.AddGameDataLog('8' + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + UserItem.MakeIndex + "\t" + '1' + "\t" + m_DealCreat.m_sCharName);
                                }
                            }
                        }
                    }
                    if (m_nDealGolds > 0)
                    {
                        m_DealCreat.m_nGold += m_nDealGolds;
                        m_DealCreat.GoldChanged();
                        if (M2Share.g_boGameLogGold)
                        {
                            M2Share.AddGameDataLog('8' + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + Grobal2.sSTRING_GOLDNAME + "\t" + m_nGold + "\t" + '1' + "\t" + m_DealCreat.m_sCharName);
                        }
                    }
                    for (var i = 0; i < m_DealCreat.m_DealItemList.Count; i++)
                    {
                        UserItem = m_DealCreat.m_DealItemList[i];
                        // 战神 sub_6C4580 @0x6C48C9 — the mirror-direction deal hand-over,
                        // same `push 1; mov cl,1; call [vmt+0x248]` shape.
                        AddItemToBag(UserItem,
                            NativeItemAcquisitionStamp.Reason.Deal, true);
                        ReassignClientItemId(UserItem);
                        this.SendAddItem(UserItem);
                        StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                        if (StdItem != null)
                        {
                            if (!M2Share.IsCheapStuff(StdItem.StdMode))
                            {
                                if (StdItem.NeedIdentify == 1)
                                {
                                    M2Share.AddGameDataLog('8' + "\t" + m_DealCreat.m_sMapName + "\t" + m_DealCreat.m_nCurrX + "\t" + m_DealCreat.m_nCurrY + "\t" + m_DealCreat.m_sCharName + "\t" + StdItem.Name + "\t" + UserItem.MakeIndex + "\t" + '1' + "\t" + m_sCharName);
                                }
                            }
                        }
                    }
                    if (m_DealCreat.m_nDealGolds > 0)
                    {
                        m_nGold += m_DealCreat.m_nDealGolds;
                        GoldChanged();
                        if (M2Share.g_boGameLogGold)
                        {
                            M2Share.AddGameDataLog('8' + "\t" + m_DealCreat.m_sMapName + "\t" + m_DealCreat.m_nCurrX + "\t" + m_DealCreat.m_nCurrY + "\t" + m_DealCreat.m_sCharName + "\t" + Grobal2.sSTRING_GOLDNAME + "\t" + m_DealCreat.m_nGold + "\t" + '1' + "\t" + m_sCharName);
                        }
                    }
                    PlayObject = m_DealCreat as TPlayObject;
                    PlayObject.SendDefMessage(Grobal2.SM_DEALSUCCESS, 0, 0, 0, 0, "");
                    PlayObject.SysMsg(M2Share.g_sDealSuccessMsg, MsgColor.Green, MsgType.Hint);
                    PlayObject.m_DealCreat = null;
                    PlayObject.m_boDealing = false;
                    PlayObject.m_DealItemList.Clear();
                    PlayObject.m_nDealGolds = 0;
                    PlayObject.m_boDealOK = false;
                    SendDefMessage(Grobal2.SM_DEALSUCCESS, 0, 0, 0, 0, "");
                    SysMsg(M2Share.g_sDealSuccessMsg, MsgColor.Green, MsgType.Hint);
                    m_DealCreat = null;
                    m_boDealing = false;
                    m_DealItemList.Clear();
                    m_nDealGolds = 0;
                    m_boDealOK = false;
                }
                else
                {
                    DealCancel();
                }
            }
            else
            {
                SysMsg(M2Share.g_sYouDealOKMsg, MsgColor.Green, MsgType.Hint);
                m_DealCreat.SysMsg(M2Share.g_sPoseDealOKMsg, MsgColor.Green, MsgType.Hint);
            }
        }

        private void ClientGetMinMap()
        {
            var nMinMap = m_PEnvir.nMinMap;
            if (nMinMap > 0)
            {
                SendDefMessage(Grobal2.SM_READMINIMAP_OK, 0, (short)nMinMap, 0, 0, "");
            }
            else
            {
                SendDefMessage(Grobal2.SM_READMINIMAP_FAIL, 0, 0, 0, 0, "");
            }
        }

        private void ClientMakeDrugItem(int NPC, string nItemName)
        {
            var Merchant = (Merchant)M2Share.UserEngine.FindMerchant(NPC);
            if (Merchant == null || !Merchant.m_boMakeDrug)
            {
                return;
            }
            if (Merchant.m_PEnvir == m_PEnvir && Math.Abs(Merchant.m_nCurrX - m_nCurrX) < 15 && Math.Abs(Merchant.m_nCurrY - m_nCurrY) < 15)
            {
                Merchant.ClientMakeDrugItem(this, nItemName);
            }
        }

        private void ClientOpenGuildDlg()
        {
            string sC;
            if (m_MyGuild != null)
            {
                sC = m_MyGuild.sGuildName + '\r' + ' ' + '\r';
                if (m_nGuildRankNo == 1)
                {
                    sC = sC + '1' + '\r';
                }
                else
                {
                    sC = sC + '0' + '\r';
                }
                sC = sC + "<Notice>" + '\r';
                for (var I = 0; I < m_MyGuild.NoticeList.Count; I++)
                {
                    if (sC.Length > 5000)
                    {
                        break;
                    }
                    sC = sC + m_MyGuild.NoticeList[I] + '\r';
                }
                sC = sC + "<KillGuilds>" + '\r';
                for (var I = 0; I < m_MyGuild.GuildWarList.Count; I++)
                {
                    if (sC.Length > 5000)
                    {
                        break;
                    }
                    sC = sC + m_MyGuild.GuildWarList[I] + '\r';
                }
                sC = sC + "<AllyGuilds>" + '\r';
                for (var i = 0; i < m_MyGuild.GuildAllList.Count; i++)
                {
                    if (sC.Length > 5000)
                    {
                        break;
                    }
                    sC = sC + m_MyGuild.GuildAllList[i] + '\r';
                }
                m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_OPENGUILDDLG, 0, 0, 0, 1);
                SendSocket(m_DefMsg, EDcode.EncodeString(sC));
            }
            else
            {
                SendDefMessage(Grobal2.SM_OPENGUILDDLG_FAIL, 0, 0, 0, 0, "");
            }
        }

        private void ClientGuildHome()
        {
            ClientOpenGuildDlg();
        }

        private void ClientGuildMemberList()
        {
            TGuildRank GuildRank;
            var sSendMsg = string.Empty;
            if (m_MyGuild == null)
            {
                return;
            }
            for (var i = 0; i < m_MyGuild.m_RankList.Count; i++)
            {
                GuildRank = m_MyGuild.m_RankList[i];
                sSendMsg = sSendMsg + '#' + GuildRank.nRankNo + "/*" + GuildRank.sRankName + '/';
                for (var j = 0; j < GuildRank.MemberList.Count; j++)
                {
                    if (sSendMsg.Length > 5000)
                    {
                        break;
                    }
                    sSendMsg = sSendMsg + GuildRank.MemberList[j].sMemberName + '/';
                }
            }
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SENDGUILDMEMBERLIST, 0, 0, 0, 1);
            SendSocket(m_DefMsg, EDcode.EncodeString(sSendMsg));
        }

        private void ClientGuildAddMember(string sHumName)
        {
            var nC = 1; 
            if (IsGuildMaster())
            {
                var PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
                if (PlayObject != null)
                {
                    if (PlayObject.GetPoseCreate() == this)
                    {
                        if (PlayObject.m_boAllowGuild)
                        {
                            if (!m_MyGuild.IsMember(sHumName))
                            {
                                if (PlayObject.m_MyGuild == null && m_MyGuild.m_RankList.Count < 400)
                                {
                                    m_MyGuild.AddMember(PlayObject);
                                    M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_207, M2Share.nServerIndex, m_MyGuild.sGuildName);
                                    PlayObject.m_MyGuild = m_MyGuild;
                                    PlayObject.m_sGuildRankName = m_MyGuild.GetRankName(PlayObject, ref PlayObject.m_nGuildRankNo);
                                    PlayObject.RefShowName();
                                    PlayObject.SysMsg("浣犲凡鍔犲叆琛屼細: " + m_MyGuild.sGuildName + " 褰撳墠灏佸彿涓? " + PlayObject.m_sGuildRankName, MsgColor.Green, MsgType.Hint);
                                    nC = 0;
                                }
                                else
                                {
                                    nC = 4; 
                                }
                            }
                            else
                            {
                                nC = 3; 
                            }
                        }
                        else
                        {
                            nC = 5; 
                            PlayObject.SysMsg("浣犳嫆缁濆姞鍏ヨ浼氥€?[鍏佽鍛戒护涓?@" + M2Share.g_GameCommand.LETGUILD.sCmd + ']', MsgColor.Red, MsgType.Hint);
                        }
                    }
                    else
                    {
                        nC = 2; 
                    }
                }
                else
                {
                    nC = 2;
                }
            }
            if (nC == 0)
            {
                SendDefMessage(Grobal2.SM_GUILDADDMEMBER_OK, 0, 0, 0, 0, "");
            }
            else
            {
                SendDefMessage(Grobal2.SM_GUILDADDMEMBER_FAIL, nC, 0, 0, 0, "");
            }
        }

        private void ClientGuildDelMember(string sHumName)
        {
            string s14;
            TPlayObject PlayObject;
            var nC = 1;
            if (IsGuildMaster())
            {
                if (m_MyGuild.IsMember(sHumName))
                {
                    if (m_sCharName != sHumName)
                    {
                        if (m_MyGuild.DelMember(sHumName))
                        {
                            PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
                            if (PlayObject != null)
                            {
                                PlayObject.m_MyGuild = null;
                                PlayObject.RefRankInfo(0, "");
                                PlayObject.RefShowName();
                            }
                            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_207, M2Share.nServerIndex, m_MyGuild.sGuildName);
                            nC = 0;
                        }
                        else
                        {
                            nC = 4;
                        }
                    }
                    else
                    {
                        nC = 3;
                        s14 = m_MyGuild.sGuildName;
                        if (m_MyGuild.CancelGuld(sHumName))
                        {
                            M2Share.GuildManager.DelGuild(s14);
                            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_206, M2Share.nServerIndex, s14);
                            m_MyGuild = null;
                            RefRankInfo(0, "");
                            RefShowName();
                            SysMsg("琛屼細" + s14 + "宸茶鍙栨秷!!!", MsgColor.Red, MsgType.Hint);
                            nC = 0;
                        }
                    }
                }
                else
                {
                    nC = 2;
                }
            }
            if (nC == 0)
            {
                SendDefMessage(Grobal2.SM_GUILDDELMEMBER_OK, 0, 0, 0, 0, "");
            }
            else
            {
                SendDefMessage(Grobal2.SM_GUILDDELMEMBER_FAIL, nC, 0, 0, 0, "");
            }
        }

        private void ClientGuildUpdateNotice(string sNotict)
        {
            var sC = string.Empty;
            if (m_MyGuild == null || m_nGuildRankNo != 1)
            {
                return;
            }
            m_MyGuild.NoticeList.Clear();
            while (!string.IsNullOrEmpty(sNotict))
            {
                sNotict = HUtil32.GetValidStr3(sNotict, ref sC, new string[] { "\r" });
                m_MyGuild.NoticeList.Add(sC);
            }
            m_MyGuild.SaveGuildInfoFile();
            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_207, M2Share.nServerIndex, m_MyGuild.sGuildName);
            ClientOpenGuildDlg();
        }

        private void ClientGuildUpdateRankInfo(string sRankInfo)
        {
            if (m_MyGuild == null || m_nGuildRankNo != 1)
            {
                return;
            }
            var nC = m_MyGuild.UpdateRank(sRankInfo);
            if (nC == 0)
            {
                M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_207, M2Share.nServerIndex, m_MyGuild.sGuildName);
                ClientGuildMemberList();
            }
            else
            {
                if (nC <= -2)
                {
                    SendDefMessage(Grobal2.SM_GUILDRANKUPDATE_FAIL, nC, 0, 0, 0, "");
                }
            }
        }

        private void ClientGuildAlly()
        {
            const string sExceptionMsg = "[Exception] TPlayObject::ClientGuildAlly";
            try
            {
                var n8 = -1;
                TBaseObject BaseObjectC = GetPoseCreate();
                if (BaseObjectC != null && BaseObjectC.m_MyGuild != null && BaseObjectC.m_btRaceServer == Grobal2.RC_PLAYOBJECT && BaseObjectC.GetPoseCreate() == this)
                {
                    if (BaseObjectC.m_MyGuild.m_boEnableAuthAlly)
                    {
                        if (BaseObjectC.IsGuildMaster() && IsGuildMaster())
                        {
                            if (m_MyGuild.IsNotWarGuild(BaseObjectC.m_MyGuild) && BaseObjectC.m_MyGuild.IsNotWarGuild(m_MyGuild))
                            {
                                m_MyGuild.AllyGuild(BaseObjectC.m_MyGuild);
                                BaseObjectC.m_MyGuild.AllyGuild(m_MyGuild);
                                m_MyGuild.SendGuildMsg(BaseObjectC.m_MyGuild.sGuildName + " guild ally success.");
                                BaseObjectC.m_MyGuild.SendGuildMsg(m_MyGuild.sGuildName + " guild ally success.");
                                m_MyGuild.RefMemberName();
                                BaseObjectC.m_MyGuild.RefMemberName();
                                M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_207, M2Share.nServerIndex, m_MyGuild.sGuildName);
                                M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_207, M2Share.nServerIndex, BaseObjectC.m_MyGuild.sGuildName);
                                n8 = 0;
                            }
                            else
                            {
                                n8 = -2;
                            }
                        }
                        else
                        {
                            n8 = -3;
                        }
                    }
                    else
                    {
                        n8 = -4;
                    }
                }
                if (n8 == 0)
                {
                    SendDefMessage(Grobal2.SM_GUILDMAKEALLY_OK, 0, 0, 0, 0, "");
                }
                else
                {
                    SendDefMessage(Grobal2.SM_GUILDMAKEALLY_FAIL, n8, 0, 0, 0, "");
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
        }

        private void ClientGuildBreakAlly(string sGuildName)
        {
            var n10 = -1;
            if (!IsGuildMaster())
            {
                return;
            }
            var guild = M2Share.GuildManager.FindGuild(sGuildName);
            if (guild != null)
            {
                if (m_MyGuild.IsAllyGuild(guild))
                {
                    m_MyGuild.DelAllyGuild(guild);
                    guild.DelAllyGuild(m_MyGuild);
                    m_MyGuild.SendGuildMsg(guild.sGuildName + " 琛屼細涓庢偍鐨勮浼氳В闄よ仈鐩熸垚鍔?!!");
                    guild.SendGuildMsg(m_MyGuild.sGuildName + " 琛屼細瑙ｉ櫎浜嗕笌鎮ㄨ浼氱殑鑱旂洘!!!");
                    m_MyGuild.RefMemberName();
                    guild.RefMemberName();
                    M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_207, M2Share.nServerIndex, m_MyGuild.sGuildName);
                    M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_207, M2Share.nServerIndex, guild.sGuildName);
                    n10 = 0;
                }
                else
                {
                    n10 = -2;
                }
            }
            else
            {
                n10 = -3;
            }
            if (n10 == 0)
            {
                SendDefMessage(Grobal2.SM_GUILDBREAKALLY_OK, 0, 0, 0, 0, "");
            }
            else
            {
                SendDefMessage(Grobal2.SM_GUILDBREAKALLY_FAIL, n10, 0, 0, 0, "");
            }
        }

        private void ClientQueryRepairCost(int nParam1, int nInt, string sMsg)
        {
            TUserItem UserItem;
            TUserItem UserItemA = null;
            string sUserItemName;
            for (var i = 0; i < m_ItemList.Count; i++)
            {
                UserItem = m_ItemList[i];
                if (ClientItemIdMatches(UserItem, nInt))
                {
                    sUserItemName = ItmUnit.GetItemName(UserItem); 
                    if (string.Compare(sUserItemName, sMsg, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        UserItemA = UserItem;
                        break;
                    }
                }
            }
            if (UserItemA == null)
            {
                return;
            }
            var merchant = (Merchant)M2Share.UserEngine.FindMerchant(nParam1);
            if (merchant != null && merchant.m_PEnvir == m_PEnvir && Math.Abs(merchant.m_nCurrX - m_nCurrX) < 15 && Math.Abs(merchant.m_nCurrY - m_nCurrY) < 15)
            {
                merchant.ClientQueryRepairCost(this, UserItemA);
            }
        }

        private void ClientRepairItem(int nParam1, int nInt, string sMsg)
        {
            TUserItem UserItem = null;
            string sUserItemName;
            for (var i = 0; i < m_ItemList.Count; i++)
            {
                var candidate = m_ItemList[i];
                sUserItemName = ItmUnit.GetItemName(candidate);// 鍙栬嚜瀹氫箟鐗╁搧鍚嶇О
                if (ClientItemIdMatches(candidate, nInt) && string.Compare(sUserItemName, sMsg, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    UserItem = candidate;
                    break;
                }
            }
            if (UserItem == null)
            {
                return;
            }
            Merchant merchant = (Merchant)M2Share.UserEngine.FindMerchant(nParam1);
            if (merchant != null && merchant.m_PEnvir == m_PEnvir && Math.Abs(merchant.m_nCurrX - m_nCurrX) < 15 && Math.Abs(merchant.m_nCurrY - m_nCurrY) < 15)
            {
                merchant.ClientRepairItem(this, UserItem);
            }
        }

        internal void ClientStorageItem(int ObjectId, int nItemIdx, string sMsg)
        {
            GoodItem StdItem;
            var bo19 = false;
            TUserItem UserItem = null;
            string sUserItemName;
            if (sMsg.IndexOf(' ') >= 0)
            {
                HUtil32.GetValidStr3(sMsg, ref sMsg, new string[] { " " });
            }
            if (m_nPayMent == 1 && !M2Share.g_Config.boTryModeUseStorage)
            {
                SysMsg(M2Share.g_sTryModeCanotUseStorage, MsgColor.Red, MsgType.Hint);
                return;
            }
            Merchant merchant = (Merchant)M2Share.UserEngine.FindMerchant(ObjectId);
            for (var i = m_ItemList.Count - 1; i >= 0; i--)
            {
                UserItem = m_ItemList[i];
                sUserItemName = ItmUnit.GetItemName(UserItem);
                if (ClientItemIdMatches(UserItem, nItemIdx) && string.Compare(sUserItemName, sMsg, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    
                    if (merchant != null && merchant.m_boStorage && (merchant.m_PEnvir == m_PEnvir && Math.Abs(merchant.m_nCurrX - m_nCurrX) < 15 && Math.Abs(merchant.m_nCurrY - m_nCurrY) < 15 || merchant == M2Share.g_FunctionNPC))
                    {
                        if (m_StorageItemList.Count < Math.Clamp(m_nStorageSpaceCount,
                                MIN_STORAGE_ITEM_COUNT, MAX_STORAGE_ITEM_COUNT))
                        {
                            m_StorageItemList.Add(UserItem);
                            m_ItemList.RemoveAt(i);
                            WeightChanged();
                            SendStorageItemOk(UserItem);
                            M2Share.UserEngine.SaveHumanRcd(this);
                            StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                            if (StdItem.NeedIdentify == 1)
                            {
                                M2Share.AddGameDataLog('1' + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + UserItem.MakeIndex + "\t" + '1' + "\t" + '0');
                            }
                        }
                        else
                        {
                            SendDefMessage(Grobal2.SM_STORAGE_FULL, 0, 0, 0, 0, "");
                        }
                        bo19 = true;
                    }
                    break;
                }
            }
            if (!bo19)
            {
                SendDefMessage(Grobal2.SM_STORAGE_FAIL, 0, 0, 0, 0, "");
            }
        }

        internal void ClientTakeBackStorageItem(int NPC, int nItemIdx, string sMsg)
        {
            GoodItem StdItem;
            string sUserItemName;
            var bo19 = false;
            TUserItem UserItem = null;
            var merchant = (Merchant)M2Share.UserEngine.FindMerchant(NPC);
            if (merchant == null)
            {
                return;
            }
            if (m_nPayMent == 1 && !M2Share.g_Config.boTryModeUseStorage)
            {
                
                SysMsg(M2Share.g_sTryModeCanotUseStorage, MsgColor.Red, MsgType.Hint);
                return;
            }
            if (!m_boCanGetBackItem)
            {
                SendMsg(merchant, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sStorageIsLockedMsg + "\\ \\" + "浠撳簱寮€閿佸懡浠? @" + M2Share.g_GameCommand.UNLOCKSTORAGE.sCmd + '\\' + "浠撳簱鍔犻攣鍛戒护: @" + M2Share.g_GameCommand.__LOCK.sCmd + '\\' + "璁剧疆瀵嗙爜鍛戒护: @" + M2Share.g_GameCommand.SETPASSWORD.sCmd + '\\' + "淇敼瀵嗙爜鍛戒护: @" + M2Share.g_GameCommand.CHGPASSWORD.sCmd);
                return;
            }
            for (var i = 0; i < m_StorageItemList.Count; i++)
            {
                UserItem = m_StorageItemList[i];
                sUserItemName = ItmUnit.GetItemName(UserItem); 
                if (ClientItemIdMatches(UserItem, nItemIdx) && string.Compare(sUserItemName, sMsg, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (IsAddWeightAvailable(M2Share.UserEngine.GetStdItemWeight(UserItem.wIndex)))
                    {
                        
                        if (merchant.m_boGetback && (merchant.m_PEnvir == m_PEnvir && Math.Abs(merchant.m_nCurrX - m_nCurrX) < 15 && Math.Abs(merchant.m_nCurrY - m_nCurrY) < 15 || merchant == M2Share.g_FunctionNPC))
                        {
                            if (AddItemToBag(UserItem))
                            {
                                SendAddItem(UserItem);
                                m_StorageItemList.RemoveAt(i);
                                SendDefMessage(Grobal2.SM_TAKEBACKSTORAGEITEM_OK, nItemIdx, 0, 0, 0, "");
                                SendSaveItemList(NPC);
                                M2Share.UserEngine.SaveHumanRcd(this);
                                StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                                if (StdItem.NeedIdentify == 1)
                                {
                                    M2Share.AddGameDataLog('0' + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + StdItem.Name + "\t" + UserItem.MakeIndex + "\t" + '1' + "\t" + '0');
                                }
                            }
                            else
                            {
                                SendDefMessage(Grobal2.SM_TAKEBACKSTORAGEITEM_FULLBAG, 0, 0, 0, 0, "");
                            }
                            bo19 = true;
                        }
                    }
                    else
                    {
                        
                        SysMsg(M2Share.g_sCanotGetItems, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                }
            }
            if (!bo19)
            {
                SendDefMessage(Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL, 0, 0, 0, 0, "");
            }
        }
    }
}
