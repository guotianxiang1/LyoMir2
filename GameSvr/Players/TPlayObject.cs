using System.Collections;
using System.Text.RegularExpressions;
using SystemModule;
using SystemModule.Common;
using SystemModule.Packages;

namespace GameSvr
{
    public partial class TPlayObject : AnimalObject
    {
        private int _merchantDialogSeq;
        private string _nativeQuestInfoBuffer = string.Empty;
        private string _yanshenTitleInfo = string.Empty;
        private bool _yanshenTitleInfoSet;
        private byte _nativeAuthStatus1;
        private byte _nativeAuthStatus2;
        private byte _nativeAuthStatus3;
        private string _lastMapDescription = string.Empty;
        public int m_nHonorValue;
        public bool m_boHonorValueLoaded;

        internal int MerchantDialogSeq => _merchantDialogSeq;

        /// <summary>The cooldown table lives on THumanKind (created at 0x73BFF2
        /// in ctor sub_73BF00), so TPlayer has it: VMT 0x6AC8C8+0x1F0 holds the
        /// same sub_748130 as THumanKind's own slot, with no override.</summary>
        internal override bool SupportsNativeColdTime => true;

        internal void SetNativeAuthenticationStatus(byte status1, byte status2, byte status3)
        {
            _nativeAuthStatus1 = status1;
            _nativeAuthStatus2 = status2;
            _nativeAuthStatus3 = status3;
        }

        internal void ApplyNativeAuthenticationLimits()
        {
            if (!M2Share.g_Config.boAuthOpen)
            {
                m_nGoldMax = 50_000_000;
                m_nStorageSpaceCount = MAX_STORAGE_ITEM_COUNT;
                return;
            }

            m_nGoldMax = ((_nativeAuthStatus2 | _nativeAuthStatus1) & 0x01) != 0
                ? 50_000_000
                : 2_000_000;
            m_nStorageSpaceCount = ((_nativeAuthStatus2 | _nativeAuthStatus1) & 0x08) != 0
                ? MAX_STORAGE_ITEM_COUNT
                : MIN_STORAGE_ITEM_COUNT;
        }

        /// <summary>
        /// 战神 <c>sub_617A38(cl=4)</c> — the drop DESTROY branch's authentication test.
        /// <c>sub_617A38</c> @0x617A3E first checks <c>cmp byte [mgr+8],0; je -&gt; return
        /// TRUE</c> (the feature switch, == <c>boAuthOpen</c>), then runs a two-round
        /// <c>bt dword [player+esi+0x193C],order</c> over <c>esi = 1</c> then <c>esi = 0</c>
        /// — i.e. it accepts the order bit in EITHER of the two adjacent status dwords,
        /// which is the <c>_nativeAuthStatus1</c>/<c>_nativeAuthStatus2</c> pair this class
        /// already models (cf. <c>ApplyNativeAuthenticationLimits</c>, which likewise ORs
        /// the two).
        /// </summary>
        protected override bool NativeItemDropDestroyAuthenticated()
        {
            // 0x617A3E `cmp byte [eax+8],0; je 0x617A6A` -> mov byte [esp],1 (allow all).
            if (!M2Share.g_Config.boAuthOpen) return true;
            return CheckNativeAuthentication(1, NativeItemDropDestroy.AuthenOrder)
                || CheckNativeAuthentication(2, NativeItemDropDestroy.AuthenOrder);
        }

        // sub_63F200 and its caller sub_6B9220 each query order 4 independently.
        internal bool NativeMerchantSellAuthenticated() => NativeItemDropDestroyAuthenticated();

        /// <summary>
        /// 0x73FCEF 的 `is THumanKind`（类指针 [0x73BBE8]）。原生 THumanKind 只有
        /// TPlayer 与 THeroAct 两支 —— 同一判据也解释了为什么 sub_741368 恰好只有
        /// 0x6C07D8 与 0x687125 两个 E8 调用者。
        /// </summary>
        internal override bool IsNativeHumanKind() => true;

        internal bool CheckNativeAuthentication(int authenLevel, int authenOrder)
        {
            var status = authenLevel switch
            {
                1 => _nativeAuthStatus1,
                2 => _nativeAuthStatus2,
                3 => _nativeAuthStatus3,
                _ => -1
            };
            if (status < 0)
                return false;
            if (authenOrder == 100)
                return (status & 0x1F) == 0x1F;
            if ((uint)authenOrder > 7)
                return false;
            return (status & (1 << authenOrder)) != 0;
        }

        internal ClientPacket BuildNativeAuthenticationStatusMessage()
        {
            return Grobal2.MakeDefaultMsg(
                Grobal2.SM_PLAYER_AUTHEN,
                m_btRaceServer == Grobal2.RC_PLAYOBJECT &&
                CheckNativeAuthentication(1, 100) ? 0 : -1,
                0, 0, 0);
        }

        private void SendNativeAuthenticationStatus()
        {
            var message = BuildNativeAuthenticationStatusMessage();
            SendDefMessage((short)message.Ident, message.Recog,
                message.Param, message.Tag, message.Series, string.Empty);
        }

        internal void ApplyQuestInfo(string message, bool setTitleEnabled)
        {
            message ??= string.Empty;
            if (!setTitleEnabled)
            {
                _nativeQuestInfoBuffer = string.IsNullOrEmpty(_nativeQuestInfoBuffer)
                    ? message
                    : message + "|" + _nativeQuestInfoBuffer;
                return;
            }

            _yanshenTitleInfo = TruncateGbk(message, 80);
            _yanshenTitleInfoSet = true;
            RefShowName();
        }

        private static string TruncateGbk(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value) || maxBytes <= 0)
                return string.Empty;
            if (HUtil32.GbkEncoding.GetByteCount(value) <= maxBytes)
                return value;

            var buffer = new byte[maxBytes];
            HUtil32.GbkEncoding.GetEncoder().Convert(
                value.AsSpan(), buffer.AsSpan(), true,
                out var charsUsed, out _, out _);
            return value[..charsUsed];
        }

        private void RegisterMerchantDialogLabels(string sMsg)
        {
            if (!string.IsNullOrEmpty(sMsg))
                GetScriptLabel(sMsg);
        }

        private static TPlayObject ClientPickUpItem_ResolveOwner(
            TBaseObject owner)
        {
            if (owner is TPlayObject player)
            {
                return player;
            }
            return owner?.GetMaster() as TPlayObject;
        }

        private bool ClientPickUpItem_IsOfGroup(TPlayObject owner)
        {
            var members = m_GroupOwner?.m_GroupMembers;
            if (owner == null || members == null)
            {
                return false;
            }
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i] != null && string.Equals(
                        members[i].m_sCharName, owner.m_sCharName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private bool ClientPickUpItem_IsOwnerAllowed(TBaseObject owner)
        {
            if (owner == null || ReferenceEquals(owner, this))
            {
                return true;
            }

            var responsiblePlayer = ClientPickUpItem_ResolveOwner(owner);
            if (responsiblePlayer == null)
            {
                return false;
            }
            if (ReferenceEquals(responsiblePlayer, this) ||
                ClientPickUpItem_IsOfGroup(responsiblePlayer))
            {
                return true;
            }

            // sub_6DD534: [self+0xB94] married byte and the spouse
            // ShortString at +0xC48 compared with responsiblePlayer+0x106.
            return m_boMarried && string.Equals(m_sDearName,
                responsiblePlayer.m_sCharName, StringComparison.Ordinal);
        }

        private static bool ClientPickUpItem_IsOwnerExpiredAtTick(
            MapItem mapItem, int currentTick)
        {
            return mapItem != null && unchecked((uint)(currentTick -
                mapItem.CanPickUpTick)) > 120000u;
        }

        private bool ClientPickUpItem()
        {
            if (m_boDealing)
            {
                return false;
            }
            var envir = m_PEnvir;
            if (envir == null)
            {
                return false;
            }

            var mapItem = envir.GetItem(m_nCurrX, m_nCurrY);
            if (mapItem == null)
            {
                return false;
            }

            // sub_6B794C clears the owner before calling sub_6B7880. The
            // native compare is unsigned and strictly greater than 120000.
            if (ClientPickUpItem_IsOwnerExpiredAtTick(mapItem,
                    HUtil32.GetTickCount()))
            {
                mapItem.OfBaseObject = null;
            }

            // sub_6B7880: cell order first, then final owner, group and spouse.
            // Every false result shares the caller's 0x6B7A41 rejection.
            if (!envir.NativeIsOldestEligiblePlayerInCell(
                    this, m_nCurrX, m_nCurrY) ||
                !ClientPickUpItem_IsOwnerAllowed(
                    mapItem.OfBaseObject as TBaseObject))
            {
                SysMsg("一定时间范围内，不能拾取。",
                    MsgColor.Red, MsgType.Hint);
                return false;
            }

            return ClientPickUpItem(mapItem, m_nCurrX, m_nCurrY);
        }

        private bool ClientPickUpItem(MapItem mapItem, int pickupX, int pickupY)
        {
            var result = false;
            // sub_6B74D8 opens with this, before it even looks at the item
            // (0x6B7500 GetTickCount / 0x6B7505 `2B 83 E4 03 00 00` /
            // 0x6B750B `3D 58 1B 00 00` + `77 28`, then the two coordinate
            // `jne`s at 0x6B7512 and 0x6B751A). Magic 266 stamps those three
            // fields at 0x773FF0..0x774003, so for 7 s after a blink the
            // caster cannot pick anything up off the cell it landed on.
            if (IsNativeBlinkPickupLocked(pickupX, pickupY,
                    HUtil32.GetTickCount()))
            {
                // 0x6B7522 `66 B9 FF 38` + string 0x6B7800, GBK length
                // prefix 26 = 13 characters.
                SysMsg("一定时间范围内，不能拾取。", MsgColor.Red, MsgType.Hint);
                return false;
            }
            if (mapItem == null)
            {
                return false;
            }
            if (mapItem.Name.Equals(Grobal2.sSTRING_GOLDNAME, StringComparison.OrdinalIgnoreCase))
            {
                if (m_PEnvir.DeleteFromMap(pickupX, pickupY, CellType.OS_ITEMOBJECT, mapItem) == 1)
                {
                    if (IncGold(mapItem.Count))
                    {
                        SendRefMsg(Grobal2.RM_ITEMHIDE, 0, mapItem.Id, pickupX, pickupY, "");
                        if (M2Share.g_boGameLogGold)
                        {
                            M2Share.AddGameDataLog('4' + "\t" + m_sMapName + "\t" + pickupX + "\t" + pickupY + "\t" + m_sCharName + "\t" + Grobal2.sSTRING_GOLDNAME
                                                   + "\t" + mapItem.Count + "\t" + '1' + "\t" + '0');
                        }
                        GoldChanged();
                        Dispose(mapItem);
                    }
                    else
                    {
                        m_PEnvir.AddToMap(pickupX, pickupY, CellType.OS_ITEMOBJECT, mapItem);
                    }
                }
                return result;
            }
            // 0x6B7662 mov dl,1 / 0x6B7668 call [vmt+0x244] (IsEnoughBag)
            // 0x6B7676 cmp dword [item+0x1C],0
            // 0x6B7689 mov dx,word [std+0x1A] / 0x6B768F call 0x73C950
            // All three failures je 0x6B77BA: SysMsg 0x6B7868 (len=20 GBK
            // "无法再拾取更多物品。") via vmt+0xD4, and they run BEFORE
            // DeleteFromMap at 0x6B76C9. The old C# arm DeleteFromMap'd first
            // then Dispose(UserItem) — a swallow native never does.
            var UserItem = mapItem.UserItem;
            var StdItem = UserItem != null
                ? M2Share.UserEngine.GetStdItem(UserItem.wIndex)
                : null;
            if (!IsEnoughBag() || StdItem == null ||
                !IsAddWeightAvailable(
                    M2Share.UserEngine.GetStdItemWeight(UserItem.wIndex)))
            {
                SysMsg("无法再拾取更多物品。", MsgColor.Red, MsgType.Hint);
                return false;
            }
            if (m_PEnvir.DeleteFromMap(pickupX, pickupY, CellType.OS_ITEMOBJECT, mapItem) == 1)
            {
                SendMsg(this, Grobal2.RM_ITEMHIDE, 0, mapItem.Id, pickupX, pickupY, "");
                // 战神 sub_6B74D8 @0x6B7708: `push 4; mov cl,1; call [vmt+0x248]`
                // — the ground-pickup site routes through the OUTER AddItemToBag
                // (sub_6B7378) with acquisitionReason = 4 and the stamper enabled.
                //
                // 眼神「捡物触发」的桩体改写的正是 0x6B770C 的 `8B 55 FC 8B C3`（那条
                // call 的两个实参装载），重放后才 jmp 0x6B7711 —— 所以 @pickpre 发在
                // AddItemToBag 之前、DeleteFromMap 之后。惰性门在 FirePickPre 内。
                GameSvr.Plugins.YanshenTriggerDispatch.FirePickPre(this, StdItem.Name);
                if (!AddItemToBag(UserItem,
                        NativeItemAcquisitionStamp.Reason.PickUp, true))
                {
                    m_PEnvir.AddToMap(pickupX, pickupY,
                        CellType.OS_ITEMOBJECT, mapItem);
                    return false;
                }
                TrackNativeMapDropItem(UserItem);
                if (!M2Share.IsCheapStuff(StdItem.StdMode))
                {
                    if (StdItem.NeedIdentify == 1)
                    {
                        M2Share.AddGameDataLog('4' + "\t" + m_sMapName + "\t" + pickupX + "\t" + pickupY + "\t" + m_sCharName + "\t" + StdItem.Name
                                               + "\t" + UserItem.MakeIndex + "\t" + '1' + "\t" + '0');
                    }
                }
                Dispose(mapItem);
                if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                {
                    this.SendAddItem(UserItem);
                }
                result = true;
            }
            return result;
        }

        // WinExp 现为 sub_6F7A18 的忠实移植，见
        // TPlayObject.NativeWinExp.cs（含被删除的五个非原生缩放的证据）。

        // GetExp 不对应任何战神函数：击杀路的落账在 sub_6C03F8
        // （== GrantNativePlayerExperience）。此处保留仅为两个**非击杀**调用点
        // 服务：经验药水（StdMode 13，本文件）与自动挂机给经验
        // （TPlayObject.Message.cs）。击杀路已不再经过它。
        private void GetExp(int dwExp)
        {
            m_Abil.Exp += dwExp;
            if (dwExp > 0)
                AccumulateNativeSwitchExperience(unchecked((uint)dwExp));
            // 原 AddBodyLuck(dwExp*0.002) 经验缩放幸运增益系伪造——原生 GetExp 无幸运变动，已删除。
            SendMsg(this, Grobal2.RM_WINEXP, 0, dwExp, 0, 0, "");
            if (m_Abil.Exp >= m_Abil.MaxExp)
            {
                m_Abil.Exp -= m_Abil.MaxExp;
                if (m_Abil.Level < M2Share.MAXUPLEVEL)
                {
                    m_Abil.Level++;
                }
                HasLevelUp(m_Abil.Level - 1);
                // 原 AddBodyLuck(100) 升级幸运增益系伪造——原生升级无幸运变动，已删除。
                M2Share.AddGameDataLog("12" + "\t" + m_sMapName + "\t" + m_Abil.Level + "\t" + m_Abil.Exp + "\t" + m_sCharName + "\t" + '0' + "\t" + '0' + "\t" + '1' + "\t" + '0');
                // 0x6C0555 mov ecx,0x4E20 / 0x6C0558 mov edx,0x4E20 = 20000,20000
                // C# 曾写 2000,2000，差 10 倍，已修正。
                IncHealthSpell(20000, 20000);
            }
        }

        public bool IncGold(int tGold)
        {
            // 战神 sub_6D791C @0x6D7920-0x6D7941：`xor ecx,ecx` ; **`test edx,edx` / `jle 0x6D7943`**（<=0 拒绝）;
            //   `ebx=[eax+0x15C]+edx` ; **`cmp ebx,[eax+0x68C]` / `jg 0x6D7943`** ; `add [eax+0x15C],edx` ;
            //   `call 0x6C19B4`(GoldChanged) ; `mov cl,1`。
            // `+0x68C` = RTTI `MaxLimitGold` = **每角色** m_nGoldMax；函数体内**不读任何 g_Config 全局**。
            // ⚠ 勿改：曾有扫描行主张「上限应为 g_Config.nHumanMaxGold」且「tGold<=0 拒绝是非原生」，
            //   两条均被 sub_6D791C 逐字反汇编推翻（见 staging/discovery_economy_20260803.md 行18）。
            //   照那行改会**引入**背离。此处 <=0 门同时覆盖了 DecGold 的 `jl` 负数门。
            if (tGold <= 0) return false;
            var result = false;
            if (m_nGold + tGold <= m_nGoldMax)
            {
                m_nGold += tGold;
                // 0x6D793C `call 0x6C19B4` -- same as DecGold's 0x6C7D7B: the
                // client refresh is emitted INSIDE the credit, success path
                // only.  Was missing on the C# side.
                GoldChanged();
                result = true;
            }
            return result;
        }

        
        
        
        
        public bool IsEnoughBag()
        {
            return m_ItemList.Count < BagCapacity.Of(this);
        }

        
        
        
        
        
        public bool IsAddWeightAvailable(int nWeight)
        {
            // Native sub_73C950 @ 0x73C950:
            //   73C950  8B 90 C4 02 00 00  mov edx,[eax+0x2C4]  ; Weight — overwrites dx
            //   73C956  3B 90 C8 02 00 00  cmp edx,[eax+0x2C8]  ; MaxWeight
            //   73C95C  0F 9C C0           setl al               ; Weight < MaxWeight
            //   73C95F  C3                 ret
            // Callers do pass item weight in dx (pickup 0x6B7689 `66 8B 50 1A`)
            // but the callee's first instruction overwrites it. Adding nWeight
            // rejects items native accepts; on the pickup fail arm that used to
            // Dispose the UserItem, that is a swallow. DROP-39 polarity is
            // still setl (strict <), not setle.
            _ = nWeight;
            return m_WAbil.Weight < m_WAbil.MaxWeight;
        }

        internal int EnsureClientItemId(TUserItem item)
        {
            if (item == null || item.wIndex == 0) return 0;
            if (item.ClientItemID != 0) return item.ClientItemID;

            var clientItemId = _nextClientItemId;
            _nextClientItemId = unchecked(_nextClientItemId + 1);
            if (clientItemId == 0)
            {
                clientItemId = _nextClientItemId;
                _nextClientItemId = unchecked(_nextClientItemId + 1);
            }
            item.ClientItemID = clientItemId;
            return clientItemId;
        }

        internal int ReassignClientItemId(TUserItem item)
        {
            if (item == null) return 0;
            item.ClientItemID = 0;
            return EnsureClientItemId(item);
        }

        internal bool ClientItemIdMatches(TUserItem item, int clientItemId)
        {
            if (item == null || item.wIndex == 0) return false;
            return item.ClientItemID == clientItemId;
        }

        internal TUserItem FindOwnedItemByClientId(int clientItemId,
            bool allowMakeIndexFallback = true)
        {
            var item = FindClientItemIn(m_UseItems, clientItemId, false)
                       ?? FindClientItemIn(m_ItemList, clientItemId, false);
            if (item == null && m_HeroObject != null)
            {
                item = FindClientItemIn(m_HeroObject.m_UseItems, clientItemId, false)
                       ?? FindClientItemIn(m_HeroObject.m_ItemList, clientItemId, false);
            }
            if (item != null || !allowMakeIndexFallback) return item;

            item = FindClientItemIn(m_UseItems, clientItemId, true)
                   ?? FindClientItemIn(m_ItemList, clientItemId, true);
            if (item == null && m_HeroObject != null)
            {
                item = FindClientItemIn(m_HeroObject.m_UseItems, clientItemId, true)
                       ?? FindClientItemIn(m_HeroObject.m_ItemList, clientItemId, true);
            }
            return item;
        }

        internal TUserItem FindClientItemIn(IEnumerable<TUserItem> items, int clientItemId,
            bool makeIndexOnly = false)
        {
            if (items == null) return null;
            foreach (var item in items)
            {
                if (item == null || item.wIndex == 0) continue;
                if (makeIndexOnly)
                {
                    if (item.MakeIndex == clientItemId) return item;
                }
                else if (EnsureClientItemId(item) == clientItemId)
                {
                    return item;
                }
            }
            return null;
        }

        internal byte[] EncodeOwnedClientItemRecord(TUserItem item)
        {
            EnsureClientItemId(item);
            return EncodeClientItemRecord(item);
        }

        public void SendAddItem(TUserItem UserItem)
        {
            if (M2Share.UserEngine.GetStdItem(UserItem.wIndex) == null)
            {
                return;
            }

            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ADDITEM, ObjectId, 0, 0, 1);
            SendSocket(m_DefMsg, EncodeOwnedClientItemRecord(UserItem));
        }

        internal static byte[] EncodeClientItemRecord(TUserItem userItem)
        {
            var fields = BuildClientItemFields(userItem);
            using var memoryStream = new MemoryStream(16 + fields.Count * 4);
            using var writer = new BinaryWriter(memoryStream);
            var clientItemId = userItem.ClientItemID != 0
                ? userItem.ClientItemID
                : userItem.MakeIndex;
            writer.Write((uint)clientItemId);
            writer.Write(userItem.wIndex);
            writer.Write(userItem.Dura);
            writer.Write(userItem.DuraMax);
            writer.Write((short)fields.Count);
            writer.Write(0u);
            foreach (var field in fields)
            {
                writer.Write(field.Type);
                writer.Write(field.Value);
            }
            return memoryStream.ToArray();
        }

        private static List<(short Type, short Value)> BuildClientItemFields(TUserItem userItem)
        {
            var fields = new List<(short Type, short Value)>();

            static short ClientValue(int value) => (short)Math.Clamp(value, 0, short.MaxValue);
            void Add(int type, int value) => fields.Add(((short)type, ClientValue(value)));

            var stdItem = M2Share.UserEngine?.GetStdItem(userItem.wIndex);
            if (stdItem != null)
            {
                TStdItem actual = null;
                stdItem.GetStandardItem(ref actual);
                stdItem.GetItemAddValue(userItem, ref actual);

                var ac = HUtil32.LoWord(actual.AC);
                var maxAc = HUtil32.HiWord(actual.AC);
                var mac = HUtil32.LoWord(actual.MAC);
                var maxMac = HUtil32.HiWord(actual.MAC);
                var dc = HUtil32.LoWord(actual.DC);
                var maxDc = HUtil32.HiWord(actual.DC);
                var mc = HUtil32.LoWord(actual.MC);
                var maxMc = HUtil32.HiWord(actual.MC);
                var sc = HUtil32.LoWord(actual.SC);
                var maxSc = HUtil32.HiWord(actual.SC);

                if (stdItem.StdMode is 5 or 6)
                {
                    if (userItem.jp1 != 0) Add(19, stdItem.Ac2 + userItem.jp1);
                }
                else
                {
                    maxAc += userItem.jp1;
                }
                maxDc += userItem.jp2;
                maxSc += userItem.jp3;
                maxMc += userItem.jp4;
                maxAc += userItem.jp5;
                maxMac += userItem.jp6;

                if (ac != stdItem.Ac) Add(0, ac);
                if (maxAc != stdItem.Ac2) Add(1, maxAc);
                if (mac != stdItem.Mac) Add(2, mac);
                if (maxMac != stdItem.Mac2) Add(3, maxMac);
                if (dc != stdItem.Dc) Add(4, dc);
                if (maxDc != stdItem.Dc2) Add(5, maxDc);
                if (mc != stdItem.Mc) Add(6, mc);
                if (maxMc != stdItem.Mc2) Add(7, maxMc);
                if (sc != stdItem.Sc) Add(8, sc);
                if (maxSc != stdItem.Sc2) Add(9, maxSc);
                if (actual.Need != stdItem.Need) Add(13, actual.Need);
                if (actual.NeedLevel != stdItem.NeedLevel) Add(14, actual.NeedLevel);
            }

            if (userItem.Bind != 0) Add(12, 2);

            if (userItem.ys1 != 0)
            {
                var value = unchecked((uint)userItem.ys1);
                Add(110, (byte)(value >> 24));
                Add(111, (byte)(value >> 16));
                Add(112, (byte)(value >> 8));
                Add(113, (byte)value);
            }

            byte[] elements =
            {
                userItem.ys2, userItem.ys3, userItem.ys4, userItem.ys5,
                userItem.ys6, userItem.ys7, userItem.ys8, userItem.ys9,
                userItem.ys10, userItem.ys11, userItem.ys12, userItem.ys13,
                userItem.ys14, userItem.ys15, userItem.ys16, userItem.ys17
            };
            for (var i = 0; i < elements.Length; i++)
                if (elements[i] != 0) Add(114 + i, elements[i]);

            if (!string.IsNullOrWhiteSpace(userItem.sourceTime)
                && DateTime.TryParse(userItem.sourceTime, out var sourceTime))
            {
                var day = (int)Math.Floor(sourceTime.ToOADate());
                Add(55, day & 0xFF);
                Add(56, (day >> 8) & 0xFF);
                if (sourceTime.Minute != 0) Add(57, sourceTime.Minute);
                if (sourceTime.Hour != 0) Add(58, sourceTime.Hour);

                void AddText(int firstType, int maxBytes, string text)
                {
                    if (string.IsNullOrEmpty(text)) return;
                    var bytes = HUtil32.GbkEncoding.GetBytes(text);
                    for (var i = 0; i < Math.Min(bytes.Length, maxBytes); i++)
                        if (bytes[i] != 0) Add(firstType + i, bytes[i]);
                }

                var systemSource = string.IsNullOrEmpty(userItem.mapName);
                AddText(59, 16, userItem.mapName);
                AddText(76, 15, userItem.killerName);
                if (!systemSource) AddText(92, 15, userItem.pname);
                if (systemSource) Add(107, 1);
                Add(108, 1);
            }

            return fields;
        }

        internal bool IsBlockWhisper(string sName)
        {
            var result = false;
            for (var i = 0; i < this.m_BlockWhisperList.Count; i++)
            {
                if (string.Compare(sName, this.m_BlockWhisperList[i], StringComparison.OrdinalIgnoreCase) == 0)
                {
                    result = true;
                    break;
                }
            }
            return result;
        }

        private void SendSocket(string sMsg)
        {
            if (m_boOffLineFlag) return;
            // 用 m_DefMsg 通过 SendSocket(ClientPacket, string) 统一发, 确保 ClientPacket 写入
            if (!string.IsNullOrEmpty(sMsg))
                SendSocket(m_DefMsg, sMsg);
            else
                SendSocket(m_DefMsg);
        }

        internal virtual void SendSocket(ClientPacket DefMsg, byte[] rawBody)
        {
            // PERF: diagnostic write removed from hot path (per-packet SendSocket)
            if (m_boOffLineFlag) return;
            if (DefMsg != null && DefMsg.Ident == Grobal2.SM_MERCHANTSAY)
            {
                _merchantDialogSeq++;
                if (rawBody.Length > 0)
                    RegisterMerchantDialogLabels(HUtil32.GetString(rawBody, 0, rawBody.Length));
            }
            var messageHead = new PacketHeader
            {
                PacketCode = Grobal2.RUNGATECODE, Socket = m_nSocket, SocketIdx = m_nGSocketIdx, Ident = Grobal2.GM_DATA
            };
            messageHead.PackLength = rawBody.Length + ClientPacket.PackSize;
            var nSendBytes = messageHead.PackLength + PacketHeader.PacketSize;
            using var memoryStream = new MemoryStream();
            using var backingStream = new BinaryWriter(memoryStream);
            backingStream.Write(nSendBytes);
            backingStream.Write(messageHead.GetBuffer());
            backingStream.Write(DefMsg.GetBuffer()); // ClientPacket 不能漏!
            if (rawBody.Length > 0) backingStream.Write(rawBody);
            memoryStream.Seek(0, SeekOrigin.Begin);
            var data = new byte[memoryStream.Length];
            memoryStream.Read(data, 0, data.Length);
            M2Share.GateManager.AddGateBuffer(m_nGateIdx, data);
        }

        private void SendSocket(ClientPacket DefMsg)
        {
            SendSocket(DefMsg, "");
        }

        internal virtual void SendSocket(ClientPacket defMsg, string sMsg)
        {
            if (m_boOffLineFlag && defMsg.Ident != Grobal2.SM_OUTOFCONNECTION)
            {
                return;
            }
            if (defMsg != null && defMsg.Ident == Grobal2.SM_MERCHANTSAY)
            {
                _merchantDialogSeq++;
                RegisterMerchantDialogLabels(sMsg);
            }
            var messageHead = new PacketHeader
            {
                PacketCode = Grobal2.RUNGATECODE,
                Socket = m_nSocket,
                SocketIdx = m_nGSocketIdx,
                Ident = Grobal2.GM_DATA
            };
            var nSendBytes = 0;
            using var memoryStream = new MemoryStream();
            using var backingStream = new BinaryWriter(memoryStream);
            byte[] bMsg = null;
            if (defMsg != null)
            {
                bMsg = HUtil32.GetBytes(sMsg);
                if (!string.IsNullOrEmpty(sMsg))
                {
                    messageHead.PackLength = bMsg.Length + 12;
                }
                else
                {
                    messageHead.PackLength = 12;
                }
                nSendBytes = messageHead.PackLength + PacketHeader.PacketSize;
                backingStream.Write(nSendBytes);
                backingStream.Write(messageHead.GetBuffer());
                backingStream.Write(defMsg.GetBuffer());
            }
            else if (!string.IsNullOrEmpty(sMsg))
            {
                bMsg = HUtil32.GetBytes(sMsg);
                messageHead.PackLength = -(bMsg.Length);
                nSendBytes = Math.Abs(messageHead.PackLength) + PacketHeader.PacketSize;
                backingStream.Write(nSendBytes);
                backingStream.Write(messageHead.GetBuffer());
            }
            if (bMsg != null && bMsg.Length > 0)
            {
                backingStream.Write(bMsg);
            }
            memoryStream.Seek(0, SeekOrigin.Begin);
            var data = new byte[memoryStream.Length];
            memoryStream.Read(data, 0, data.Length);
            var queued = M2Share.GateManager.AddGateBuffer(m_nGateIdx, data);
            // 协议日志严重影响性能，已禁用（仅保留特殊协议）
            // if (defMsg != null && (defMsg.Ident == Grobal2.SM_SENDNOTICE || defMsg.Ident == Grobal2.SM_LOGON))
            // {
            //     M2Share.MainOutMessage($"[SendPacket] ident={defMsg.Ident} chr={m_sCharName} gate={m_nGateIdx} socket={m_nSocket} socketIdx={m_nGSocketIdx} queued={queued} bodyLen={(bMsg == null ? 0 : bMsg.Length)}");
            // }
        }

        public void SendDefMessage(short wIdent, int nRecog, int nParam, int nTag, int nSeries, string sMsg)
        {
            m_DefMsg = Grobal2.MakeDefaultMsg(wIdent, nRecog, nParam, nTag, nSeries);
            if (!string.IsNullOrEmpty(sMsg))
            {
                SendSocket(m_DefMsg, sMsg);
            }
            else
            {
                SendSocket(m_DefMsg);
            }
        }

        private byte DayBright()
        {
            byte result;
            if (m_PEnvir.Flag.boDarkness)
            {
                result = 1;
            }
            else if (m_btBright == 1)
            {
                result = 0;
            }
            else if (m_btBright == 3)
            {
                result = 1;
            }
            else
            {
                result = 2;
            }
            if (m_PEnvir.Flag.boDayLight)
            {
                result = 0;
            }
            return result;
        }

        private void RefUserState()
        {
            var n8 = 0;
            if (m_PEnvir.Flag.boFightZone || m_PEnvir.Flag.boFREEPK)
            {
                n8 = n8 | 1;
            }
            if (m_PEnvir.Flag.boSAFE || InSafeZone())
            {
                n8 = n8 | 2;
            }
            if (m_boInFreePKArea)
            {
                n8 = n8 | 4;
            }
            if (m_PEnvir.Flag.boFight3Zone)
            {
                n8 = n8 | 8;
            }
            SendDefMessage(Grobal2.SM_MYSTATUS, n8, 0, 0, 0, "");
        }

        private void SendSafeZoneInfo()
        {
            var records = new List<(string MapName, short X, short Y, short Range)>();
            if (M2Share.StartPointList != null)
            {
                for (var i = 0; i < M2Share.StartPointList.Count; i++)
                {
                    var point = M2Share.StartPointList[i];
                    if (point == null || string.IsNullOrEmpty(point.m_sMapName))
                    {
                        continue;
                    }

                    var range = point.m_nRange > 0 ? point.m_nRange : M2Share.g_Config.nSafeZoneSize;
                    records.Add((point.m_sMapName, point.m_nCurrX, point.m_nCurrY, ClampToShort(range)));
                }
            }

            if (M2Share.SafeZoneList != null)
            {
                for (var i = 0; i < M2Share.SafeZoneList.Count; i++)
                {
                    var area = M2Share.SafeZoneList[i];
                    if (area == null || string.IsNullOrEmpty(area.MapName) || area.Points.Count < 3)
                    {
                        continue;
                    }

                    var minX = area.Points[0].X;
                    var maxX = area.Points[0].X;
                    var minY = area.Points[0].Y;
                    var maxY = area.Points[0].Y;
                    for (var pointIndex = 1; pointIndex < area.Points.Count; pointIndex++)
                    {
                        minX = Math.Min(minX, area.Points[pointIndex].X);
                        maxX = Math.Max(maxX, area.Points[pointIndex].X);
                        minY = Math.Min(minY, area.Points[pointIndex].Y);
                        maxY = Math.Max(maxY, area.Points[pointIndex].Y);
                    }

                    var centerX = (minX + maxX) / 2;
                    var centerY = (minY + maxY) / 2;
                    var range = Math.Max(maxX - minX, maxY - minY) / 2;
                    records.Add((area.MapName, ClampToShort(centerX), ClampToShort(centerY), ClampToShort(range)));
                }
            }

            // 战神 sub_6F05D8 (@0x6F05E0 mov ebx,eax => ebx IS Self) always answers, and
            // always with nRecog = Self:
            //   empty  0x6F0697 cmp [ebp-4],0 / je 0x6F06D8 and 0x6F06A5 test eax,eax /
            //          0x6F06A7 jle 0x6F06D8 -> 0x6F06D8 push 0 x5 / 8B CB mov ecx,ebx /
            //          66 BA 86 10 mov dx,0x1086 / FF 93 54 02 00 00 call [ebx+0x254]
            //   filled 0x6F06B1 push eax(count) / push 0 / push 0 / push list /
            //          0x6F06C2 6B C0 16 imul eax,eax,0x16 push (count*22) /
            //          0x6F06C6 8B CB mov ecx,ebx / mov dx,0x1086 / call [ebx+0x254]
            // Both were wrong here: nRecog was 0, and an empty list returned without
            // sending, so a client that waits for 4230 during the login burst never got it.
            if (records.Count == 0)
            {
                m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SAFE_ZONE_INFO, ObjectId, 0, 0, 0);
                SendSocket(m_DefMsg);
                return;
            }

            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);
            for (var i = 0; i < records.Count; i++)
            {
                WriteClientFixedGbkString(writer, records[i].MapName, 14);
                AlignWriter(writer, 2);
                writer.Write(records[i].X);
                writer.Write(records[i].Y);
                writer.Write(records[i].Range);
            }

            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SAFE_ZONE_INFO, ObjectId, records.Count, 0, 0);
            SendSocket(m_DefMsg, memoryStream.ToArray());
        }

        private static short ClampToShort(int value)
        {
            if (value < short.MinValue)
            {
                return short.MinValue;
            }
            if (value > short.MaxValue)
            {
                return short.MaxValue;
            }
            return (short)value;
        }

        private static void WriteClientFixedGbkString(BinaryWriter writer, string value, int declaredLength)
        {
            var buffer = new byte[declaredLength + 1];
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            var writeCount = 0;
            for (var i = 0; i < bytes.Length && writeCount < declaredLength;)
            {
                var charBytes = IsGbkLeadByte(bytes[i]) && i + 1 < bytes.Length ? 2 : 1;
                if (writeCount + charBytes > declaredLength)
                {
                    break;
                }

                Buffer.BlockCopy(bytes, i, buffer, writeCount + 1, charBytes);
                writeCount += charBytes;
                i += charBytes;
            }
            buffer[0] = (byte)writeCount;
            writer.Write(buffer);
        }

        private static bool IsGbkLeadByte(byte value)
        {
            return value >= 0x81 && value <= 0xFE;
        }

        private static void AlignWriter(BinaryWriter writer, int alignment)
        {
            var padding = (int)(writer.BaseStream.Position % alignment);
            if (padding == 0)
            {
                return;
            }

            writer.Write(new byte[alignment - padding]);
        }

        public void RefMyStatus()
        {
            RecalcAbilitys();
            SendMsg(this, Grobal2.RM_MYSTATUS, 0, 0, 0, 0, "");
        }

        
        
        
        private void ProcessSpiritSuite()
        {
            GoodItem StdItem;
            TUserItem UseItem;
            if (!M2Share.g_Config.boSpiritMutiny || !m_bopirit)
            {
                return;
            }
            m_bopirit = false;
            for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
            {
                UseItem = m_UseItems[i];
                if (UseItem == null)
                {
                    continue;
                }
                if (UseItem.wIndex <= 0)
                {
                    continue;
                }
                StdItem = M2Share.UserEngine.GetStdItem(UseItem.wIndex);
                if (StdItem != null)
                {
                    if (StdItem.Shape == 126 || StdItem.Shape == 127 || StdItem.Shape == 128 || StdItem.Shape == 129)
                    {
                        SendDelItems(UseItem);
                        UseItem.wIndex = 0;
                    }
                }
            }
            RecalcAbilitys();
            M2Share.g_dwSpiritMutinyTick = HUtil32.GetTickCount() + M2Share.g_Config.dwSpiritMutinyTime;
            M2Share.UserEngine.SendBroadCastMsg("神之祈祷，天地震怒，尸横遍野...", MsgType.System);
            SysMsg("祈祷发出强烈的宇宙效应", MsgColor.Green, MsgType.Hint);
        }

        private void LogonTimcCost()
        {
            int n08;
            string sC;
            if (m_nPayMent == 2 || M2Share.g_Config.boTestServer)
            {
                n08 = (HUtil32.GetTickCount() - m_dwLogonTick) / 1000;
            }
            else
            {
                n08 = 0;
            }
            sC = m_sIPaddr + "\t" + m_sUserID + "\t" + m_sCharName + "\t" + n08 + "\t" + m_dLogonTime.ToString("yyyy-mm-dd hh:mm:ss") + "\t" + DateTime.Now.ToString("yyyy-mm-dd hh:mm:ss") + "\t" + m_nPayMode;
            M2Share.AddLogonCostLog(sC);
            if (m_nPayMode == 2)
            {
                IdSrvClient.Instance.SendLogonCostMsg(m_sUserID, n08 / 60);
            }
        }

        // TPlayer 的 mover 是 0x741224（人形），不是父类 AnimalObject 对应的 0x71F0F4（怪物）——
        // MOVE-40 的 VMT 普查里 TPlayer 和 THumanKind / THeroAct 同槽。C# 的继承链把玩家放在
        // AnimalObject 之下，若不 override 就会继承怪物的松边界，玩家便能站上第 0 行/列。
        // 故这里把人形边界（严格 > 0 且 < Width / < Height，0x741276 jle、0x741284 jge）取回。
        protected override bool WalkToInBounds(short nNX, short nNY)
        {
            return nNX > 0 && nNX < m_PEnvir.wWidth
                && nNY > 0 && nNY < m_PEnvir.wHeight;
        }

        private bool CommitRunMove(int nX, int nY)
        {
            // 原生 run mover（sub_76756C 2 格 / sub_767694 3 格）把 Obj+0x3FE（穿透缓存）
            // 作为移动原语 sub_7797CC(MoveToMovingObjectForRun) 的 boIgnoreOccupancy——
            // 移动读点 0x767601(run2) / 0x76772B(run3) 均 `mov al,[ebx+0x3fe]; push eax`。
            // 调用方 RunTo/HorseRunTo/NativeRun3To 已在入口刷新该字段（tick 写等价），此处只读。
            // 改前误用 boDiableHumanRun||(perm>9&&boGMRunAll)（stock-Mir2 污染，原生 mover 无此参）。
            var ignoreObjects = m_boThroughOccupancyCache;
            return m_PEnvir.MoveToMovingObjectForRun(m_nCurrX, m_nCurrY, this, nX, nY, ignoreObjects) > 0;
        }

        private bool RunTo(byte btDir, bool boFlag, int nDestX, int nDestY)
        {
            const string sExceptionMsg = "[Exception] TBaseObject::RunTo";
            var result = false;
            if (HasTimedAbility(13))
            {
                return result;
            }
            try
            {
                int nOldX = m_nCurrX;
                int nOldY = m_nCurrY;
                m_btDirection = btDir;
                // run mover sub_76756C 在 0x7675BA(探测 sub_777EF8)与 0x767601(移动 sub_7797CC)
                // 两处都 `mov al,[ebx+0x3fe]` 读 Obj+0x3FE(穿透缓存) 作 boIgnoreOccupancy——
                // 与 walk(0x6BBD0C) 同一缓存判定。MOVE-73：该字段的唯一写点是玩家 tick
                // sub_6B2D38 的 0x6B30A3，mover 一律**只读**，本端不再在入口重算。
                switch (btDir)
                {
                    case Grobal2.DR_UP:
                        if (m_nCurrY > 1 && m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY - 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY - 2, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX, m_nCurrY - 2))
                        {
                            m_nCurrY -= 2;
                        }
                        break;
                    case Grobal2.DR_UPRIGHT:
                        if (m_nCurrX < m_PEnvir.wWidth - 2 && m_nCurrY > 1 && m_PEnvir.CanWalkEx(m_nCurrX + 1, m_nCurrY - 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX + 2, m_nCurrY - 2, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX + 2, m_nCurrY - 2))
                        {
                            m_nCurrX += 2;
                            m_nCurrY -= 2;
                        }
                        break;
                    case Grobal2.DR_RIGHT:
                        if (m_nCurrX < m_PEnvir.wWidth - 2 && m_PEnvir.CanWalkEx(m_nCurrX + 1, m_nCurrY, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX + 2, m_nCurrY, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX + 2, m_nCurrY))
                        {
                            m_nCurrX += 2;
                        }
                        break;
                    case Grobal2.DR_DOWNRIGHT:
                        if (m_nCurrX < m_PEnvir.wWidth - 2 && m_nCurrY < m_PEnvir.wHeight - 2 && m_PEnvir.CanWalkEx(m_nCurrX + 1, m_nCurrY + 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX + 2, m_nCurrY + 2, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX + 2, m_nCurrY + 2))
                        {
                            m_nCurrX += 2;
                            m_nCurrY += 2;
                        }
                        break;
                    case Grobal2.DR_DOWN:
                        if (m_nCurrY < m_PEnvir.wHeight - 2 && m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY + 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY + 2, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX, m_nCurrY + 2))
                        {
                            m_nCurrY += 2;
                        }
                        break;
                    case Grobal2.DR_DOWNLEFT:
                        if (m_nCurrX > 1 && m_nCurrY < m_PEnvir.wHeight - 2 && m_PEnvir.CanWalkEx(m_nCurrX - 1, m_nCurrY + 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX - 2, m_nCurrY + 2, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX - 2, m_nCurrY + 2))
                        {
                            m_nCurrX -= 2;
                            m_nCurrY += 2;
                        }
                        break;
                    case Grobal2.DR_LEFT:
                        if (m_nCurrX > 1 && m_PEnvir.CanWalkEx(m_nCurrX - 1, m_nCurrY, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX - 2, m_nCurrY, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX - 2, m_nCurrY))
                        {
                            m_nCurrX -= 2;
                        }
                        break;
                    case Grobal2.DR_UPLEFT:
                        if (m_nCurrX > 1 && m_nCurrY > 1 && m_PEnvir.CanWalkEx(m_nCurrX - 1, m_nCurrY - 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX - 2, m_nCurrY - 2, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX - 2, m_nCurrY - 2))
                        {
                            m_nCurrX -= 2;
                            m_nCurrY -= 2;
                        }
                        break;
                }
                if (m_nCurrX != nOldX || m_nCurrY != nOldY)
                {
                    // MOVE-39 —— 2 格 run mover sub_76756C 同样在提交 X/Y(0x76762E)
                    // 之后、广播(0x767645)之前清定时状态 0x17：
                    //   0x767634  B2 17           mov  dl,0x17
                    //   0x767638  E8 93 3E 00 00  call 0x76B4D0
                    RemoveNativeMovementTimedState(23);
                    // MOVE-39 — sub_76756C 0x767645→0x767656; native ignores 778EC0 return.
                    Walk(Grobal2.RM_RUN);
                    m_dwSearchTick = 0;
                    // MOVE-41 — sub_76756C 尾 0x76765B..0x767683：广播与 sub_778EC0 之后。
                    SyncNativeHorsePartnerAfterRun3();
                    result = true;
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
            return result;
        }

        private bool HorseRunTo(byte btDir, bool boFlag)
        {
            int n10;
            int n14;
            const string sExceptionMsg = "[Exception] TPlayObject::HorseRunTo";
            var result = false;
            if (HasTimedAbility(13))
            {
                return result;
            }
            try
            {
                n10 = m_nCurrX;
                n14 = m_nCurrY;
                m_btDirection = btDir;
                // 3 格马跑 mover sub_767694 同样在 0x7676E2(探测)/0x76772B(移动)读 Obj+0x3FE
                // 作 boIgnoreOccupancy，同样只读（MOVE-73，写点唯一在 0x6B30A3）。
                switch (btDir)
                {
                    case Grobal2.DR_UP:
                        if (m_nCurrY > 2 && m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY - 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY - 2, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY - 3, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX, m_nCurrY - 3))
                        {
                            m_nCurrY -= 3;
                        }
                        break;
                    case Grobal2.DR_UPRIGHT:
                        if (m_nCurrX < m_PEnvir.wWidth - 3 && m_nCurrY > 2 && m_PEnvir.CanWalkEx(m_nCurrX + 1, m_nCurrY - 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX + 2, m_nCurrY - 2, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX + 3, m_nCurrY - 3, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX + 3, m_nCurrY - 3))
                        {
                            m_nCurrX += 3;
                            m_nCurrY -= 3;
                        }
                        break;
                    case Grobal2.DR_RIGHT:
                        if (m_nCurrX < m_PEnvir.wWidth - 3 && m_PEnvir.CanWalkEx(m_nCurrX + 1, m_nCurrY, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX + 2, m_nCurrY, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX + 3, m_nCurrY, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX + 3, m_nCurrY))
                        {
                            m_nCurrX += 3;
                        }
                        break;
                    case Grobal2.DR_DOWNRIGHT:
                        if (m_nCurrX < m_PEnvir.wWidth - 3 && m_nCurrY < m_PEnvir.wHeight - 3 && m_PEnvir.CanWalkEx(m_nCurrX + 1, m_nCurrY + 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX + 2, m_nCurrY + 2, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX + 3, m_nCurrY + 3, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX + 3, m_nCurrY + 3))
                        {
                            m_nCurrX += 3;
                            m_nCurrY += 3;
                        }
                        break;
                    case Grobal2.DR_DOWN:
                        if (m_nCurrY < m_PEnvir.wHeight - 3 && m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY + 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY + 2, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX, m_nCurrY + 3, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX, m_nCurrY + 3))
                        {
                            m_nCurrY += 3;
                        }
                        break;
                    case Grobal2.DR_DOWNLEFT:
                        if (m_nCurrX > 2 && m_nCurrY < m_PEnvir.wHeight - 3 && m_PEnvir.CanWalkEx(m_nCurrX - 1, m_nCurrY + 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX - 2, m_nCurrY + 2, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX - 3, m_nCurrY + 3, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX - 3, m_nCurrY + 3))
                        {
                            m_nCurrX -= 3;
                            m_nCurrY += 3;
                        }
                        break;
                    case Grobal2.DR_LEFT:
                        if (m_nCurrX > 2 && m_PEnvir.CanWalkEx(m_nCurrX - 1, m_nCurrY, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX - 2, m_nCurrY, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX - 3, m_nCurrY, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX - 3, m_nCurrY))
                        {
                            m_nCurrX -= 3;
                        }
                        break;
                    case Grobal2.DR_UPLEFT:
                        if (m_nCurrX > 2 && m_nCurrY > 2 && m_PEnvir.CanWalkEx(m_nCurrX - 1, m_nCurrY - 1, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX - 2, m_nCurrY - 2, m_boThroughOccupancyCache) && m_PEnvir.CanWalkEx(m_nCurrX - 3, m_nCurrY - 3, m_boThroughOccupancyCache) && CommitRunMove(m_nCurrX - 3, m_nCurrY - 3))
                        {
                            m_nCurrX -= 3;
                            m_nCurrY -= 3;
                        }
                        break;
                }
                if (m_nCurrX != n10 || m_nCurrY != n14)
                {
                    // MOVE-39 —— 3 格 run mover sub_767694 同构：提交 X/Y(0x767758)
                    // 之后、广播(0x76776F)之前清定时状态 0x17：
                    //   0x76775E  B2 17           mov  dl,0x17
                    //   0x767762  E8 69 3D 00 00  call 0x76B4D0
                    RemoveNativeMovementTimedState(23);
                    // MOVE-39 — sub_767694 0x76776F→0x767780; native ignores 778EC0 return.
                    Walk(Grobal2.RM_RUN);
                    m_dwSearchTick = 0;
                    result = true;
                }
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage(sExceptionMsg + " " + ex.Message);
            }
            return result;
        }

        protected void ThrustingOnOff(bool boSwitch)
        {
            m_boUseThrusting = boSwitch;
            if (m_boUseThrusting)
            {
                SysMsg(M2Share.sThrustingOn, MsgColor.Green, MsgType.Hint);
            }
            else
            {
                SysMsg(M2Share.sThrustingOff, MsgColor.Green, MsgType.Hint);
            }
        }

        protected void HalfMoonOnOff(bool boSwitch)
        {
            m_boUseHalfMoon = boSwitch;
            if (m_boUseHalfMoon)
            {
                SysMsg(M2Share.sHalfMoonOn, MsgColor.Green, MsgType.Hint);
            }
            else
            {
                SysMsg(M2Share.sHalfMoonOff, MsgColor.Green, MsgType.Hint);
            }
        }

        protected void SkillTwinOnOff(bool boSwitch)
        {
            m_boTwinHitSkill = boSwitch;
            if (m_boTwinHitSkill)
            {
                SysMsg(M2Share.sTwinHitOn, MsgColor.Green, MsgType.Hint);
            }
            else
            {
                SysMsg(M2Share.sTwinHitOff, MsgColor.Green, MsgType.Hint);
            }
        }

        private bool AllowFireHitSkill()
        {
            return AllowFireHitSkill(HUtil32.GetTickCount());
        }

        internal bool AllowFireHitSkill(int currentTick)
        {
            // sub_6BE068 uses one GetTickCount value and an unsigned subtraction.
            if (unchecked((uint)(currentTick - m_dwLatestFireHitTick)) > 10000u)
            {
                m_dwLatestFireHitTick = currentTick;
                m_boFireHitSkill = true;
                return true;
            }
            SysMsg("凝聚内力失败", MsgColor.Red, MsgType.Hint);
            return false;
        }

        /// <summary>
        /// 战神 hands sub_6B8B28 three arguments, not two:
        ///   0x6D8EFE  0F B7 48 08  movzx ecx, word [msg+8]   ; ECX = Tag
        ///   0x6D8F05  8B 10        mov   edx, [msg]          ; EDX = Recog
        ///   0x6D8F07  8B 45 FC     mov   eax, [ebp-4]        ; EAX = Self
        /// and the Tag is a two-way selector, not decoration:
        ///   0x6B8B5A  83 7D FC 01           cmp dword [ebp-4], 1
        ///   0x6B8B5E  0F 85 8B 00 00 00     jne 0x6B8BEF
        /// The two arms search two different registries and run two different script hosts:
        ///   Tag == 1  0x6B8B64 `A1 9C 5D 7D 00` [[0x7D5D9C]] -> 0x67DBE8 (TList at mgr+0xD4,
        ///             matched by pointer identity) -> click via 0x720444, whose script object
        ///             is monster+0x4D0 and whose unit owns "monScript\" and "TAnimal.LoadScript"
        ///   Tag != 1  0x6B8BEF `A1 84 67 7D 00` [[0x7D6784]] -> 0x649A58, then 0x64A844 on a
        ///             miss -> click via 0x63DC74, script object npc+0x570, unit owns
        ///             "点击NPC成功" and "TPsNpc.Run"
        /// Both label literals are "@main" (0x720460 and 0x63DC90, declen 5 each).
        ///
        /// C# used to take only the id and always ran the NPC arm, so clicking a scripted
        /// monster did nothing at all. On this deployment CM_CLICKNPC is 363,328 packets.
        /// </summary>
        private void ClientClickNPC(int npcId, int tag)
        {
            // PERF: diagnostic write removed from hot path (per-click)
            // TRADE-62: 战神 sub_6B8B28 的第一条可执行语句（SEH 序言之后）就是
            //   0x6B8B4D  80 BB 61 04 00 00 00  cmp byte [ebx+0x461], 0   ; m_boDealing
            //   0x6B8B54  0F 85 2A 01 00 00     jne 0x6B8C84
            // 0x6B8C84 是 `33 C0 xor eax,eax` + SEH 拆除 + `C3 ret`，即**静默返回**，
            // 不发任何消息。交易进行中点 NPC 会被整体忽略。
            // 注意：紧随其后的 `!m_boCanDeal` 占的正是这道门的位置，但它不是原生门
            // （见下），原生在此只测 +0x461。
            if (m_boDealing)
            {
                return;
            }
            if (!m_boCanDeal)
            {
                SendMsg(M2Share.g_ManageNPC, Grobal2.RM_MENU_OK, 0, ObjectId, 0, 0, M2Share.g_sCanotTryDealMsg);
                return;
            }
            if (m_boDeath || m_boGhost)
            {
                return;
            }
            if ((HUtil32.GetTickCount() - m_dwClickNpcTime) > M2Share.g_Config.dwClickNpcTime)
            {
                m_dwClickNpcTime = HUtil32.GetTickCount();
                if (tag == 1)
                {
                    ClickScriptedMonster(npcId);
                    return;
                }
                var normNpc = (NormNpc)M2Share.UserEngine.FindMerchant(npcId) ?? (NormNpc)M2Share.UserEngine.FindNPC(npcId);
                if (normNpc != null && normNpc.m_PEnvir == m_PEnvir && Math.Abs(normNpc.m_nCurrX - m_nCurrX) <= 15 && Math.Abs(normNpc.m_nCurrY - m_nCurrY) <= 15)
                {
                    normNpc.Click(this);
                    // Native sub_6B8B28 stores the NPC into player+0xCD8 AFTER the
                    // talk vcall returns, with no test of the script result:
                    //   0x6B8BA2 call 0x720444 / 0x6B8BA7 mov [ebx+0xCD8],esi
                    //   0x6B8C43 call 0x63DC74 / 0x6B8C48 mov [ebx+0xCD8],esi
                    // Exhaustive disp 0xCD8 scan: those two plus setter 0x63DFAF.
                    // There is no write of 0 anywhere. Bind here so @main itself
                    // still sees the previous/nil field (Give audit 0x6DF341).
                    m_NPC = normNpc;
                }
            }
        }

        /// <summary>
        /// The Tag == 1 arm of CM_CLICKNPC, 0x6B8B64..0x6B8BAD:
        ///   0x6B8B6D  E8 76 50 FC FF        call 0x67DBE8   ; find in [[0x7D5D9C]] by identity
        ///   0x6B8B74  85 F6 / 74 3A         test esi,esi / je 0x6B8BB2
        ///   0x6B8B78  8B 86 28 01 00 00     mov eax,[target+0x128]
        ///   0x6B8B7E  3B 83 28 01 00 00     cmp eax,[self+0x128] / 75 2C jne 0x6B8BB2
        ///   0x6B8B86  8B 8E 30 01 00 00     mov ecx,[target+0x130]     ; CurrY
        ///   0x6B8B8C  8B 96 2C 01 00 00     mov edx,[target+0x12C]     ; CurrX
        ///   0x6B8B94  E8 0B 29 0B 00        call 0x76B4A4
        ///   0x6B8B99  83 F8 0F / 77 14      cmp eax,0x0F / ja 0x6B8BB2
        ///   0x6B8BA2  E8 9D 78 06 00        call 0x720444              ; monster @main
        ///   0x6B8BA7  89 B3 D8 0C 00 00     mov [self+0xCD8],esi
        /// 0x76B4A4 is Chebyshev distance — `sub / cdq / xor / sub` twice then
        /// 0x76B4C6 `3B C6 cmp eax,esi / 7D 02 jge` keeps the larger — so `cmp 0x0F / ja` is
        /// exactly the `Abs(dx) &lt;= 15 &amp;&amp; Abs(dy) &lt;= 15` the NPC arm spells out longhand.
        ///
        /// Two differences from the NPC arm that are deliberate, not oversights:
        ///  * no ghost test. 0x649A58 and 0x64A844 both reject `[obj+0x73] != 0`
        ///    (0x649A64 / 0x64A873), but 0x67DBE8 only walks the TList at mgr+0xD4 comparing
        ///    each element against the id (0x67DC19 `3B 45 FC cmp eax,[ebp-4]`). Adding one
        ///    here would be stricter than native.
        ///  * the map and range tests are unconditional. The NPC arm runs them only when
        ///    0x6B8C17 `80 BE 5C 04 00 00 00 cmp byte[npc+0x45C],0` is non-zero.
        ///
        /// C# has one global actor registry rather than native's two, so membership of the
        /// monster registry is approximated by the runtime type. That is narrower than native
        /// in one case only: an object removed from the monster manager but still reachable
        /// through ObjectManager would be found here and dropped there.
        /// </summary>
        private void ClickScriptedMonster(int monsterId)
        {
            if (M2Share.ObjectManager.Get(monsterId) is not Monster animal) return;
            if (animal.m_PEnvir != m_PEnvir) return;
            if (Math.Abs(animal.m_nCurrX - m_nCurrX) > 15 ||
                Math.Abs(animal.m_nCurrY - m_nCurrY) > 15) return;
            M2Share.PasEngine?.TryCallMonsterMain(animal, this);
            // 0x6B8BA7 stores unconditionally, with no test of the script result, exactly as
            // the NPC arm does at 0x6B8C48. player+0xCD8 is untyped in native and TBaseObject
            // here, so a monster is as valid an occupant as an NPC.
            m_NPC = animal;
        }

        private int GetRangeHumanCount()
        {
            return M2Share.UserEngine.GetMapOfRangeHumanCount(m_PEnvir, m_nCurrX, m_nCurrY, 10);
        }

        private void GetStartPoint()
        {
            for (var i = 0; i < M2Share.StartPointList.Count; i++)
            {
                if (M2Share.StartPointList[i].m_sMapName == m_PEnvir.sMapName)
                {
                    if (M2Share.StartPointList[i] != null)
                    {
                        m_sHomeMap = M2Share.StartPointList[i].m_sMapName;
                        m_nHomeX = M2Share.StartPointList[i].m_nCurrX;
                        m_nHomeY = M2Share.StartPointList[i].m_nCurrY;
                    }
                }
            }

            if (PKLevel() >= 2)
            {
                m_sHomeMap = M2Share.g_Config.sRedHomeMap;
                m_nHomeX = M2Share.g_Config.nRedHomeX;
                m_nHomeY = M2Share.g_Config.nRedHomeY;
            }
        }

        private void MobPlace(string sX, string sY, string sMonName, string sCount)
        {

        }

        internal void DealCancel()
        {
            if (!m_boDealing)
            {
                return;
            }
            m_boDealing = false;
            SendDefMessage(Grobal2.SM_DEALCANCEL, 0, 0, 0, 0, "");
            // 战神 sub_6C43C4 @0x6C43F3-0x6C440C：`eax=[ebx+0xBAC]` / `test eax,eax` / `je` /
            //   **`mov dword ptr [eax+0xBAC], 0`** / `test eax,eax` / `je` / `call 0x6C43C4`。
            // 对端的 m_DealCreat 是在递归**之前、无条件**清零的。旧 C# 先递归，
            // 而对端 DealCancel 在 `!m_boDealing` 时提前 return，于是对端的 m_DealCreat
            // 仍指向自己（单边悬挂指针）——配合 ClientDealEnd 缺失的互指校验即构成
            // 「对已取消并已取回押金的一方再次成交」的复制漏洞。
            // DealCancelA()（UsrEngn.cs:1511 周期 SaveHumanRcd）会清一侧 m_boDealing，故此路可达。
            // 此处顺序与原生一致：先无条件清对端指针（同时也是递归终止条件），再递归。
            var dealRemote = m_DealCreat;
            if (dealRemote != null)
            {
                dealRemote.m_DealCreat = null;
                (dealRemote as TPlayObject)?.DealCancel();
            }
            m_DealCreat = null;
            GetBackDealItems();
            SysMsg(M2Share.g_sDealActionCancelMsg, MsgColor.Green, MsgType.Hint);
            m_DealLastTick = HUtil32.GetTickCount();
        }

        public void DealCancelA()
        {
            m_Abil.HP = m_WAbil.HP;
            DealCancel();
        }

        public bool DecGold(int nGold)
        {
            // 战神 sub_6C7D64 @0x6C7D67-0x6C7D73：
            //   `xor ecx,ecx` ; **`test edx,edx` / `jl 0x6C7D82`** ; `cmp edx,[eax+0x15C]` / `jg 0x6C7D82` ;
            //   `sub [eax+0x15C],edx` ; `call 0x6C19B4`(GoldChanged) ; `mov cl,1`。
            // 首个 `jl` = 负数金额直接返回 false（ecx 仍为 0），**不改动 m_nGold**。
            // 旧 C# 缺此门：DecGold(-N) 时 `m_nGold >= -N` 恒真 → `m_nGold -= -N` = **凭空造 N 金币**。
            // 可达路径：PAS `decgold`（PasApiBridge.cs:3503，args[0].AsInt() 未校验）。
            if (nGold < 0)
            {
                return false;
            }
            if (m_nGold >= nGold)
            {
                m_nGold -= nGold;
                // 0x6C7D7B `call 0x6C19B4` -- sub_6C19B4 is SendMsg with
                // cx=0x2798 (RM_GOLDCHANGED, 10136) and six zero slots, i.e.
                // GoldChanged().  Native fires it INSIDE the deduct, on the
                // success path only, so every caller gets the client refresh
                // for free and none of them re-sends it (checked 0x61EAFB and
                // 0x6418D5: both go straight on with the transaction).
                //
                // C# omitted it, so DecGold call sites that do not hand-roll a
                // GoldChanged() -- e.g. PAS `NpcLeaveTec` -- left the client
                // showing a stale purse.  The sites that DO hand-roll one are
                // now redundant rather than wrong (RM_GOLDCHANGED carries no
                // delta, it just tells the client to re-read m_nGold), and they
                // are left alone: removing them is a separate sweep and touching
                // ~8 unrelated files here would widen this change for no
                // behavioural gain.
                GoldChanged();
                return true;
            }
            return false;
        }

        // 战神 sub_726C3C 组队分经验加成表 @0x7D3E50。二进制里是 int32 且已 ×10 存储，
        // 逐字节 = {600,10,12,13,14,15,16,17,18,19,20,21}，由成员数 n 直接索引
        // （0x726CE7 `mov eax,[eax*4+0x7D3E50]`，eax = n）。
        // idx0(=600) 不可达（n<=1 先走单人分支），idx>=12 不可达（n>11 走单人分支）；
        // idx0 保留只为让下标与 n 对齐。旧 C# 的 double 表 {1,1.2,...,2.2} 整体错位一格
        // （C# idx1=1.2 而战神 idx1=1.0），且尾部 2.2 在战神中不存在。
        private static readonly int[] NativeGroupExpBonusX10 =
            { 600, 10, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 };

        // 战神 sub_728124 @0x728124：组队分经验的等级差惩罚。
        //   EAX(group) 参数在函数体内从不被读——是死参数，故此处不带。
        //   调用点 0x726D4D..0x726D57：EDX = self.Level（击杀者），
        //   ECX 沿用 0x726D35 装入的 member.Level，栈参 = share。
        //   即：成员等级 >= 击杀者等级 + 10 时按差额扣减。
        //   0x728180 = float 15.0；sub_403574 = Delphi Round（fistp，banker's）= HUtil32.Round。
        // 注意：这与 CalcGetExp(sub_6C02A4) 是两个不同函数——后者有 [self+0xBD0] 配置门且
        //       比较方向相反，不可复用。
        private static int NativeGroupExpLevelGapAdjust(int selfLevel, int otherLevel, int share)
        {
            if (share <= 0) return 0;                                   // 0x728134
            int r;
            if (otherLevel < selfLevel + 10)                            // 0x72813B jge -> 惩罚
            {
                r = share;                                              // 0x72813F 无惩罚
            }
            else
            {
                r = share - HUtil32.Round(share / 15.0 * (otherLevel - (selfLevel + 10)));  // 0x728146..0x728165
            }
            if (r <= 0) r = 1;                                          // 0x72816B 下限 1
            return r;
        }

        public void GainExp(int dwExp)
        {
            int n;
            int sumlv;
            TPlayObject PlayObject;
            const string sExceptionMsg = "[Exception] TPlayObject::GainExp";
            try
            {
                if (m_GroupOwner != null)
                {
                    // ---- 收集轮（战神 0x726C72..0x726CC4，定长 11 槽 `cmp esi,0xB`）----
                    // 四道门逐条对应：槽非空 / !IsDead([+0x74], sub_772DA8) / 同地图([+0x128]) /
                    // X、Y 两轴都在 12 格内（sub_7743E0，战神两轴都测）。
                    var members = new List<TPlayObject>();
                    sumlv = 0;
                    n = 0;
                    for (var i = 0; i < m_GroupOwner.m_GroupMembers.Count; i++)
                    {
                        PlayObject = m_GroupOwner.m_GroupMembers[i];
                        if (PlayObject != null && !PlayObject.m_boDeath && m_PEnvir == PlayObject.m_PEnvir && Math.Abs(m_nCurrX - PlayObject.m_nCurrX) <= 12 && Math.Abs(m_nCurrY - PlayObject.m_nCurrY) <= 12)
                        {
                            members.Add(PlayObject);
                            sumlv = sumlv + PlayObject.m_Abil.Level;    // 0x726CBA，movzx word [+0x278]
                            n++;                                        // 0x726CBD
                        }
                    }
                    // 三个回落到单人的出口：0x726CC6 / 0x726CD0 / 0x726CDA（`jg 0xB`，即 n<=11 通过）
                    if (sumlv > 0 && n > 1 && n <= Grobal2.GROUPMAX)
                    {
                        // 池加成：32 位截断乘法（imul 后紧跟 cdq 丢弃高半），再整数除 10。
                        // 0x726CE4..0x726CF9。夹取上界用的是【原始】dwExp（[ebp-8] 全程只在序言写一次），
                        // 不是加成后的 expB，所以这里绝不能覆写 dwExp。
                        int expB = unchecked(NativeGroupExpBonusX10[n] * dwExp) / 10;
                        // ---- 发放轮（战神 0x726D09..0x726DCD，遍历压实后的 member[0..n-1]）----
                        // 战神在此还有两道门，命中即跳过该成员（但该成员【仍已】计入 n 与 sumlv）：
                        //   A) sub_6D7788 = TestStatus(member, 25) —— 外挂/防沉迷惩罚状态（0x726D1B）
                        //   B) [member+0x1829] == 3 —— 防沉迷档位 3（0x726D28）
                        // C# 无这两个字段；战神默认值均为“未生效”（状态位未置、+0x1829 初值 0/1），
                        // 故此处省略即为忠实（缺口已记录，字段补齐后再接线）。
                        for (var k = 0; k < members.Count; k++)
                        {
                            PlayObject = members[k];
                            if (PlayObject == null) continue;           // 0x726D11
                            // 1) 份额：32 位截断乘法在前、整数除在后，然后 +1（0x726D45 `inc eax`）。
                            //    0x726D35..0x726D46。全程无 FPU。
                            int share = unchecked(PlayObject.m_Abil.Level * expB) / sumlv;
                            share += 1;
                            // 2) 等级差惩罚（0x726D57 sub_728124）
                            share = NativeGroupExpLevelGapAdjust(m_Abil.Level, PlayObject.m_Abil.Level, share);
                            // 3) 夹到【原始】dwExp（0x726D62..0x726D6A）
                            if (share > dwExp) share = dwExp;
                            // 4) 战神此处还有 ×1.5：`if (group->boDoubleExpActive) rate=1.5f;`
                            //    share = Round(share*rate)（0x726D6D..0x726D96，banker's）。
                            //    标志位在【组】对象上（[group+0x74]，+0x78 startTick / +0x7C durationMs），
                            //    由征召令道具 sub_727C1C 点亮、sub_727BC0 到期广播
                            //    “征召令的效果已消失，1.5倍经验时间结束”。
                            //    C# 的组没有这个定时 buff 字段；战神构造函数把它初始化为 0（0x726BE7），
                            //    即默认 rate=1.0，等价于不乘——故此处省略即为忠实（缺口已记录）。
                            PlayObject.WinExp(share);
                        }
                    }
                    else
                    {
                        WinExp(dwExp);
                    }
                }
                else
                {
                    WinExp(dwExp);
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg);
            }
        }

        public void GameTimeChanged()
        {
            if (m_btBright != M2Share.g_nGameTime)
            {
                m_btBright = (byte)M2Share.g_nGameTime;
                SendMsg(this, Grobal2.RM_DAYCHANGING, 0, 0, 0, 0, "");
            }
        }

        public void SetShengWan(int value)
        {
            m_nShengWan = value;
            SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
        }

        public void GetBackDealItems()
        {
            // 战神 sub_6C4114（GetBackDealItems 本体；0x6C40B8 只是 OpenDealDlg 对它的调用）：
            //   0x6C411B  8B 83 DC 06 00 00  mov eax,[ebx+0x6DC]   ; m_DealItemList
            //   0x6C4121  8B 40 08           mov eax,[eax+8]       ; .Count
            //   0x6C4126  7E 33              jle 0x6C415B          ; Count <= 0 跳过整段
            //   0x6C4128  8B F0 / 4E         mov esi,eax / dec esi ; i := Count-1
            //   0x6C412B  83 FE 00 / 7C 20   cmp esi,0 / jl 0x6C4150
            //   0x6C4130  8B D6              mov edx,esi           ; ← 取的是 i
            //   0x6C4138  E8 .. call 0x424D4C                      ; TList.Get(i)
            //   0x6C4145  E8 .. call 0x424AB8                      ; m_ItemList.Add
            //   0x6C414A  4E / 83 FE FF / 75 E0  dec esi / cmp esi,-1 / jne 0x6C4130
            // TRADE-57：**倒序**（Count-1 downto 0），旧 C# 是正序，于是取回押金后
            // 背包里这批物品的相对次序与原生完全相反。背包次序是可观测的：客户端
            // 按 m_ItemList 顺序铺格子，存档 THumInfoData.BagItems 也按同序落盘
            // （§1.4 记录布局），后续按下标操作的路径同样受影响。
            //
            // 0x6C4150 之后是 `call [DealItemList.vmt+8]`(Clear)，然后
            //   0x6C415B  8B 83 E0 06 00 00  mov eax,[ebx+0x6E0]   ; m_nDealGolds
            //   0x6C4161  01 83 5C 01 00 00  add [ebx+0x15C],eax   ; **裸加，不走 IncGold**
            //   0x6C4167  33 C0 / 89 83 E0 06 00 00  m_nDealGolds := 0
            //   0x6C416F  C6 83 84 06 00 00 00      m_boDealOK := false
            // 裸加是忠实的：这里退的是本人押金，押金在 ClientChangeDealGold 里
            // 已从 m_nGold 扣走（0x6C44D4 同样是裸写），加回来不可能超过扣走前的
            // 值，所以原生不需要 m_nGoldMax 门。**不要"修"成 IncGold** —— IncGold
            // 的 `jle` 会让 0 押金返回 false，且失败时静默吞掉押金。
            // 函数尾部无 0x73CEE4（WeightChanged）；那一句只在成交清理 sub_6C4A98 里。
            if (m_DealItemList.Count > 0)
            {
                for (var i = m_DealItemList.Count - 1; i >= 0; i--)
                {
                    m_ItemList.Add(m_DealItemList[i]);
                }
            }
            m_DealItemList.Clear();
            m_nGold += m_nDealGolds;
            m_nDealGolds = 0;
            m_boDealOK = false;
        }

        public override string GeTBaseObjectInfo()
        {
            return this.m_sCharName + " 标识:" + this.ObjectId + " 权限等级: " + this.m_btPermission + " 管理模式: " + HUtil32.BoolToStr(this.m_boAdminMode)
                + " 隐身模式: " + HUtil32.BoolToStr(this.m_boObMode) + " 无敌模式: " + HUtil32.BoolToStr(this.m_boSuperMan) + " 地图:" + this.m_sMapName + '(' + this.m_PEnvir.sMapDesc + ')'
                + " 座标:" + this.m_nCurrX + ':' + this.m_nCurrY + " 等级:" + this.m_Abil.Level + " 转生等级:" + m_btReLevel
                + " 经验:" + this.m_Abil.Exp + " 生命值: " + this.m_WAbil.HP + '-' + this.m_WAbil.MaxHP + " 魔法值: " + this.m_WAbil.MP + '-' + this.m_WAbil.MaxMP
                + " 攻击力: " + HUtil32.LoWord(this.m_WAbil.DC) + '-' + HUtil32.HiWord(this.m_WAbil.DC) + " 魔法力: " + HUtil32.LoWord(this.m_WAbil.MC) + '-'
                + HUtil32.HiWord(this.m_WAbil.MC) + " 道术: " + HUtil32.LoWord(this.m_WAbil.SC) + '-' + HUtil32.HiWord(this.m_WAbil.SC)
                + " 防御力: " + HUtil32.LoWord(this.m_WAbil.AC) + '-' + HUtil32.HiWord(this.m_WAbil.AC) + " 魔防力: " + HUtil32.LoWord(this.m_WAbil.MAC)
                + '-' + HUtil32.HiWord(this.m_WAbil.MAC) + " 准确:" + this.m_btHitPoint + " 敏捷:" + this.m_btSpeedPoint + " 速度:" + this.m_nHitSpeed
                + " 仓库密码:" + m_sStoragePwd + " 登录IP:" + m_sIPaddr + '(' + m_sIPLocal + ')' + " 登录帐号:" + m_sUserID + " 登录时间:" + m_dLogonTime
                + " 在线时长(分钟):" + ((HUtil32.GetTickCount() - m_dwLogonTick) / 60000) + " 登录模式:" + m_nPayMent + ' ' + M2Share.g_Config.sGameGoldName + ':' + m_nGameGold
                + ' ' + M2Share.g_Config.sGamePointName + ':' + m_nGamePoint + ' ' + M2Share.g_Config.sPayMentPointName + ':' + m_nPayMentPoint + " 会员类型:" + m_nMemberType
                + " 会员等级:" + m_nMemberLevel + " 经验倍数:" + (m_nKillMonExpRate / 100) + " 攻击倍数:" + (m_nPowerRate / 100) + " 声望值:" + m_btCreditPoint;
        }

        public int GetDigUpMsgCount()
        {
            var result = 0;
            SendMessage SendMessage;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                for (var i = 0; i < m_MsgList.Count; i++)
                {
                    SendMessage = m_MsgList[i];
                    if (SendMessage.wIdent == Grobal2.CM_BUTCH)
                    {
                        result++;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            return result;
        }

        public void GoldChange(string sChrName, int nGold)
        {
            string s10;
            string s14;
            if (nGold > 0)
            {
                s10 = "14";
                s14 = "增加完成";
            }
            else
            {
                s10 = "13";
                s14 = "以删减";
            }
            SysMsg(sChrName + " 的金币 " + nGold + " 金币" + s14, MsgColor.Green, MsgType.Hint);
            if (M2Share.g_boGameLogGold)
            {
                M2Share.AddGameDataLog(s10 + "\t" + m_sMapName + "\t" + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t" + Grobal2.sSTRING_GOLDNAME + "\t" + nGold + "\t" + '1' + "\t" + sChrName);
            }
        }

        public void ClearStatusTime()
        {
            ClearLegacyStatusSlots();
        }

        private void SendMapDescription()
        {
            var frames = BuildNativeMapDescriptionFrames();
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames[index];
                if (frame.IsBinary)
                    SendSocket(frame.Header, frame.BinaryBody);
                else
                    SendSocket(frame.Header, frame.TextBody);
            }
        }

        private void SendWhisperMsg(TPlayObject PlayObject)
        {
            if (PlayObject == this)
            {
                return;
            }
            if (PlayObject.m_btPermission >= 9 || m_btPermission >= 9)
            {
                return;
            }
            if (M2Share.UserEngine.PlayObjectCount < M2Share.g_Config.nSendWhisperPlayCount + M2Share.RandomNumber.Random(5))
            {
                return;
            }
        }

        private void ReadAllBook()
        {
            for (var i = 0; i < M2Share.UserEngine.m_MagicList.Count; i++)
            {
                var Magic = M2Share.UserEngine.m_MagicList[i];
                TUserMagic UserMagic = new TUserMagic
                {
                    MagicInfo = Magic,
                    wMagIdx = Magic.wMagicID,
                    btLevel = 2,
                    btKey = 0
                };
                UserMagic.btLevel = 0;
                UserMagic.nTranPoint = 100000;
                m_MagicList.Add(UserMagic);
                SendAddMagic(UserMagic);
            }
        }

        private void SendServerStatus()
        {
            if (m_btPermission < 10)
            {
                return;
            }
            
        }

        
        
        
        protected bool CretInNearXY(TBaseObject TargeTBaseObject, int nX, int nY)
        {
            MapCellinfo MapCellInfo;
            CellObject OSObject;
            TBaseObject BaseObject;
            if (m_PEnvir == null)
            {
                M2Share.MainOutMessage("CretInNearXY nil PEnvir");
                return false;
            }
            for (var nCX = nX - 1; nCX <= nX + 1; nCX++)
            {
                for (var nCY = nY - 1; nCY <= nY + 1; nCY++)
                {
                    var mapCell = false;
                    MapCellInfo = m_PEnvir.GetMapCellInfo(nCX, nCY, ref mapCell);
                    if (mapCell && MapCellInfo.ObjList != null)
                    {
                        for (var i = 0; i < MapCellInfo.Count; i++)
                        {
                            OSObject = MapCellInfo.ObjList[i];
                            if (OSObject.CellType == CellType.OS_MOVINGOBJECT)
                            {
                                BaseObject = OSObject.CellObj as TBaseObject;
                                if (BaseObject != null)
                                {
                                    if (!BaseObject.m_boGhost && BaseObject == TargeTBaseObject)
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        internal void SendUseitems()
        {
            var body = EncodeClientUseItemsWithCount(out var itemCount);
            if (body.Length == 0) return;
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SENDUSEITEMS,
                0, 0, itemCount, 0);
            SendSocket(m_DefMsg, body);
        }

        internal byte[] EncodeClientUseItems() => EncodeClientUseItemsWithCount(out _);

        private byte[] EncodeClientUseItemsWithCount(out int itemCount)
        {
            itemCount = 0;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
            {
                var item = m_UseItems[i];
                if (item == null || item.wIndex <= 0 || M2Share.UserEngine.GetStdItem(item.wIndex) == null)
                    continue;
                writer.Write(i);
                writer.Write(EncodeOwnedClientItemRecord(item));
                itemCount++;
            }
            return stream.ToArray();
        }

        private void SendUseMagic()
        {
            if (m_MagicList.Count == 0)
            {
                return;
            }

            using var stream = new MemoryStream(m_MagicList.Count * TNewClientMagic.RecordSize);
            for (var i = 0; i < m_MagicList.Count; i++)
            {
                var record = EncodeClientMagic(m_MagicList[i]);
                stream.Write(record, 0, record.Length);
            }
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SENDMYMAGIC, 0, 0, 0, (short)m_MagicList.Count);
            SendSocket(m_DefMsg, stream.ToArray());
        }

        internal static byte[] EncodeClientMagic(TUserMagic userMagic)
        {
            var magic = userMagic.MagicInfo;
            var level = userMagic.btLevel;
            var maxTrainIndex = magic.MaxTrain.Length == 0
                ? -1
                : Math.Min(level, (byte)(magic.MaxTrain.Length - 1));
            var clientMagic = new TNewClientMagic
            {
                MagicName = magic.sMagicName,
                MagicType = magic.wMagicID == 62 || magic.wMagicID == 112 ? (byte)1 : (byte)0,
                EffectType = magic.btEffectType,
                Effect = magic.btEffect,
                MagicId = magic.wMagicID,
                Level = level,
                Key = userMagic.btKey,
                // Native client-magic encoder fn 0x4C8498 writes the MP cost straight
                // from sub_4C8888 into this field with no further arithmetic:
                //   4C850A  mov eax,esi
                //   4C850C  call 0x4C8888
                //   4C8511  mov word ptr [ebx+0x18],ax    ; <- NeedMp
                // sub_4C8888 = Round((wSpell/4.0f) * (btLevel+1)) + btDefSpell, where
                // the divisor is the float32 4.0 at [0x4C88C8] and btDefSpell (+0x17) is
                // added INSIDE the function (@0x4C88BA). The previous expression divided
                // by (btTrainLv + 1) AND — because both operands were integral — did the
                // division in INTEGER arithmetic, truncating before the multiply, whereas
                // native fild's the dividend and rounds only once at sub_403574
                // (fistp qword, round-half-to-even). See
                // staging/heromagic_mpcost_fix_20260804.md §B.
                NeedMp = unchecked((short)GetNativeMagicProducerMpCost(userMagic)),
                CurTrain = userMagic.nTranPoint,
                MaxTrain = maxTrainIndex < 0 ? 0 : magic.MaxTrain[maxTrainIndex],
                DelayTime = magic.dwDelayTime
            };
            return clientMagic.GetBuffer();
        }

        private bool UseStdmodeFunItem(GoodItem StdItem)
        {
            var result = false;
            if (M2Share.g_FunctionNPC != null)
            {
                M2Share.g_FunctionNPC.GotoLable(this, "@StdModeFunc" + StdItem.AniCount, false);
                result = true;
            }
            return result;
        }

        public void RecalcAdjusBonus_AdjustAb(byte abil, short val, ref short lov, ref short hiv)
        {
            var lo = HUtil32.LoByte(abil);
            var hi = HUtil32.HiByte(abil);
            lov = 0;
            hiv = 0;
            for (var i = 0; i <= val; i++)
            {
                if (lo + 1 < hi)
                {
                    lo++;
                    lov++;
                }
                else
                {
                    hi++;
                    hiv++;
                }
            }
        }

        private void RecalcAdjusBonus()
        {
            TNakedAbility BonusTick;
            TNakedAbility NakedAbil;
            short adc;
            short amc;
            short asc;
            short aac;
            short amac;
            short ldc = 0;
            short lmc = 0;
            short lsc = 0;
            short lac = 0;
            short lmac = 0;
            short hdc = 0;
            short hmc = 0;
            short hsc = 0;
            short hac = 0;
            short hmac = 0;
            BonusTick = null;
            NakedAbil = null;
            switch (m_btJob)
            {
                case M2Share.jWarr:
                    BonusTick = M2Share.g_Config.BonusAbilofWarr;
                    NakedAbil = M2Share.g_Config.NakedAbilofWarr;
                    break;
                case M2Share.jWizard:
                    BonusTick = M2Share.g_Config.BonusAbilofWizard;
                    NakedAbil = M2Share.g_Config.NakedAbilofWizard;
                    break;
                case M2Share.jTaos:
                    BonusTick = M2Share.g_Config.BonusAbilofTaos;
                    NakedAbil = M2Share.g_Config.NakedAbilofTaos;
                    break;
            }
            adc = (short)(m_BonusAbil.DC / BonusTick.DC);
            amc = (short)(m_BonusAbil.MC / BonusTick.MC);
            asc = (short)(m_BonusAbil.SC / BonusTick.SC);
            aac = (short)(m_BonusAbil.AC / BonusTick.AC);
            amac = (short)(m_BonusAbil.MAC / BonusTick.MAC);
            RecalcAdjusBonus_AdjustAb((byte)NakedAbil.DC, adc, ref ldc, ref hdc);
            RecalcAdjusBonus_AdjustAb((byte)NakedAbil.MC, amc, ref lmc, ref hmc);
            RecalcAdjusBonus_AdjustAb((byte)NakedAbil.SC, asc, ref lsc, ref hsc);
            RecalcAdjusBonus_AdjustAb((byte)NakedAbil.AC, aac, ref lac, ref hac);
            RecalcAdjusBonus_AdjustAb((byte)NakedAbil.MAC, amac, ref lmac, ref hmac);
            m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.DC) + ldc, HUtil32.HiWord(m_WAbil.DC) + hdc);
            m_WAbil.MC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.MC) + lmc, HUtil32.HiWord(m_WAbil.MC) + hmc);
            m_WAbil.SC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.SC) + lsc, HUtil32.HiWord(m_WAbil.SC) + hsc);
            m_WAbil.AC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.AC) + lac, HUtil32.HiWord(m_WAbil.AC) + hac);
            m_WAbil.MAC = HUtil32.MakeLong(HUtil32.LoWord(m_WAbil.MAC) + lmac, HUtil32.HiWord(m_WAbil.MAC) + hmac);
            m_WAbil.MaxHP = (int)Math.Min(int.MaxValue,
                (long)m_WAbil.MaxHP + m_BonusAbil.HP / BonusTick.HP);
            m_WAbil.MaxMP = (int)Math.Min(int.MaxValue,
                (long)m_WAbil.MaxMP + m_BonusAbil.MP / BonusTick.MP);


        }

        private void ClientAdjustBonus(int nPoint, string sMsg)
        {
            var BonusAbil = new TNakedAbility();
            int nTotleUsePoint;
            
            
            nTotleUsePoint = BonusAbil.DC + BonusAbil.MC + BonusAbil.SC + BonusAbil.AC + BonusAbil.MAC + BonusAbil.HP + BonusAbil.MP + BonusAbil.Hit + BonusAbil.Speed + BonusAbil.X2;
            if (nPoint + nTotleUsePoint == m_nBonusPoint)
            {
                m_nBonusPoint = nPoint;
                m_BonusAbil.DC += BonusAbil.DC;
                m_BonusAbil.MC += BonusAbil.MC;
                m_BonusAbil.SC += BonusAbil.SC;
                m_BonusAbil.AC += BonusAbil.AC;
                m_BonusAbil.MAC += BonusAbil.MAC;
                m_BonusAbil.HP += BonusAbil.HP;
                m_BonusAbil.MP += BonusAbil.MP;
                m_BonusAbil.Hit += BonusAbil.Hit;
                m_BonusAbil.Speed += BonusAbil.Speed;
                m_BonusAbil.X2 += BonusAbil.X2;
                RecalcAbilitys();
                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
            }
            else
            {
                SysMsg("非法数据调整!!!", MsgColor.Red, MsgType.Hint);
            }
        }

        public int GetMyStatus()
        {
            var result = m_nHungerStatus / 1000;
            if (result > 4)
            {
                result = 4;
            }
            return result;
        }

        private void SendAdjustBonus()
        {
            // Native CODE 0x401000..0x7A10D0 has zero 16-bit dx/cx loads of 811
            // (0x032B) reaching a send slot. srv_AppearTimes.ini ident 811 = 0.
            // Keep the RM_ADJUST_BONUS consumer so routing stays, but do not emit.
        }

        private void ShowMapInfo(string sMap, string sX, string sY)
        {
            Envirnoment Map;
            var nX = (short)HUtil32.Str_ToInt(sX, 0);
            var nY = (short)HUtil32.Str_ToInt(sY, 0);
            if (sMap != "" && nX >= 0 && nY >= 0)
            {
                Map = M2Share.MapManager.FindMap(sMap);
                if (Map != null)
                {
                    var mapCell = false;
                    MapCellinfo MapCellInfo = Map.GetMapCellInfo(nX, nY, ref mapCell);
                    if (mapCell)
                    {
                        SysMsg("标志: " + MapCellInfo.Attribute, MsgColor.Green, MsgType.Hint);
                        if (MapCellInfo.ObjList != null)
                        {
                            SysMsg("对象数: " + MapCellInfo.Count, MsgColor.Green, MsgType.Hint);
                        }
                    }
                    else
                    {
                        SysMsg("取地图单元信息失败: " + sMap, MsgColor.Red, MsgType.Hint);
                    }
                }
            }
            else
            {
                SysMsg("请按正确格式输入: " + M2Share.g_GameCommand.MAPINFO.sCmd + " 地图号 X Y", MsgColor.Green, MsgType.Hint);
            }
        }

        public void PKDie(TPlayObject PlayObject)
        {
            var nWinLevel = M2Share.g_Config.nKillHumanWinLevel;
            var nLostLevel = M2Share.g_Config.nKilledLostLevel;
            var nWinExp = M2Share.g_Config.nKillHumanWinExp;
            var nLostExp = M2Share.g_Config.nKillHumanLostExp;
            var boWinLEvel = M2Share.g_Config.boKillHumanWinLevel;
            var boLostLevel = M2Share.g_Config.boKilledLostLevel;
            var boWinExp = M2Share.g_Config.boKillHumanWinExp;
            var boLostExp = M2Share.g_Config.boKilledLostExp;
            if (m_PEnvir.Flag.boPKWINLEVEL)
            {
                boWinLEvel = true;
                nWinLevel = m_PEnvir.Flag.nPKWINLEVEL;
            }
            if (m_PEnvir.Flag.boPKLOSTLEVEL)
            {
                boLostLevel = true;
                nLostLevel = m_PEnvir.Flag.nPKLOSTLEVEL;
            }
            if (m_PEnvir.Flag.boPKWINEXP)
            {
                boWinExp = true;
                nWinExp = m_PEnvir.Flag.nPKWINEXP;
            }
            if (m_PEnvir.Flag.boPKLOSTEXP)
            {
                boLostExp = true;
                nLostExp = m_PEnvir.Flag.nPKLOSTEXP;
            }
            if (PlayObject.m_Abil.Level - m_Abil.Level > M2Share.g_Config.nHumanLevelDiffer)
            {
                if (!PlayObject.IsGoodKilling(this))
                {
                    PlayObject.IncPkPoint(M2Share.g_Config.nKillHumanAddPKPoint);
                    PlayObject.SysMsg(M2Share.g_sYouMurderedMsg, MsgColor.Red, MsgType.Hint);
                    SysMsg(format(M2Share.g_sYouKilledByMsg, m_LastHiter.m_sCharName), MsgColor.Red, MsgType.Hint);
                    // 原版 sub_6C0FE4: 受害者 PK点 ≤ off_7D5FAC 时，对凶手 [+0x164] -= 1。
                    // 原 config nKillHumanDecLuckPoint 系伪造，原生为固定 -1。
                    PlayObject.AddBodyLuck(-1);
                    if (PKLevel() < 1)
                    {
                        if (M2Share.RandomNumber.Random(5) == 0)
                        {
                            PlayObject.MakeWeaponUnlock();
                        }
                    }
                    if (M2Share.g_FunctionNPC != null)
                    {
                        M2Share.g_FunctionNPC.GotoLable(PlayObject, "@OnMurder", false);
                        M2Share.g_FunctionNPC.GotoLable(this, "@Murdered", false);
                    }
                }
                else
                {
                    PlayObject.SysMsg(M2Share.g_sYouProtectedByLawOfDefense, MsgColor.Green, MsgType.Hint);
                }
                return;
            }
            if (boWinLEvel)
            {
                if (PlayObject.m_Abil.Level + nWinLevel <= M2Share.MAXUPLEVEL)
                {
                    PlayObject.m_Abil.Level += (ushort)nWinLevel;
                }
                else
                {
                    PlayObject.m_Abil.Level = M2Share.MAXUPLEVEL;
                }
                PlayObject.HasLevelUp(PlayObject.m_Abil.Level - nWinLevel);
                if (boLostLevel)
                {
                    if (PKLevel() >= 2)
                    {
                        if (m_Abil.Level >= nLostLevel * 2)
                        {
                            m_Abil.Level -= (ushort)(nLostLevel * 2);
                        }
                    }
                    else
                    {
                        if (m_Abil.Level >= nLostLevel)
                        {
                            m_Abil.Level -= (ushort)nLostLevel;
                        }
                    }
                }
            }
            if (boWinExp)
            {
                PlayObject.WinExp(nWinExp);
                if (boLostExp)
                {
                    if (m_Abil.Exp >= nLostExp)
                    {
                        if (m_Abil.Exp >= nLostExp)
                        {
                            m_Abil.Exp -= nLostExp;
                        }
                        else
                        {
                            m_Abil.Exp = 0;
                        }
                    }
                    else
                    {
                        if (m_Abil.Level >= 1)
                        {
                            m_Abil.Level -= 1;
                            m_Abil.Exp += GetLevelExp(m_Abil.Level);
                            if (m_Abil.Exp >= nLostExp)
                            {
                                m_Abil.Exp -= nLostExp;
                            }
                            else
                            {
                                m_Abil.Exp = 0;
                            }
                        }
                        else
                        {
                            m_Abil.Level = 0;
                            m_Abil.Exp = 0;
                        }
                    }
                }
            }
        }

        public bool CancelGroup()
        {
            var result = true;
            // 战神 sub_7270F8 @0x727158 push 0x7271BC，AnsiString 前缀 dword=19，
            // 正文「-你的小组被解散了。」（含全角句号）。旧 C# 用 ASCII 句号且缺前导 '-'。
            const string sCanceGrop = "-你的小组被解散了。";
            if (m_GroupMembers.Count <= 1)
            {
                SendGroupText(sCanceGrop);
                m_GroupMembers.Clear();
                m_GroupOwner = null;
                result = false;
            }
            return result;
        }

        public void SendGroupMembers()
        {
            TPlayObject PlayObject;
            var sSendMsg = "";
            for (var i = 0; i < m_GroupMembers.Count; i++)
            {
                PlayObject = m_GroupMembers[i];
                sSendMsg = sSendMsg + PlayObject.m_sCharName + '/';
            }
            for (var i = 0; i < m_GroupMembers.Count; i++)
            {
                PlayObject = m_GroupMembers[i];
                PlayObject.SendDefMessage(Grobal2.SM_GROUPMEMBERS, 0, 0, 0, 0, sSendMsg);
            }
        }

        internal ushort GetSpellPoint(TUserMagic UserMagic)
        {
            return GetNativeMagicProducerMpCost(UserMagic);
        }

        private bool DoSpell(TUserMagic UserMagic, short nTargetX, short nTargetY, TBaseObject BaseObject)
        {
            var result = false;
            try
            {
                if (!M2Share.MagicManager.IsWarrSkill(UserMagic.wMagIdx))
                {
                    var nSpellPoint = GetSpellPoint(UserMagic);
                    if (m_WAbil.MP < nSpellPoint)
                    {
                        return result;
                    }
                    // sub_6ED62C calls both routines even when the computed
                    // cost is zero; the MP publication remains observable.
                    DamageSpell(nSpellPoint);
                    HealthSpellChanged();
                    result = M2Share.MagicManager.DoSpell(this, UserMagic, nTargetX, nTargetY, BaseObject);
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(format("[Exception] TPlayObject.DoSpell MagID:{0} X:{1} Y:{2}", UserMagic.wMagIdx, nTargetX, nTargetY));
                M2Share.ErrorMessage(e.Message);
            }
            return result;
        }

        
        
        
        
        
        
        private bool PileStones(int nX, int nY)
        {
            // MINE-46: 三道早退全部 `je/jne 0x6BC366`，而 0x6BC366 是函数 epilogue
            // （`5F 5E 5B 8B E5 5D C3` pop edi/esi/ebx / mov esp,ebp / pop ebp / ret），
            // 不是另一段逻辑。RM_HEAVYHIT 广播在 try 块尾 0x6BC306（`66 BA 15 27`
            // ident 0x2715 / `call [vmt+0xD8]`），早退到不了那里。
            //   0x6BC202  80 BB 28 18 00 00 03   cmp byte [ebx+0x1828],3  ; fatigue
            //   0x6BC209  0F 84 57 01 00 00      je  0x6BC366
            //   0x6BC211  E8 72 B5 01 00         call 0x6D7788            ; HasState(0x19)=25
            //   0x6BC216  84 C0                  test al,al
            //   0x6BC218  0F 85 48 01 00 00      jne 0x6BC366
            //   0x6BC21E  80 BB 29 18 00 00 03   cmp byte [ebx+0x1829],3  ; cheat
            //   0x6BC225  0F 84 3B 01 00 00      je  0x6BC366
            if (m_btNativeFatigueTier == 3
                || HasNativeActiveState(25)
                || m_btNativeCheatPenaltyTier == 3)
            {
                return false;
            }

            var result = false;
            var s1C = string.Empty;
            // MINE-15: 原版取不到矿点就地创建，挖矿是唯一创建点（类引用单元
            // 0x71683C 在全镜像只有 0x6BC277 一处代码引用）：
            //   0x6BC25E  E8 15 ED 0B 00  call 0x77AF78     ; 按格取矿点
            //   0x6BC263  8B F0           mov esi,eax
            //   0x6BC265  85 F6           test esi,esi
            //   0x6BC267  75 19           jne 0x6BC282      ; 已存在则直接用
            //   0x6BC276  A1 3C 68 71 00  mov eax,[0x71683C]; TStoneMineEvent
            //   0x6BC27B  E8 D8 B3 05 00  call 0x717658     ; ctor
            // 创建点落在格子门之后、MineCount 判定之前，抽签序不变。
            // 取矿点走带类型的重载而不是类强转：原版 0x77AF78 只认节点种类
            // 字节 8（0x77AFB0 cmp byte[node],8），格上挂着别的 Event 时
            // 原版走链继续、最终返回 null，不会当成矿点。
            var mineEvent =
                m_PEnvir.GetEvent(nX, nY, Grobal2.ET_MINE) as StoneMineEvent
                ?? new StoneMineEvent(m_PEnvir, nX, nY, Grobal2.ET_MINE);
            if (mineEvent.MineCount > 0)
            {
                mineEvent.MineCount -= 1;
                // MINE-61: Native @0x717715 hardcodes hit rate = 4 (mov eax,4; call 0x403B4C).
                if (M2Share.RandomNumber.Random(4) == 0)
                {
                    // MINE-55: 原版按 kind 判别取石堆，不做类判定；不匹配就继续
                    // 走链、最终返回 null：
                    //   0x717723  6A 03              push 3               ; 期望的 kind
                    //   0x717737  E8 54 1A 06 00     call 0x779190
                    //     0x7791CD  80 38 03         cmp byte [node],3    ; 节点种类
                    //     0x7791E1  3A 58 0C         cmp bl,byte [obj+0xC]; 事件类型
                    //     0x7791E4  75 04            jne next             ; 不匹配继续走链
                    //   0x71773C  85 C0              test eax,eax
                    //   0x71773E  75 33              jne 0x717773         ; 命中 → AddEventParam
                    // C# 原来用 (PileStones) 强转 GetEvent(x,y) 的返回值，而
                    // Envirnoment.GetEvent(x,y) 返回该格**最后一个**
                    // OS_EVENTOBJECT、不区分类型，所以格上放过火墙/圣言术屏障
                    // 等任何别的 Event 子类时会抛 InvalidCastException。
                    // 强转在类型判断之前就炸了，下面那个 m_nEventType 判断因此
                    // 是不可达的死代码；改成带类型的重载后它也不再需要。
                    var pileEvent =
                        m_PEnvir.GetEvent(m_nCurrX, m_nCurrY, Grobal2.ET_PILESTONES)
                            as PileStones;
                    if (pileEvent == null)
                    {
                        pileEvent = new PileStones(m_PEnvir, m_nCurrX, m_nCurrY, Grobal2.ET_PILESTONES, 5 * 60 * 1000);
                        M2Share.EventManager.AddEvent(pileEvent);
                    }
                    else
                    {
                        pileEvent.AddEventParam();
                    }
                    // MINE-21/MINE-61: Tier==2 halves ore output rate. Both branches
                    // are HARDCODED to match native: 0x6BC2A3 `cmp byte[ebx+0x1828],2`
                    // -> 0x6BC2AC `mov eax,0x18`(24) tier2 / 0x6BC2C3 `mov eax,0xC`(12)
                    // normal. 原生无区间/比率配置；此前正常档误用 nMakeMineRate 配置，
                    // 默认 12 虽同值但配置化即偏离原生，故改回硬编码（MINE-61）。
                    // Normal: Random(12) -> effective 1/4 * 1/12 = 1/48
                    // Tier 2: Random(24) -> effective 1/4 * 1/24 = 1/96
                    int mineRate = m_btNativeFatigueTier == 2 ? 24 : 12;
                    // MINE-08: MINE 旗标由派发器在 0x6EC0FE 测过了（非 MINE 图
                    // 根本到不了这里），产出卷因此无条件抽。原来这里的注释把
                    // 旗标地址写成 0x6BC24A —— 那是格子地形门 cmp byte[cell],0，
                    // 不是旗标，以字节为准。
                    if (M2Share.RandomNumber.Random(mineRate) == 0)
                    {
                        MakeMine();
                    }
                    // MINE-50: 原版顺序是先扣耐久再发成功包，不可颠倒：
                    //   0x6BC2D8  B8 0F 00 00 00  mov eax,0x0F   ; Random(15)
                    //   0x6BC2E4  83 C2 05        add edx,5      ; +5
                    //   0x6BC2E9  E8 16 25 08 00  call 0x73E804  ; DoDamageWeapon
                    //   0x6BC2F8  66 BA 74 02     mov dx,0x274   ; ident 628
                    //   0x6BC300  FF 96 50 02 00 00 call [esi+0x250] ; SendDefMessage
                    DoDamageWeapon(M2Share.RandomNumber.Random(15) + 5);
                    SendDefMessage(Grobal2.SM_MINESUCCESS, 0, 0, 0, 0, string.Empty);
                    result = true;
                }
            }
            else
            {
                if ((HUtil32.GetTickCount() - mineEvent.AddStoneMineTick) > 10 * 60 * 1000)
                {
                    mineEvent.AddStoneMine();
                }
            }
            // MINE-51: Native @0x6BC306 broadcast RM_HEAVYHIT (0x2715) payload:
            // success in nParam (1/0), string empty. C# had it swapped.
            SendRefMsg(Grobal2.RM_HEAVYHIT, m_btDirection, m_nCurrX, m_nCurrY, result ? 1 : 0, string.Empty);
            return result;
        }

        internal void SendSaveItemList(int nBaseObject)
        {
            var totalCount = m_StorageItemList.Count;
            var totalPages = HUtil32._MAX(2, (totalCount + STORAGE_PAGE_SIZE - 1) / STORAGE_PAGE_SIZE);
            if (m_nStoragePage < 0)
            {
                m_nStoragePage = 0;
            }
            if (m_nStoragePage >= totalPages)
            {
                m_nStoragePage = totalPages - 1;
            }
            var startIndex = m_nStoragePage * STORAGE_PAGE_SIZE;
            var endIndex = HUtil32._MIN(totalCount, startIndex + STORAGE_PAGE_SIZE);
            var pageItemCount = 0;
            using var stream = new MemoryStream();
            for (var i = startIndex; i < endIndex; i++)
            {
                var item = m_StorageItemList[i];
                if (item == null || M2Share.UserEngine.GetStdItem(item.wIndex) == null) continue;
                var record = EncodeOwnedClientItemRecord(item);
                stream.Write(record, 0, record.Length);
                pageItemCount++;
            }
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SAVEITEMLIST, nBaseObject,
                pageItemCount, STORAGE_PAGE_SIZE, m_nStoragePage);
            SendSocket(m_DefMsg, stream.ToArray());
        }

        private void SendChangeGuildName()
        {
            // Native RM 10301 handler 0x6B624C is the dispatcher finally, not a
            // send. CODE has zero 16-bit dx/cx loads of 750 (0x02EE) reaching a
            // send slot. srv_AppearTimes.ini 750=0. Constant kept.
        }

        private static byte[] BuildDelItemListBody(IList<TDeleteItem> ItemList)
        {
            using var stream = new MemoryStream(
                ItemList.Count * sizeof(uint) + sizeof(byte));
            using var writer = new BinaryWriter(stream);
            for (var i = 0; i < ItemList.Count; i++)
            {
                var deleteItem = ItemList[i];
                var clientItemId = deleteItem.ClientItemID != 0
                    ? deleteItem.ClientItemID
                    : deleteItem.MakeIndex;
                writer.Write((uint)clientItemId);
            }
            if (ItemList.Count > 0)
                writer.Write((byte)0);
            return stream.ToArray();
        }

        private void SendDelItemList(IList<TDeleteItem> ItemList, int itemCount)
        {
            var body = BuildDelItemListBody(ItemList);
            m_DefMsg = Grobal2.MakeDefaultMsg(
                Grobal2.SM_DELITEMS, itemCount, 0, 0, 0);
            SendSocket(m_DefMsg, body);
        }

        public void SendDelItems(TUserItem UserItem)
        {
            var clientItemId = EnsureClientItemId(UserItem);
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_DELITEM, clientItemId, 0, 0, 1);
            SendSocket(m_DefMsg);
        }

        internal void SendStorageItemOk(TUserItem userItem)
        {
            var clientItemId = EnsureClientItemId(userItem);
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_STORAGE_OK,
                clientItemId, 0, 0, 0);
            SendSocket(m_DefMsg, EncodeOwnedClientItemRecord(userItem));
        }

        public void SendUpdateItem(TUserItem UserItem)
        {
            if (M2Share.UserEngine.GetStdItem(UserItem.wIndex) == null) return;
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_UPDATEITEM, ObjectId, 0, 0, 1);
            SendSocket(m_DefMsg, EncodeClientItemRecord(UserItem));
        }

        private bool CheckTakeOnItems(int nWhere, ref TStdItem StdItem)
        {
            var result = false;
            TUserCastle Castle;
            if (StdItem.StdMode == 10 && m_btGender != PlayGender.Man)
            {
                SysMsg(M2Share.sWearNotOfWoMan, MsgColor.Red, MsgType.Hint);
                return false;
            }
            if (StdItem.StdMode == 11 && m_btGender != PlayGender.WoMan)
            {
                SysMsg(M2Share.sWearNotOfMan, MsgColor.Red, MsgType.Hint);
                return false;
            }
            if (nWhere == 1 || nWhere == 2)
            {
                if (StdItem.Weight > m_WAbil.MaxHandWeight)
                {
                    SysMsg(M2Share.sHandWeightNot, MsgColor.Red, MsgType.Hint);
                    return false;
                }
            }
            else
            {
                if (StdItem.Weight + GetUserItemWeitht(nWhere) > m_WAbil.MaxWearWeight)
                {
                    SysMsg(M2Share.sWearWeightNot, MsgColor.Red, MsgType.Hint);
                    return false;
                }
            }
            Castle = M2Share.CastleManager.IsCastleMember(this);
            switch (StdItem.Need)
            {
                case 0:
                    if (m_Abil.Level >= StdItem.NeedLevel)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sLevelNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 1:
                    if (HUtil32.HiWord(m_WAbil.DC) >= StdItem.NeedLevel)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sDCNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 10:
                    if (m_btJob == HUtil32.LoWord(StdItem.NeedLevel) && m_Abil.Level >= HUtil32.HiWord(StdItem.NeedLevel))
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sJobOrLevelNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 11:
                    if (m_btJob == HUtil32.LoWord(StdItem.NeedLevel) && HUtil32.HiWord(m_WAbil.DC) >= HUtil32.HiWord(StdItem.NeedLevel))
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sJobOrDCNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 12:
                    if (m_btJob == HUtil32.LoWord(StdItem.NeedLevel) && HUtil32.HiWord(m_WAbil.MC) >= HUtil32.HiWord(StdItem.NeedLevel))
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sJobOrMCNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 13:
                    if (m_btJob == HUtil32.LoWord(StdItem.NeedLevel) && HUtil32.HiWord(m_WAbil.SC) >= HUtil32.HiWord(StdItem.NeedLevel))
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sJobOrSCNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 2:
                    if (HUtil32.HiWord(m_WAbil.MC) >= StdItem.NeedLevel)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sMCNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 3:
                    if (HUtil32.HiWord(m_WAbil.SC) >= StdItem.NeedLevel)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sSCNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 4:
                    if (m_btReLevel >= StdItem.NeedLevel)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sReNewLevelNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 40:
                    if (m_btReLevel >= HUtil32.LoWord(StdItem.NeedLevel))
                    {
                        if (m_Abil.Level >= HUtil32.HiWord(StdItem.NeedLevel))
                        {
                            result = true;
                        }
                        else
                        {
                            SysMsg(M2Share.g_sLevelNot, MsgColor.Red, MsgType.Hint);
                        }
                    }
                    else
                    {
                        SysMsg(M2Share.g_sReNewLevelNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 41:
                    if (m_btReLevel >= HUtil32.LoWord(StdItem.NeedLevel))
                    {
                        if (HUtil32.HiWord(m_WAbil.DC) >= HUtil32.HiWord(StdItem.NeedLevel))
                        {
                            result = true;
                        }
                        else
                        {
                            SysMsg(M2Share.g_sDCNot, MsgColor.Red, MsgType.Hint);
                        }
                    }
                    else
                    {
                        SysMsg(M2Share.g_sReNewLevelNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 42:
                    if (m_btReLevel >= HUtil32.LoWord(StdItem.NeedLevel))
                    {
                        if (HUtil32.HiWord(m_WAbil.MC) >= HUtil32.HiWord(StdItem.NeedLevel))
                        {
                            result = true;
                        }
                        else
                        {
                            SysMsg(M2Share.g_sMCNot, MsgColor.Red, MsgType.Hint);
                        }
                    }
                    else
                    {
                        SysMsg(M2Share.g_sReNewLevelNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 43:
                    if (m_btReLevel >= HUtil32.LoWord(StdItem.NeedLevel))
                    {
                        if (HUtil32.HiWord(m_WAbil.SC) >= HUtil32.HiWord(StdItem.NeedLevel))
                        {
                            result = true;
                        }
                        else
                        {
                            SysMsg(M2Share.g_sSCNot, MsgColor.Red, MsgType.Hint);
                        }
                    }
                    else
                    {
                        SysMsg(M2Share.g_sReNewLevelNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 44:
                    if (m_btReLevel >= HUtil32.LoWord(StdItem.NeedLevel))
                    {
                        if (m_btCreditPoint >= HUtil32.HiWord(StdItem.NeedLevel))
                        {
                            result = true;
                        }
                        else
                        {
                            SysMsg(M2Share.g_sCreditPointNot, MsgColor.Red, MsgType.Hint);
                        }
                    }
                    else
                    {
                        SysMsg(M2Share.g_sReNewLevelNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 5:
                    if (m_btCreditPoint >= StdItem.NeedLevel)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sCreditPointNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 6:
                    if (m_MyGuild != null)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sGuildNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 60:
                    if (m_MyGuild != null && m_nGuildRankNo == 1)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sGuildMasterNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 7:
                    if (m_MyGuild != null && Castle != null)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sSabukHumanNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 70:
                    if (m_MyGuild != null && Castle != null && m_nGuildRankNo == 1)
                    {
                        if (m_Abil.Level >= StdItem.NeedLevel)
                        {
                            result = true;
                        }
                        else
                        {
                            SysMsg(M2Share.g_sLevelNot, MsgColor.Red, MsgType.Hint);
                        }
                    }
                    else
                    {
                        SysMsg(M2Share.g_sSabukMasterManNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 8:
                    if (m_nMemberType != 0)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sMemberNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 81:
                    if (m_nMemberType == HUtil32.LoWord(StdItem.NeedLevel) && m_nMemberLevel >= HUtil32.HiWord(StdItem.NeedLevel))
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sMemberTypeNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
                case 82:
                    if (m_nMemberType >= HUtil32.LoWord(StdItem.NeedLevel) && m_nMemberLevel >= HUtil32.HiWord(StdItem.NeedLevel))
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sMemberTypeNot, MsgColor.Red, MsgType.Hint);
                    }
                    break;
            }
            return result;
        }

        private int GetUserItemWeitht(int nWhere)
        {
            int result;
            var n14 = 0;
            GoodItem StdItem;
            for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
            {
                if (nWhere == -1 || !(i == nWhere) && !(i == 1) && !(i == 2))
                {
                    if (m_UseItems[i] == null)
                    {
                        continue;
                    }
                    StdItem = M2Share.UserEngine.GetStdItem(m_UseItems[i].wIndex);
                    if (StdItem != null)
                    {
                        n14 += StdItem.Weight;
                    }
                }
            }
            result = n14;
            return result;
        }

        private bool EatItems(GoodItem StdItem, TUserItem Useritem)
        {
            var result = false;
            if (m_PEnvir.Flag.boNODRUG)
            {
                SysMsg(M2Share.sCanotUseDrugOnThisMap, MsgColor.Red, MsgType.Hint);
                return result;
            }
            switch (StdItem.StdMode)
            {
                case 0:
                    switch (StdItem.Shape)
                    {
                        case 1:
                            IncHealthSpell(StdItem.Ac, StdItem.Mac);
                            result = true;
                            break;
                        case 2:
                            m_boUserUnLockDurg = true;
                            result = true;
                            break;
                        case 3:
                            IncHealthSpell(HUtil32.Round(m_WAbil.MaxHP / 100 * StdItem.Ac), HUtil32.Round(m_WAbil.MaxMP / 100 * StdItem.Mac));
                            result = true;
                            break;
                        default:
                            if (StdItem.Ac > 0)
                            {
                                m_nIncHealth += StdItem.Ac;
                            }
                            if (StdItem.Mac > 0)
                            {
                                m_nIncSpell += StdItem.Mac;
                            }
                            result = true;
                            break;
                    }
                    break;
                case 1:
                    var nOldStatus = GetMyStatus();
                    m_nHungerStatus += StdItem.DuraMax / 10;
                    m_nHungerStatus = HUtil32._MIN(5000, m_nHungerStatus);
                    if (nOldStatus != GetMyStatus())
                    {
                        RefMyStatus();
                    }
                    result = true;
                    break;
                case 2:
                    result = true;
                    break;
                case 3:
                    switch (StdItem.Shape)
                    {
                        case 12:
                            var boNeedRecalc = false;
                            if (StdItem.Dc > 0)
                            {
                                m_wStatusArrValue[0] = StdItem.Dc;
                                m_dwStatusArrTimeOutTick[0] = HUtil32.GetTickCount() + StdItem.Mac2 * 1000;
                                SysMsg("攻击力增加" + StdItem.Mac2 + "秒.", MsgColor.Green, MsgType.Hint);
                                boNeedRecalc = true;
                            }
                            if (StdItem.Mc > 0)
                            {
                                m_wStatusArrValue[1] = StdItem.Mc;
                                m_dwStatusArrTimeOutTick[1] = HUtil32.GetTickCount() + StdItem.Mac2 * 1000;
                                SysMsg("魔法力增加" + StdItem.Mac2 + "秒.", MsgColor.Green, MsgType.Hint);
                                boNeedRecalc = true;
                            }
                            if (StdItem.Sc > 0)
                            {
                                m_wStatusArrValue[2] = StdItem.Sc;
                                m_dwStatusArrTimeOutTick[2] = HUtil32.GetTickCount() + StdItem.Mac2 * 1000;
                                SysMsg("道术增加" + StdItem.Mac2 + "秒.", MsgColor.Green, MsgType.Hint);
                                boNeedRecalc = true;
                            }
                            if (StdItem.Ac2 > 0)
                            {
                                m_wStatusArrValue[3] = StdItem.Ac2;
                                m_dwStatusArrTimeOutTick[3] = HUtil32.GetTickCount() + StdItem.Mac2 * 1000;
                                SysMsg("攻击速度增加" + StdItem.Mac2 + "秒.", MsgColor.Green, MsgType.Hint);
                                boNeedRecalc = true;
                            }
                            if (StdItem.Ac > 0)
                            {
                                m_wStatusArrValue[4] = StdItem.Ac;
                                m_dwStatusArrTimeOutTick[4] = HUtil32.GetTickCount() + StdItem.Mac2 * 1000;
                                SysMsg("生命值增加" + StdItem.Mac2 + "秒.", MsgColor.Green, MsgType.Hint);
                                boNeedRecalc = true;
                            }
                            if (StdItem.Mac > 0)
                            {
                                m_wStatusArrValue[5] = StdItem.Mac;
                                m_dwStatusArrTimeOutTick[5] = HUtil32.GetTickCount() + StdItem.Mac2 * 1000;
                                SysMsg("魔法值增加" + StdItem.Mac2 + "秒.", MsgColor.Green, MsgType.Hint);
                                boNeedRecalc = true;
                            }
                            if (boNeedRecalc)
                            {
                                RecalcAbilitys();
                                SendMsg(this, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                                result = true;
                            }
                            break;
                        case 13:
                            GetExp(StdItem.DuraMax);
                            result = true;
                            break;
                        default:
                            result = EatUseItems(StdItem.Shape);
                            break;
                    }
                    break;
            }
            return result;
        }

        private bool ReadBook(GoodItem StdItem)
        {
            TUserMagic UserMagic;
            TPlayObject PlayObject;
            var result = false;
            var magic = M2Share.UserEngine.FindMagic(StdItem.Name);
            if (magic == null)
            {
                SysMsg($"[{StdItem.Name}] 没有对应的技能配置。", MsgColor.Red, MsgType.Hint);
                return false;
            }
            if (IsTrainingSkill(magic.wMagicID))
            {
                SysMsg($"[{magic.sMagicName}] 已经学过了。", MsgColor.Red, MsgType.Hint);
                return false;
            }
            if (magic.btJob != 99 && magic.btJob != m_btJob)
            {
                SysMsg($"当前职业不能学习 [{magic.sMagicName}]。", MsgColor.Red, MsgType.Hint);
                return false;
            }
            if (m_Abil.Level < magic.TrainLevel[0])
            {
                SysMsg($"等级不足，[{magic.sMagicName}] 需要 {magic.TrainLevel[0]} 级。", MsgColor.Red, MsgType.Hint);
                return false;
            }
            UserMagic = new TUserMagic
            {
                MagicInfo = magic,
                wMagIdx = magic.wMagicID,
                btKey = 0,
                btLevel = 0,
                nTranPoint = 0
            };
            m_MagicList.Add(UserMagic);
            if (m_MagicArr != null && UserMagic.wMagIdx >= 0 && UserMagic.wMagIdx < m_MagicArr.Length)
            {
                m_MagicArr[UserMagic.wMagIdx] = UserMagic;
            }
            RecalcAbilitys();
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                PlayObject = this;
                PlayObject.SendAddMagic(UserMagic);
            }
            result = true;
            return result;
        }

        public void SendAddMagic(TUserMagic UserMagic)
        {
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_ADDMAGIC, 0, 0, 0, 1);
            SendSocket(m_DefMsg, EncodeClientMagic(UserMagic));
        }

        internal void SendDelMagic(TUserMagic UserMagic)
        {
            var magicId = UserMagic.MagicInfo?.wMagicID ?? UserMagic.wMagIdx;
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_DELMAGIC, magicId, 0, 0, 1);
            SendSocket(m_DefMsg);
        }

        
        
        
        
        
        private bool EatUseItems(int nShape)
        {
            var result = false;
            switch (nShape)
            {
                case 1:
                    SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, 0, 0, 0, "");
                    BaseObjectMove(m_sHomeMap, 0, 0);
                    result = true;
                    break;
                case 2:
                    if (!m_PEnvir.Flag.boNORANDOMMOVE)
                    {
                        SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, 0, 0, 0, "");
                        BaseObjectMove(m_sMapName, 0, 0);
                        result = true;
                    }
                    break;
                case 3:
                    SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, 0, 0, 0, "");
                    if (PKLevel() < 2)
                    {
                        BaseObjectMove(m_sHomeMap, m_nHomeX, m_nHomeY);
                    }
                    else
                    {
                        BaseObjectMove(M2Share.g_Config.sRedHomeMap, M2Share.g_Config.nRedHomeX, M2Share.g_Config.nRedHomeY);
                    }
                    result = true;
                    break;
                case 4:
                    if (WeaptonMakeLuck())
                    {
                        result = true;
                    }
                    break;
                case 5:
                    if (m_MyGuild != null)
                    {
                        if (!m_boInFreePKArea)
                        {
                            TUserCastle Castle = M2Share.CastleManager.IsCastleMember(this);
                            if (Castle != null && Castle.IsMasterGuild(m_MyGuild))
                            {
                                BaseObjectMove(Castle.m_sHomeMap, Castle.GetHomeX(), Castle.GetHomeY());
                            }
                            else
                            {
                                SysMsg("无效", MsgColor.Red, MsgType.Hint);
                            }
                            result = true;
                        }
                        else
                        {
                            SysMsg("此处无法使用", MsgColor.Red, MsgType.Hint);
                        }
                    }
                    break;
                case 9:
                    if (RepairWeapon())
                    {
                        result = true;
                    }
                    break;
                case 10:
                    if (SuperRepairWeapon())
                    {
                        result = true;
                    }
                    break;
                case 11:
                    WinLottery();
                    result = true;
                    break;
            }
            return result;
        }

        internal void MoveToHome()
        {
            SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, 0, 0, 0, "");
            BaseObjectMove(m_sHomeMap, m_nHomeX, m_nHomeY);
        }

        private void BaseObjectMove(string sMap, short sX, short sY)
        {
            if (string.IsNullOrEmpty(sMap))
            {
                sMap = m_sMapName;
            }
            if (sX != 0 && sY != 0)
            {
                short nX = sX;
                short nY = sY;
                SpaceMove(sMap, nX, nY, 0);
            }
            else
            {
                MapRandomMove(sMap, 0);
            }
            var envir = m_PEnvir;
            if (envir != m_PEnvir && m_btRaceServer == Grobal2.RC_PLAYOBJECT)
            {
                m_boTimeRecall = false;
            }
        }

        internal void ChangeServerMakeSlave(TSlaveInfo slaveInfo)
        {
            int nSlavecount = 0;
            if (m_btJob == M2Share.jTaos)
            {
                nSlavecount = 1;
            }
            else
            {
                nSlavecount = 5;
            }
            var BaseObject = MakeNativeSlave(slaveInfo.sSlaveName,
                slaveInfo.btSlaveLevel, nSlavecount, slaveInfo.dwRoyaltySec,
                fromHero: false, hpAfterSlave: 10);
            if (BaseObject != null)
            {
                BaseObject.m_WAbil.HP = unchecked((ushort)slaveInfo.nHP);
                BaseObject.m_WAbil.MP = unchecked((ushort)slaveInfo.nMP);
                if (BaseObject.m_btRaceServer == 0x97)
                {
                    (BaseObject as HolyMonster)?.NativeBindHolyBeastSummoner(this);
                }
                else
                {
                    BaseObject.m_nKillMonCount = slaveInfo.nKillCount;
                    BaseObject.m_btSlaveExpLevel = slaveInfo.btSlaveExpLevel;
                    var walkSpeed = 1500 - slaveInfo.btSlaveLevel * 200;
                    if (walkSpeed < BaseObject.m_nWalkSpeed)
                        BaseObject.m_nWalkSpeed = walkSpeed;
                    var nextHitTime = 2000 - slaveInfo.btSlaveLevel * 200;
                    if (nextHitTime < BaseObject.m_nNextHitTime)
                        BaseObject.m_nNextHitTime = nextHitTime;
                }
                BaseObject.RecalcAbilitys();
            }
        }

        private void SendAddDealItem(TUserItem UserItem)
        {
            SendDefMessage(Grobal2.SM_DEALADDITEM_OK, 0, 0, 0, 0, "");
            if (m_DealCreat is TPlayObject remote
                && M2Share.UserEngine.GetStdItem(UserItem.wIndex) != null)
            {
                m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_DEALREMOTEADDITEM, ObjectId, 0, 0, 1);
                remote.SendSocket(m_DefMsg, EncodeOwnedClientItemRecord(UserItem));
                m_DealCreat.m_DealLastTick = HUtil32.GetTickCount();
                m_DealLastTick = HUtil32.GetTickCount();
            }
        }

        private void OpenDealDlg(TBaseObject BaseObject)
        {
            m_boDealing = true;
            m_DealCreat = BaseObject;
            GetBackDealItems();
            SendDefMessage(Grobal2.SM_DEALMENU, 0, 0, 0, 0, m_DealCreat.m_sCharName);
            m_DealLastTick = HUtil32.GetTickCount();
        }

        private void JoinGroup(TPlayObject PlayObject)
        {
            m_GroupOwner = PlayObject;
            SendGroupText(format(M2Share.g_sJoinGroup, m_sCharName));
        }

        
        
        
        
        private ushort MakeMineRandomDrua()
        {
            var result = M2Share.RandomNumber.Random(M2Share.g_Config.nStoneGeneralDuraRate) + M2Share.g_Config.nStoneMinDura;
            if (M2Share.RandomNumber.Random(M2Share.g_Config.nStoneAddDuraRate) == 0)
            {
                result = result + M2Share.RandomNumber.Random(M2Share.g_Config.nStoneAddDuraMax);
            }
            return (ushort)result;
        }

        
        
        
        private void MakeMine()
        {
            TUserItem UserItem;
            if (m_ItemList.Count >= BagCapacity.Of(this))
            {
                return;
            }
            // MINE-24/25: 原版 sub_6BC3CC 是「减权重 + 借位跳转」的阶梯，不是区间比较：
            //   0x6BC3F7  B8 78 00 00 00   mov eax, 0x78   ; Random(120) -> 0..119
            //   0x6BC401  83 E8 0C         sub eax, 12
            //   0x6BC404  72 11            jb  0x6BC417    ; 金矿   权重 12  (10.0%)
            //   0x6BC406  83 E8 12         sub eax, 18
            //   0x6BC409  72 1B            jb  0x6BC426    ; 银矿   权重 18  (15.0%)
            //   0x6BC40B  83 E8 0F         sub eax, 15
            //   0x6BC40E  72 25            jb  0x6BC435    ; 铁矿   权重 15  (12.5%)
            //   0x6BC410  83 E8 3C         sub eax, 60
            //   0x6BC413  72 2F            jb  0x6BC444    ; 黑铁矿石 权重 60 (50.0%)
            //   0x6BC415  EB 3C            jmp 0x6BC453    ; 铜矿   余数 15  (12.5%)
            // 各分支目标字符串（Delphi 常量，长度前缀已校验）：
            //   0x6BC4C4 len=4 BD F0 BF F3          = 金矿
            //   0x6BC4D4 len=4 D2 F8 BF F3          = 银矿
            //   0x6BC4E4 len=4 CC FA BF F3          = 铁矿
            //   0x6BC4F4 len=8 BA DA CC FA BF F3 CA AF = 黑铁矿石
            //   0x6BC508 len=4 CD AD BF F3          = 铜矿
            var nRandom = M2Share.RandomNumber.Random(120);
            nRandom -= 12;
            if (nRandom < 0)
            {
                UserItem = new TUserItem();
                if (M2Share.UserEngine.CopyToUserItemFromName(M2Share.g_Config.sGoldStone, ref UserItem))
                {
                    UserItem.Dura = MakeMineRandomDrua();
                    m_ItemList.Add(UserItem);
                    WeightChanged();
                    SendAddItem(UserItem);
                }
                else
                {
                    Dispose(UserItem);
                }
                return;
            }

            nRandom -= 18; // 0x6BC406: sub eax,0x12 — silver weight 18
            if (nRandom < 0)
            {
                UserItem = new TUserItem();
                if (M2Share.UserEngine.CopyToUserItemFromName(M2Share.g_Config.sSilverStone, ref UserItem))
                {
                    UserItem.Dura = MakeMineRandomDrua();
                    m_ItemList.Add(UserItem);
                    WeightChanged();
                    SendAddItem(UserItem);
                }
                else
                {
                    Dispose(UserItem);
                }
                return;
            }

            nRandom -= 15; // 0x6BC40B: sub eax,0x0F — steel weight 15
            if (nRandom < 0)
            {
                UserItem = new TUserItem();
                if (M2Share.UserEngine.CopyToUserItemFromName(M2Share.g_Config.sSteelStone, ref UserItem))
                {
                    UserItem.Dura = MakeMineRandomDrua();
                    m_ItemList.Add(UserItem);
                    WeightChanged();
                    SendAddItem(UserItem);
                }
                else
                {
                    Dispose(UserItem);
                }
                return;
            }

            nRandom -= 60; // 0x6BC410: sub eax,0x3C — black weight 60
            if (nRandom < 0)
            {
                UserItem = new TUserItem();
                if (M2Share.UserEngine.CopyToUserItemFromName(M2Share.g_Config.sBlackStone, ref UserItem))
                {
                    UserItem.Dura = MakeMineRandomDrua();
                    m_ItemList.Add(UserItem);
                    WeightChanged();
                    SendAddItem(UserItem);
                }
                else
                {
                    Dispose(UserItem);
                }
                return;
            }

            // 0x6BC415: jmp → copper (weight 15 = 12.5%)
            UserItem = new TUserItem();
            if (M2Share.UserEngine.CopyToUserItemFromName(M2Share.g_Config.sCopperStone, ref UserItem))
            {
                UserItem.Dura = MakeMineRandomDrua();
                m_ItemList.Add(UserItem);
                WeightChanged();
                SendAddItem(UserItem);
            }
            else
            {
                Dispose(UserItem);
            }
        }

        // MINE-01: MakeMine2()（金刚石矿/绿宝石矿/红宝石矿/白宝石矿 四选一的第二
        // 条宝石产线）已移除。原版没有 MINE2 旗标，也没有第二条产线：挖矿产出的
        // 唯一入口是 0x6BC3CC，五个分支全是 金矿/银矿/铁矿/黑铁矿石/铜矿。

        public TUserItem QuestCheckItem(string sItemName, ref int nCount, ref int nParam, ref int nDura)
        {
            TUserItem UserItem;
            string s1C;
            TUserItem result = null;
            nParam = 0;
            nDura = 0;
            nCount = 0;
            for (var i = 0; i < m_ItemList.Count; i++)
            {
                UserItem = m_ItemList[i];
                s1C = M2Share.UserEngine.GetStdItemName(UserItem.wIndex);
                if (string.Compare(s1C, sItemName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (UserItem.Dura > nDura)
                    {
                        nDura = UserItem.Dura;
                        result = UserItem;
                    }
                    nParam += UserItem.Dura;
                    if (result == null)
                    {
                        result = UserItem;
                    }
                    nCount++;
                }
            }
            return result;
        }

        public bool QuestTakeCheckItem(TUserItem CheckItem)
        {
            TUserItem UserItem;
            var result = false;
            for (var i = 0; i < m_ItemList.Count; i++)
            {
                UserItem = m_ItemList[i];
                if (UserItem == CheckItem)
                {
                    SendDelItems(UserItem);
                    Dispose(UserItem);
                    m_ItemList.RemoveAt(i);
                    result = true;
                    break;
                }
            }
            for (var i = m_UseItems.GetLowerBound(0); i <= m_UseItems.GetUpperBound(0); i++)
            {
                if (m_UseItems[i] == CheckItem)
                {
                    SendDelItems(m_UseItems[i]);
                    m_UseItems[i].wIndex = 0;
                    result = true;
                    break;
                }
            }
            return result;
        }

        public void MakeSaveRcd(ref THumDataInfo HumanRcd)
        {
            HumanRcd.Header ??= new TRecordHeader();
            HumanRcd.Header.dCreateDate = m_dCreateDate;
            var HumData = HumanRcd.Data;
            HumData.sCharName = m_sCharName;
            HumData.sCurMap = m_sMapName;
            HumData.wCurX = m_nCurrX;
            HumData.wCurY = m_nCurrY;
            HumData.btDir = m_btDirection;
            HumData.btHair = m_btHair;
            HumData.btSex = (byte)m_btGender;
            HumData.btJob = m_btJob;
            HumData.nGold = m_nGold;
            HumData.Abil.Level = m_Abil.Level;
            HumData.Abil.HP = m_Abil.HP;
            HumData.Abil.MP = m_Abil.MP;
            HumData.Abil.MaxHP = m_Abil.MaxHP;
            HumData.Abil.MaxMP = m_Abil.MaxMP;
            HumData.Abil.Exp = m_Abil.Exp;
            HumData.Abil.MaxExp = m_Abil.MaxExp;
            HumData.Abil.Weight = m_Abil.Weight;
            HumData.Abil.MaxWeight = m_Abil.MaxWeight;
            HumData.Abil.WearWeight = m_Abil.WearWeight;
            HumData.Abil.MaxWearWeight = m_Abil.MaxWearWeight;
            HumData.Abil.HandWeight = m_Abil.HandWeight;
            HumData.Abil.MaxHandWeight = m_Abil.MaxHandWeight;
            HumData.Abil.HP = m_WAbil.HP;
            HumData.Abil.MP = m_WAbil.MP;
            // Save path of the legacy-slot trio. ToArray projects the live node
            // list down to the 12 legacy seconds the record has always carried,
            // so the on-disk layout is unchanged (12 x ushort, slot i = native
            // state 31 - i). Slot 11 is zeroed to match the load path in
            // UsrEngn.LoadPlayObject.
            HumData.wStatusTimeArr = m_wStatusTimeArr.ToArray();
            HumData.wStatusTimeArr[Grobal2.STATE_BUBBLEDEFENCEUP] = 0;
            HumData.sHomeMap = m_sHomeMap;
            HumData.wHomeX = m_nHomeX;
            HumData.wHomeY = m_nHomeY;
            HumData.nPKPoint = m_nPkPoint;
            HumData.BonusAbil = m_BonusAbil;
            HumData.nBonusPoint = m_nBonusPoint;
            HumData.sStoragePwd = m_sStoragePwd;
            HumData.StorageSpaceCount = unchecked((ushort)m_nStorageSpaceCount);
            HumData.btCreditPoint = m_btCreditPoint;
            HumData.nShengWan = m_nShengWan;
            HumData.nLingFu = m_nLingFu;
            HumData.nUsedLingFu = m_nUsedLingFu;
            HumData.nNickLinFu = m_nNickLinFu;
            HumData.ForceLv = m_nForceLv;
            HumData.ForceExp = m_nForceExp;
            HumData.FightPoints = m_nFightPoints;
            HumData.sfLevel = m_nSfLevel;
            HumData.NativeHeroIntimacy = m_dNativeHeroIntimacy;
            HumData.NativeHeroExperienceAccumulator =
                m_NativeHeroExperienceAccumulator?.Length == 24
                    ? (byte[])m_NativeHeroExperienceAccumulator.Clone()
                    : new byte[24];
            HumData.btSecHeroPracticeRewardMode = m_btSecHeroPracticeRewardMode;
            HumData.btSecHeroPracticeCostTier = m_btSecHeroPracticeCostTier;
            HumData.wSecHeroPracticeLevel = m_wSecHeroPracticeLevel;
            HumData.btGoldActNextLevel = m_btGoldActNextLevel;
            HumData.btFirstUsedGiftStage = m_btFirstUsedGiftStage;
            HumData.nActivePoint = m_nActivePoint;
            HumData.ExchangeBookPersonalRareCounters =
                (int[])_nativeExchangeBookPersonalRareCounters.Clone();
            HumData.btReLevel = m_btReLevel;
            HumData.sMasterName = m_sMasterName;
            HumData.boAllowMarry = m_boAllowMarry;
            HumData.boMarried = m_boMarried;
            HumData.boAllowMaster = m_boAllowMaster;
            HumData.boMaster = m_boMaster;
            HumData.boStudent = m_boStudent;
            HumData.btStudentOrder = m_btStudentOrder;
            HumData.btStudentCount = unchecked((byte)m_nStudentCount);
            HumData.sStudentNames = new string[5];
            for (var i = 0; i < HumData.sStudentNames.Length; i++)
                HumData.sStudentNames[i] = m_sStudentNames != null
                    && i < m_sStudentNames.Length
                    ? m_sStudentNames[i] ?? string.Empty
                    : string.Empty;
            HumData.sDearName = m_sDearName;
            HumData.nGameGold = m_nGameGold;
            HumData.nGamePoint = m_nGamePoint;
            if (m_boAllowGroup)
            {
                HumData.btAllowGroup = 1;
            }
            else
            {
                HumData.btAllowGroup = 0;
            }
            HumData.btF9 = btB2;
            HumData.btAttatckMode = m_btAttatckMode;
            HumData.btIncHealth = (byte)m_nIncHealth;
            HumData.btIncSpell = (byte)m_nIncSpell;
            HumData.btIncHealing = (byte)m_nIncHealing;
            HumData.btFightZoneDieCount = (byte)m_nFightZoneDieCount;
            HumData.sAccount = m_sUserID;
            HumData.btEE = (byte)nC4;
            HumData.boLockLogon = m_boLockLogon;
            HumData.wContribution = m_wContribution;
            HumData.btEF = btC8;
            HumData.nHungerStatus = m_nHungerStatus;
            HumData.boAllowGuildReCall = m_boAllowGuildReCall;
            HumData.wGroupRcallTime = m_wGroupRcallTime;
            HumData.dBodyLuck = m_nBodyLuckLevel; // 持久化权威小值(原生 [+0x164]↔HumData[+160]); 原 ×5000 累加器 m_dBodyLuck 已退役
            HumData.boAllowGroupReCall = m_boAllowGroupReCall;
            HumData.QuestUnitOpen = m_QuestUnitOpen == null ? Array.Empty<byte>() : (byte[])m_QuestUnitOpen.Clone();
            HumData.QuestUnit = m_QuestUnit == null ? Array.Empty<byte>() : (byte[])m_QuestUnit.Clone();
            HumData.QuestFlag = m_QuestFlag == null ? Array.Empty<byte>() : (byte[])m_QuestFlag.Clone();
            HumData.IntVar = Array.Empty<int>();
            HumData.ScriptV = CopyKeyedScriptVars(m_ScriptVVars);
            HumData.ScriptS = CopyKeyedScriptVars(m_ScriptSVars);
            var HumItems = new TUserItem[Grobal2.HUMAN_EQUIPPED_ITEM_COUNT];
            HumanRcd.Data.HumItems = HumItems;
            var equippedCount = Math.Min(HumItems.Length, m_UseItems?.Length ?? 0);
            for (var i = 0; i < equippedCount; i++)
                if (m_UseItems[i] != null && m_UseItems[i].wIndex > 0)
                    HumItems[i] = new TUserItem(m_UseItems[i]);

            var BagItems = new TUserItem[BagCapacity.NativeSlots];
            HumanRcd.Data.BagItems = BagItems;
            // 0x6B171B `cmp edi,0x30 / jne 0x6B16E9`: 原生循环在写满 48 槽后停止，
            // 第 49 件起静默丢弃。背包可经 GetBackDealItems 无余量退还而超过 48。
            //
            // 这里是**记录槽位数**不是容量：装了无限背包也仍然只有 48 槽，48 格以后
            // 的物品走 bags\<角色名>.bin。绝不能改成 BagCapacity.Of —— 那会改存档
            // 记录布局（REPLICATION_RULES §1.4）。越界那部分由 SaveHumanRcd 的
            // BagCapacity.PersistableOf 闸门在到达这里之前拦下。
            for (var i = 0; i < m_ItemList.Count && i < BagCapacity.NativeSlots; i++)
                if (m_ItemList[i] != null && m_ItemList[i].wIndex > 0)
                    BagItems[i] = new TUserItem(m_ItemList[i]);

            var HumMagic = new TMagicRcd[Grobal2.MAXMAGIC];
            HumanRcd.Data.Magic = HumMagic;
            for (var i = 0; i < m_MagicList.Count; i++)
            {
                var UserMagic = m_MagicList[i];
                HumMagic[i] = new TMagicRcd
                {
                    wMagIdx = UserMagic.wMagIdx,
                    btLevel = UserMagic.btLevel,
                    btKey = UserMagic.btKey,
                    nTranPoint = UserMagic.nTranPoint,
                    NativeRecord = UserMagic.NativeRecord == null
                        ? null
                        : (byte[])UserMagic.NativeRecord.Clone()
                };
            }

            var StorageItems = new TUserItem[MAX_STORAGE_ITEM_COUNT];
            HumanRcd.Data.StorageItems = StorageItems;
            for (var i = 0; i < m_StorageItemList.Count; i++)
                if (m_StorageItemList[i] != null && m_StorageItemList[i].wIndex > 0)
                    StorageItems[i] = new TUserItem(m_StorageItemList[i]);
        }

        /// <summary>
        /// Keyed V/S banks only. group*1000+index is >= 1001 whenever both
        /// arguments are positive (sub_6E42CC). Keys below that are group 0,
        /// which native keeps in the inline table and never writes to the
        /// type0/type1 sections (encoder 0x6E4DE7 / 0x6E4E19).
        /// </summary>
        private static Dictionary<int, int> CopyKeyedScriptVars(Dictionary<int, int> source)
        {
            var copy = new Dictionary<int, int>();
            if (source == null) return copy;
            foreach (var pair in source)
            {
                if (pair.Key < 1001) continue;
                copy[pair.Key] = pair.Value;
            }
            return copy;
        }

        public void RefRankInfo(int nRankNo, string sRankName)
        {
            m_nGuildRankNo = nRankNo;
            m_sGuildRankName = sRankName;
            SendMsg(this, Grobal2.RM_CHANGEGUILDNAME, 0, 0, 0, 0, "");
        }

        private void GetOldAbil(ref TOAbility OAbility)
        {
            OAbility = new TOAbility();
            OAbility.Level = m_WAbil.Level;
            OAbility.AC = HUtil32.MakeWord(HUtil32._MIN(byte.MaxValue, HUtil32.LoWord(m_WAbil.AC)), HUtil32._MIN(byte.MaxValue, HUtil32.HiWord(m_WAbil.AC)));
            OAbility.MAC = HUtil32.MakeWord(HUtil32._MIN(byte.MaxValue, HUtil32.LoWord(m_WAbil.MAC)), HUtil32._MIN(byte.MaxValue, HUtil32.HiWord(m_WAbil.MAC)));
            OAbility.DC = HUtil32.MakeWord(HUtil32._MIN(byte.MaxValue, HUtil32.LoWord(m_WAbil.DC)), HUtil32._MIN(byte.MaxValue, HUtil32.HiWord(m_WAbil.DC)));
            OAbility.MC = HUtil32.MakeWord(HUtil32._MIN(byte.MaxValue, HUtil32.LoWord(m_WAbil.MC)), HUtil32._MIN(byte.MaxValue, HUtil32.HiWord(m_WAbil.MC)));
            OAbility.SC = HUtil32.MakeWord(HUtil32._MIN(byte.MaxValue, HUtil32.LoWord(m_WAbil.SC)), HUtil32._MIN(byte.MaxValue, HUtil32.HiWord(m_WAbil.SC)));
            OAbility.HP = m_WAbil.HP;
            OAbility.MP = m_WAbil.MP;
            OAbility.MaxHP = m_WAbil.MaxHP;
            OAbility.MaxMP = m_WAbil.MaxMP;
            OAbility.Exp = m_WAbil.Exp;
            OAbility.MaxExp = m_WAbil.MaxExp;
            OAbility.Weight = m_WAbil.Weight;
            OAbility.MaxWeight = m_WAbil.MaxWeight;
            OAbility.WearWeight = (byte)HUtil32._MIN(byte.MaxValue, m_WAbil.WearWeight);
            OAbility.MaxWearWeight = (byte)HUtil32._MIN(byte.MaxValue, m_WAbil.MaxWearWeight);
            OAbility.HandWeight = (byte)HUtil32._MIN(byte.MaxValue, m_WAbil.HandWeight);
            OAbility.MaxHandWeight = (byte)HUtil32._MIN(byte.MaxValue, m_WAbil.MaxHandWeight);
        }

        
        
        
        
        private int GetHitMsgCount()
        {
            SendMessage SendMessage;
            var result = 0;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                for (var i = 0; i < m_MsgList.Count; i++)
                {
                    SendMessage = m_MsgList[i];
                    if (SendMessage.wIdent == Grobal2.CM_HIT || SendMessage.wIdent == Grobal2.CM_HEAVYHIT || SendMessage.wIdent == Grobal2.CM_BIGHIT || SendMessage.wIdent == Grobal2.CM_POWERHIT
                        || SendMessage.wIdent == Grobal2.CM_LONGHIT || SendMessage.wIdent == Grobal2.CM_WIDEHIT || SendMessage.wIdent == Grobal2.CM_FIREHIT)
                    {
                        result++;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            return result;
        }

        
        
        
        
        private int GetSpellMsgCount()
        {
            SendMessage SendMessage;
            var result = 0;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                for (var i = 0; i < m_MsgList.Count; i++)
                {
                    SendMessage = m_MsgList[i];
                    if (SendMessage.wIdent == Grobal2.CM_SPELL)
                    {
                        result++;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            return result;
        }

        
        
        
        
        private int GetRunMsgCount()
        {
            SendMessage SendMessage;
            var result = 0;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                for (var i = 0; i < m_MsgList.Count; i++)
                {
                    SendMessage = m_MsgList[i];
                    if (SendMessage.wIdent == Grobal2.CM_RUN)
                    {
                        result++;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            return result;
        }

        
        
        
        
        private int GetWalkMsgCount()
        {
            SendMessage SendMessage;
            var result = 0;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                for (var i = 0; i < m_MsgList.Count; i++)
                {
                    SendMessage = m_MsgList[i];
                    if (SendMessage.wIdent == Grobal2.CM_WALK)
                    {
                        result++;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            return result;
        }

        private int GetTurnMsgCount()
        {
            SendMessage SendMessage;
            var result = 0;
            try
            {
                HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
                for (var i = 0; i < m_MsgList.Count; i++)
                {
                    SendMessage = m_MsgList[i];
                    if (SendMessage.wIdent == Grobal2.CM_TURN)
                    {
                        result++;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            return result;
        }

        private int GetSiteDownMsgCount()
        {
            var result = 0;
            SendMessage SendMessage;
            HUtil32.EnterCriticalSection(M2Share.ProcessMsgCriticalSection);
            try
            {
                for (var i = 0; i < m_MsgList.Count; i++)
                {
                    SendMessage = m_MsgList[i];
                    if (SendMessage.wIdent == Grobal2.CM_SITDOWN)
                    {
                        result++;
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessMsgCriticalSection);
            }
            return result;
        }

        private bool CheckActionStatus(int wIdent, ref int dwDelayTime)
        {
            var result = false;
            dwDelayTime = 0;
            if (M2Share.g_Config.boSpeedHackCheck)
            {
                return true;
            }
            int dwCheckTime;
            if (!M2Share.g_Config.boDisableStruck) 
            {
                dwCheckTime = HUtil32.GetTickCount() - m_dwStruckTick;
                if (M2Share.g_Config.dwStruckTime > dwCheckTime)
                {
                    dwDelayTime = M2Share.g_Config.dwStruckTime - dwCheckTime;
                    m_btOldDir = m_btDirection;
                    return false;
                }
            }
            
            dwCheckTime = HUtil32.GetTickCount() - m_dwActionTick;
            if (m_boTestSpeedMode)
            {
                SysMsg("间隔: " + dwCheckTime, MsgColor.Blue, MsgType.Notice);
            }
            if (m_wOldIdent == wIdent)
            {
                
                return true;
            }
            if (!M2Share.g_Config.boControlActionInterval)
            {
                return true;
            }
            int dwActionIntervalTime = m_dwActionIntervalTime;
            switch (wIdent)
            {
                case Grobal2.CM_LONGHIT:
                    if (M2Share.g_Config.boControlRunLongHit && m_wOldIdent == Grobal2.CM_RUN && m_btOldDir != m_btDirection)
                    {
                        dwActionIntervalTime = m_dwRunLongHitIntervalTime;// 跑位刺杀
                    }
                    break;
                case Grobal2.CM_HIT:
                    if (M2Share.g_Config.boControlWalkHit && m_wOldIdent == Grobal2.CM_WALK && m_btOldDir != m_btDirection)
                    {
                        dwActionIntervalTime = m_dwWalkHitIntervalTime; 
                    }
                    if (M2Share.g_Config.boControlRunHit && m_wOldIdent == Grobal2.CM_RUN && m_btOldDir != m_btDirection)
                    {
                        dwActionIntervalTime = m_dwRunHitIntervalTime;// 跑位攻击
                    }
                    break;
                case Grobal2.CM_RUN:
                    if (M2Share.g_Config.boControlRunLongHit && m_wOldIdent == Grobal2.CM_LONGHIT && m_btOldDir != m_btDirection)
                    {
                        dwActionIntervalTime = m_dwRunLongHitIntervalTime;// 跑位刺杀
                    }
                    if (M2Share.g_Config.boControlRunHit && m_wOldIdent == Grobal2.CM_HIT && m_btOldDir != m_btDirection)
                    {
                        dwActionIntervalTime = m_dwRunHitIntervalTime;// 跑位攻击
                    }
                    if (M2Share.g_Config.boControlRunMagic && m_wOldIdent == Grobal2.CM_SPELL && m_btOldDir != m_btDirection)
                    {
                        dwActionIntervalTime = m_dwRunMagicIntervalTime;// 跑位魔法
                    }
                    break;
                case Grobal2.CM_WALK:
                    if (M2Share.g_Config.boControlWalkHit && m_wOldIdent == Grobal2.CM_HIT && m_btOldDir != m_btDirection)
                    {
                        dwActionIntervalTime = m_dwWalkHitIntervalTime;// 走位攻击
                    }
                    if (M2Share.g_Config.boControlRunLongHit && m_wOldIdent == Grobal2.CM_LONGHIT && m_btOldDir != m_btDirection)
                    {
                        dwActionIntervalTime = m_dwRunLongHitIntervalTime;// 跑位刺杀
                    }
                    break;
                case Grobal2.CM_SPELL:
                    if (M2Share.g_Config.boControlRunMagic && m_wOldIdent == Grobal2.CM_RUN && m_btOldDir != m_btDirection)
                    {
                        dwActionIntervalTime = m_dwRunMagicIntervalTime;// 跑位魔法
                    }
                    break;
            }
            
            if (wIdent == Grobal2.CM_HIT || wIdent == Grobal2.CM_HEAVYHIT || wIdent == Grobal2.CM_BIGHIT || wIdent == Grobal2.CM_POWERHIT || wIdent == Grobal2.CM_WIDEHIT || wIdent == Grobal2.CM_FIREHIT)
            {
                wIdent = Grobal2.CM_HIT;
            }
            if (dwCheckTime >= dwActionIntervalTime)
            {
                m_dwActionTick = HUtil32.GetTickCount();
                result = true;
            }
            else
            {
                dwDelayTime = dwActionIntervalTime - dwCheckTime;
            }
            m_wOldIdent = wIdent;
            m_btOldDir = m_btDirection;
            return result;
        }

        public void SetScriptLabel(string sLabel)
        {
            m_CanJmpScriptLableList.Clear();
            m_CanJmpScriptLableList.Add(sLabel, sLabel);
        }

        
        
        
        
        public void GetScriptLabel(string sMsg)
        {
            var sText = string.Empty;
            m_CanJmpScriptLableList.Clear();
            const string start = "<";
            const string end = ">";
            var sCmdStr = string.Empty;
            while (true)
            {
                if (string.IsNullOrEmpty(sMsg))
                {
                    break;
                }
                sMsg = HUtil32.GetValidStr3(sMsg, ref sText, "\\");
                if (!string.IsNullOrEmpty(sText))
                {
                    var rg = new Regex("(?<=(" + start + "))[.\\s\\S]*?(?=(" + end + "))", RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
                    var match = rg.Matches(sText);
                    if (match.Count > 0)
                    {
                        foreach (Match item in match)
                        {
                            sCmdStr = item.Value;
                            var sLabel = HUtil32.GetValidStr3(sCmdStr, ref sCmdStr, HUtil32.Backslash);
                            if (!string.IsNullOrEmpty(sLabel) && !m_CanJmpScriptLableList.ContainsKey(sLabel))
                            {
                                m_CanJmpScriptLableList.Add(sLabel, sLabel);
                            }
                        }
                    }
                }
            }
        }

        public bool LableIsCanJmp(string sLabel)
        {
            if (string.Compare(sLabel, "@main", StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
            if (m_CanJmpScriptLableList.ContainsKey(sLabel))
            {
                return true;
            }
            if (string.Compare(sLabel, m_sPlayDiceLabel, StringComparison.OrdinalIgnoreCase) == 0)
            {
                m_sPlayDiceLabel = string.Empty;
                return true;
            }
            return false;
        }

        private bool CheckItemsNeed(GoodItem StdItem)
        {
            var result = true;
            var castle = M2Share.CastleManager.IsCastleMember(this);
            switch (StdItem.Need)
            {
                case 6:
                    if (m_MyGuild == null)
                    {
                        result = false;
                    }
                    break;
                case 60:
                    if (m_MyGuild == null || m_nGuildRankNo != 1)
                    {
                        result = false;
                    }
                    break;
                case 7:
                    if (castle == null)
                    {
                        result = false;
                    }
                    break;
                case 70:
                    if (castle == null || m_nGuildRankNo != 1)
                    {
                        result = false;
                    }
                    break;
                case 8:
                    if (m_nMemberType == 0)
                    {
                        result = false;
                    }
                    break;
                case 81:
                    if (m_nMemberType != HUtil32.LoWord(StdItem.NeedLevel) || m_nMemberLevel < HUtil32.HiWord(StdItem.NeedLevel))
                    {
                        result = false;
                    }
                    break;
                case 82:
                    if (m_nMemberType < HUtil32.LoWord(StdItem.NeedLevel) || m_nMemberLevel < HUtil32.HiWord(StdItem.NeedLevel))
                    {
                        result = false;
                    }
                    break;
            }
            return result;
        }

        private void CheckMarry()
        {
            StringList LoadList;
            string sSayMsg;
            var boIsfound = false;
            var sUnMarryFileName = M2Share.sConfigPath + M2Share.g_Config.sEnvirDir + "UnMarry.txt";
            if (File.Exists(sUnMarryFileName))
            {
                LoadList = new StringList();
                LoadList.LoadFromFile(sUnMarryFileName);
                for (var i = 0; i < LoadList.Count; i++)
                {
                    if (LoadList[i].CompareTo(this.m_sCharName) == 0)
                    {
                        LoadList.RemoveAt(i);
                        boIsfound = true;
                        break;
                    }
                }
                LoadList.SaveToFile(sUnMarryFileName);
                LoadList.Dispose();
                LoadList = null;
            }
            if (boIsfound)
            {
                if (m_btGender == PlayGender.Man)
                {
                    sSayMsg = string.Format(M2Share.g_sfUnMarryManLoginMsg, m_sDearName, m_sDearName);
                }
                else
                {
                    sSayMsg = string.Format(M2Share.g_sfUnMarryWoManLoginMsg, m_sCharName, m_sCharName);
                }
                SysMsg(sSayMsg, MsgColor.Red, MsgType.Hint);
                m_sDearName = "";
                m_boMarried = false;
                m_DearHuman = null;
                RefShowName();
            }
            m_DearHuman = M2Share.UserEngine.GetPlayObject(m_sDearName);
            if (m_DearHuman != null)
            {
                m_DearHuman.m_DearHuman = this;
                if (m_btGender == PlayGender.Man)
                {
                    sSayMsg = string.Format(M2Share.g_sManLoginDearOnlineSelfMsg, m_sDearName, m_sCharName, m_DearHuman.m_PEnvir.sMapDesc, m_DearHuman.m_nCurrX, m_DearHuman.m_nCurrY);
                    SysMsg(sSayMsg, MsgColor.Blue, MsgType.Hint);
                    sSayMsg = string.Format(M2Share.g_sManLoginDearOnlineDearMsg, m_sDearName, m_sCharName, m_PEnvir.sMapDesc, m_nCurrX, m_nCurrY);
                    m_DearHuman.SysMsg(sSayMsg, MsgColor.Blue, MsgType.Hint);
                }
                else
                {
                    sSayMsg = string.Format(M2Share.g_sWoManLoginDearOnlineSelfMsg, m_sDearName, m_sCharName, m_DearHuman.m_PEnvir.sMapDesc, m_DearHuman.m_nCurrX, m_DearHuman.m_nCurrY);
                    SysMsg(sSayMsg, MsgColor.Blue, MsgType.Hint);
                    sSayMsg = string.Format(M2Share.g_sWoManLoginDearOnlineDearMsg, m_sDearName, m_sCharName, m_PEnvir.sMapDesc, m_nCurrX, m_nCurrY);
                    m_DearHuman.SysMsg(sSayMsg, MsgColor.Blue, MsgType.Hint);
                }
            }
            else
            {
                if (m_btGender == PlayGender.Man)
                {
                    SysMsg(M2Share.g_sManLoginDearNotOnlineMsg, MsgColor.Red, MsgType.Hint);
                }
                else
                {
                    SysMsg(M2Share.g_sWoManLoginDearNotOnlineMsg, MsgColor.Red, MsgType.Hint);
                }
            }
        }

        private void CheckMaster()
        {
            bool boIsfound = false;
            string sSayMsg;
            TPlayObject Human;
            for (var i = 0; i < M2Share.g_UnForceMasterList.Count; i++) 
            {
                if (String.Compare(M2Share.g_UnForceMasterList[i], this.m_sCharName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    M2Share.g_UnForceMasterList.RemoveAt(i);
                    M2Share.SaveUnForceMasterList();
                    boIsfound = true;
                    break;
                }
            }
            if (boIsfound)
            {
                if (m_boMaster)
                {
                    sSayMsg = string.Format(M2Share.g_sfUnMasterLoginMsg, m_sMasterName);
                }
                else
                {
                    sSayMsg = string.Format(M2Share.g_sfUnMasterListLoginMsg, m_sMasterName);
                }
                SysMsg(sSayMsg, MsgColor.Red, MsgType.Hint);
                m_sMasterName = "";
                RefShowName();
            }
            if (!string.IsNullOrEmpty(m_sMasterName) && !m_boMaster)
            {
                if (m_Abil.Level >= M2Share.g_Config.nMasterOKLevel)
                {
                    Human = M2Share.UserEngine.GetPlayObject(m_sMasterName);
                    if (Human != null && !Human.m_boDeath && !Human.m_boGhost)
                    {
                        sSayMsg = string.Format(M2Share.g_sYourMasterListUnMasterOKMsg, m_sCharName);
                        Human.SysMsg(sSayMsg, MsgColor.Red, MsgType.Hint);
                        SysMsg(M2Share.g_sYouAreUnMasterOKMsg, MsgColor.Red, MsgType.Hint);
                        if (m_sCharName == Human.m_sMasterName)// 如果大徒弟则将师父上的名字去掉
                        {
                            Human.m_sMasterName = "";
                            Human.RefShowName();
                        }
                        for (var i = 0; i < Human.m_MasterList.Count; i++)
                        {
                            if (Human.m_MasterList[i] == this)
                            {
                                Human.m_MasterList.RemoveAt(i);
                                break;
                            }
                        }
                        m_sMasterName = "";
                        RefShowName();
                        if (Human.m_btCreditPoint + M2Share.g_Config.nMasterOKCreditPoint <= byte.MaxValue)
                        {
                            Human.m_btCreditPoint += (byte)M2Share.g_Config.nMasterOKCreditPoint;
                        }
                        Human.m_nBonusPoint += M2Share.g_Config.nMasterOKBonusPoint;
                        Human.SendMsg(Human, Grobal2.RM_ADJUST_BONUS, 0, 0, 0, 0, "");
                    }
                    else
                    {
                        
                        boIsfound = false;
                        for (var i = 0; i < M2Share.g_UnMasterList.Count; i++)
                        {
                            if (String.Compare(M2Share.g_UnMasterList[i], this.m_sCharName, StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                boIsfound = true;
                                break;
                            }
                        }
                        if (!boIsfound)
                        {
                            M2Share.g_UnMasterList.Add(m_sMasterName);
                        }
                        if (!boIsfound)
                        {
                            M2Share.SaveUnMasterList();
                        }
                        SysMsg(M2Share.g_sYouAreUnMasterOKMsg, MsgColor.Red, MsgType.Hint);
                        m_sMasterName = "";
                        RefShowName();
                    }
                }
            }
            
            boIsfound = false;
            for (var i = 0; i < M2Share.g_UnMasterList.Count; i++)
            {
                if (string.Compare(M2Share.g_UnMasterList[i], this.m_sCharName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    M2Share.g_UnMasterList.RemoveAt(i);
                    M2Share.SaveUnMasterList();
                    boIsfound = true;
                    break;
                }
            }
            if (boIsfound && m_boMaster)
            {
                SysMsg(M2Share.g_sUnMasterLoginMsg, MsgColor.Red, MsgType.Hint);
                m_sMasterName = "";
                RefShowName();
                if (m_btCreditPoint + M2Share.g_Config.nMasterOKCreditPoint <= byte.MaxValue)
                {
                    m_btCreditPoint += (byte)M2Share.g_Config.nMasterOKCreditPoint;
                }
                m_nBonusPoint += M2Share.g_Config.nMasterOKBonusPoint;
                SendMsg(this, Grobal2.RM_ADJUST_BONUS, 0, 0, 0, 0, "");
            }
            if (string.IsNullOrEmpty(m_sMasterName))
            {
                return;
            }
            if (m_boMaster) 
            {
                m_MasterHuman = M2Share.UserEngine.GetPlayObject(m_sMasterName);
                if (m_MasterHuman != null)
                {
                    m_MasterHuman.m_MasterHuman = this;
                    m_MasterList.Add(m_MasterHuman);
                    sSayMsg = string.Format(M2Share.g_sMasterOnlineSelfMsg, m_sMasterName, m_sCharName, m_MasterHuman.m_PEnvir.sMapDesc, m_MasterHuman.m_nCurrX, m_MasterHuman.m_nCurrY);
                    SysMsg(sSayMsg, MsgColor.Blue, MsgType.Hint);
                    sSayMsg = string.Format(M2Share.g_sMasterOnlineMasterListMsg, m_sMasterName, m_sCharName, m_PEnvir.sMapDesc, m_nCurrX, m_nCurrY);
                    m_MasterHuman.SysMsg(sSayMsg, MsgColor.Blue, MsgType.Hint);
                }
                else
                {
                    SysMsg(M2Share.g_sMasterNotOnlineMsg, MsgColor.Red, MsgType.Hint);
                }
            }
            else
            {
                
                if (!string.IsNullOrEmpty(m_sMasterName))
                {
                    m_MasterHuman = M2Share.UserEngine.GetPlayObject(m_sMasterName);
                    if (m_MasterHuman != null)
                    {
                        if (m_MasterHuman.m_sMasterName == m_sCharName)
                        {
                            m_MasterHuman.m_MasterHuman = this;
                        }
                        m_MasterHuman.m_MasterList.Add(this);
                        sSayMsg = string.Format(M2Share.g_sMasterListOnlineSelfMsg, m_sMasterName, m_sCharName, m_MasterHuman.m_PEnvir.sMapDesc, m_MasterHuman.m_nCurrX, m_MasterHuman.m_nCurrY);
                        SysMsg(sSayMsg, MsgColor.Blue, MsgType.Hint);
                        sSayMsg = string.Format(M2Share.g_sMasterListOnlineMasterMsg, m_sMasterName, m_sCharName, m_PEnvir.sMapDesc, m_nCurrX, m_nCurrY);
                        m_MasterHuman.SysMsg(sSayMsg, MsgColor.Blue, MsgType.Hint);
                    }
                    else
                    {
                        SysMsg(M2Share.g_sMasterListNotOnlineMsg, MsgColor.Red, MsgType.Hint);
                    }
                }
            }
        }

        public string GetMyInfo()
        {
            var sMyInfo = M2Share.g_sMyInfo;
            sMyInfo = sMyInfo.Replace("%name", m_sCharName);
            sMyInfo = sMyInfo.Replace("%map", m_PEnvir.sMapDesc);
            sMyInfo = sMyInfo.Replace("%x", m_nCurrX.ToString());
            sMyInfo = sMyInfo.Replace("%y", m_nCurrY.ToString());
            sMyInfo = sMyInfo.Replace("%level", m_Abil.Level.ToString());
            sMyInfo = sMyInfo.Replace("%gold", m_nGold.ToString());
            sMyInfo = sMyInfo.Replace("%pk", m_nPkPoint.ToString());
            sMyInfo = sMyInfo.Replace("%minhp", m_WAbil.HP.ToString());
            sMyInfo = sMyInfo.Replace("%maxhp", m_WAbil.MaxHP.ToString());
            sMyInfo = sMyInfo.Replace("%minmp", m_WAbil.MP.ToString());
            sMyInfo = sMyInfo.Replace("%maxmp", m_WAbil.MaxMP.ToString());
            sMyInfo = sMyInfo.Replace("%mindc", HUtil32.LoWord(m_WAbil.DC).ToString());
            sMyInfo = sMyInfo.Replace("%maxdc", HUtil32.HiWord(m_WAbil.DC).ToString());
            sMyInfo = sMyInfo.Replace("%minmc", HUtil32.LoWord(m_WAbil.MC).ToString());
            sMyInfo = sMyInfo.Replace("%maxmc", HUtil32.HiWord(m_WAbil.MC).ToString());
            sMyInfo = sMyInfo.Replace("%minsc", HUtil32.LoWord(m_WAbil.SC).ToString());
            sMyInfo = sMyInfo.Replace("%maxsc", HUtil32.HiWord(m_WAbil.SC).ToString());
            sMyInfo = sMyInfo.Replace("%logontime", m_dLogonTime.ToString());
            sMyInfo = sMyInfo.Replace("%logonint", ((HUtil32.GetTickCount() - m_dwLogonTick) / 60000).ToString());
            return sMyInfo;
        }

        private bool CheckItemBindUse(TUserItem UserItem)
        {
            TItemBind ItemBind;
            bool result = true;
            for (var i = 0; i < M2Share.g_ItemBindAccount.Count; i++)
            {
                ItemBind = M2Share.g_ItemBindAccount[i];
                if (ItemBind.nMakeIdex == UserItem.MakeIndex && ItemBind.nItemIdx == UserItem.wIndex)
                {
                    result = false;
                    if (string.Compare(ItemBind.sBindName, m_sUserID, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sItemIsNotThisAccount, MsgColor.Red, MsgType.Hint);
                    }
                    return result;
                }
            }
            for (var i = 0; i < M2Share.g_ItemBindIPaddr.Count; i++)
            {
                ItemBind = M2Share.g_ItemBindIPaddr[i];
                if (ItemBind.nMakeIdex == UserItem.MakeIndex && ItemBind.nItemIdx == UserItem.wIndex)
                {
                    result = false;
                    if (string.Compare(ItemBind.sBindName, m_sIPaddr, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sItemIsNotThisIPaddr, MsgColor.Red, MsgType.Hint);
                    }
                    return result;
                }
            }
            for (var i = 0; i < M2Share.g_ItemBindCharName.Count; i++)
            {
                ItemBind = M2Share.g_ItemBindCharName[i];
                if (ItemBind.nMakeIdex == UserItem.MakeIndex && ItemBind.nItemIdx == UserItem.wIndex)
                {
                    result = false;
                    if (string.Compare(ItemBind.sBindName, m_sCharName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        result = true;
                    }
                    else
                    {
                        SysMsg(M2Share.g_sItemIsNotThisCharName, MsgColor.Red, MsgType.Hint);
                    }
                    return result;
                }
            }
            return result;
        }

        private void ProcessClientPassword(TProcessMessage ProcessMsg)
        {
            if (ProcessMsg.wParam == 0)
            {
                ProcessUserLineMsg('@' + M2Share.g_GameCommand.UNLOCK.sCmd);
                return;
            }
            string sData = ProcessMsg.sMsg;
            int nLen = sData.Length;
            if (m_boSetStoragePwd)
            {
                m_boSetStoragePwd = false;
                if (nLen > 3 && nLen < 8)
                {
                    m_sTempPwd = sData;
                    m_boReConfigPwd = true;
                    SysMsg(M2Share.g_sReSetPasswordMsg, MsgColor.Green, MsgType.Hint);// '请重复输入一次仓库密码：'
                    SendMsg(this, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
                }
                else
                {
                    SysMsg(M2Share.g_sPasswordOverLongMsg, MsgColor.Red, MsgType.Hint);// '输入的密码长度不正确!!!，密码长度必须在 4 - 7 的范围内，请重新设置密码。'
                }
                return;
            }
            if (m_boReConfigPwd)
            {
                m_boReConfigPwd = false;
                if (String.Compare(m_sTempPwd, sData, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    m_sStoragePwd = sData;
                    m_boPasswordLocked = true;
                    m_sTempPwd = "";
                    SysMsg(M2Share.g_sReSetPasswordOKMsg, MsgColor.Blue, MsgType.Hint);// '密码设置成功!!，仓库已经自动上锁，请记好您的仓库密码，在取仓库时需要使用此密码开锁。'
                }
                else
                {
                    m_sTempPwd = "";
                    SysMsg(M2Share.g_sReSetPasswordNotMatchMsg, MsgColor.Red, MsgType.Hint);
                }
                return;
            }
            if (m_boUnLockPwd || m_boUnLockStoragePwd)
            {
                if (String.Compare(m_sStoragePwd, sData, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    m_boPasswordLocked = false;
                    if (m_boUnLockPwd)
                    {
                        if (M2Share.g_Config.boLockDealAction)
                        {
                            m_boCanDeal = true;
                        }
                        if (M2Share.g_Config.boLockDropAction)
                        {
                            m_boCanDrop = true;
                        }
                        if (M2Share.g_Config.boLockWalkAction)
                        {
                            m_boCanWalk = true;
                        }
                        if (M2Share.g_Config.boLockRunAction)
                        {
                            m_boCanRun = true;
                        }
                        if (M2Share.g_Config.boLockHitAction)
                        {
                            m_boCanHit = true;
                        }
                        if (M2Share.g_Config.boLockSpellAction)
                        {
                            m_boCanSpell = true;
                        }
                        if (M2Share.g_Config.boLockSendMsgAction)
                        {
                            m_boCanSendMsg = true;
                        }
                        if (M2Share.g_Config.boLockUserItemAction)
                        {
                            m_boCanUseItem = true;
                        }
                        if (M2Share.g_Config.boLockInObModeAction)
                        {
                            m_boObMode = false;
                            m_boAdminMode = false;
                        }
                        m_boLockLogoned = true;
                        SysMsg(M2Share.g_sPasswordUnLockOKMsg, MsgColor.Blue, MsgType.Hint);
                    }
                    if (m_boUnLockStoragePwd)
                    {
                        if (M2Share.g_Config.boLockGetBackItemAction)
                        {
                            m_boCanGetBackItem = true;
                        }
                        SysMsg(M2Share.g_sStorageUnLockOKMsg, MsgColor.Blue, MsgType.Hint);
                    }
                }
                else
                {
                    m_btPwdFailCount++;
                    SysMsg(M2Share.g_sUnLockPasswordFailMsg, MsgColor.Red, MsgType.Hint);
                    if (m_btPwdFailCount > 3)
                    {
                        SysMsg(M2Share.g_sStoragePasswordLockedMsg, MsgColor.Red, MsgType.Hint);
                    }
                }
                m_boUnLockPwd = false;
                m_boUnLockStoragePwd = false;
                return;
            }
            if (m_boCheckOldPwd)
            {
                m_boCheckOldPwd = false;
                if (m_sStoragePwd == sData)
                {
                    SendMsg(this, Grobal2.RM_PASSWORD, 0, 0, 0, 0, "");
                    SysMsg(M2Share.g_sSetPasswordMsg, MsgColor.Green, MsgType.Hint);
                    m_boSetStoragePwd = true;
                }
                else
                {
                    m_btPwdFailCount++;
                    SysMsg(M2Share.g_sOldPasswordIncorrectMsg, MsgColor.Red, MsgType.Hint);
                    if (m_btPwdFailCount > 3)
                    {
                        SysMsg(M2Share.g_sStoragePasswordLockedMsg, MsgColor.Red, MsgType.Hint);
                        m_boPasswordLocked = true;
                    }
                }
            }
        }

        public void RecallHuman(string sHumName)
        {
            short nX = 0;
            short nY = 0;
            short n18 = 0;
            short n1C = 0;
            var PlayObject = M2Share.UserEngine.GetPlayObject(sHumName);
            if (PlayObject != null)
            {
                if (GetFrontPosition(ref nX, ref nY))
                {
                    if (GetRecallXY(nX, nY, 3, ref n18, ref n1C))
                    {
                        PlayObject.SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, 0, 0, 0, "");
                        PlayObject.SpaceMove(m_sMapName, n18, n1C, 0);
                    }
                }
                else
                {
                    SysMsg("召唤失败!!!", MsgColor.Red, MsgType.Hint);
                }
            }
            else
            {
                SysMsg(format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumName), MsgColor.Red, MsgType.Hint);
            }
        }

        public void ReQuestGuildWar(string sGuildName)
        {
            if (!IsGuildMaster())
            {
                SysMsg("只有行会掌门人才能申请!!!", MsgColor.Red, MsgType.Hint);
                return;
            }
            if (M2Share.nServerIndex != 0)
            {
                SysMsg("这个命令不能在本服务器上使用!!!", MsgColor.Red, MsgType.Hint);
                return;
            }
            Association Guild = M2Share.GuildManager.FindGuild(sGuildName);
            if (Guild == null)
            {
                SysMsg("行会不存在!!!", MsgColor.Red, MsgType.Hint);
                return;
            }
            bool boReQuestOK = false;
            TWarGuild WarGuild = m_MyGuild.AddWarGuild(Guild);
            if (WarGuild != null)
            {
                if (Guild.AddWarGuild(m_MyGuild) == null)
                {
                    WarGuild.dwWarTick = 0;
                }
                else
                {
                    boReQuestOK = true;
                }
            }
            if (boReQuestOK)
            {
                M2Share.UserEngine.SendServerGroupMsg(Grobal2.ISM_CS_RELOADGUILD, M2Share.nServerIndex, m_MyGuild.sGuildName);
                M2Share.UserEngine.SendServerGroupMsg(Grobal2.ISM_CS_RELOADGUILD, M2Share.nServerIndex, Guild.sGuildName);
            }
        }

        private bool CheckDenyLogon()
        {
            var result = false;
            if (M2Share.GetDenyIPAddrList(m_sIPaddr))
            {
                SysMsg(M2Share.g_sYourIPaddrDenyLogon, MsgColor.Red, MsgType.Hint);
                result = true;
            }
            else if (M2Share.GetDenyAccountList(m_sUserID))
            {
                SysMsg(M2Share.g_sYourAccountDenyLogon, MsgColor.Red, MsgType.Hint);
                result = true;
            }
            else if (M2Share.GetDenyChrNameList(m_sCharName))
            {
                SysMsg(M2Share.g_sYourCharNameDenyLogon, MsgColor.Red, MsgType.Hint);
                result = true;
            }
            if (result)
            {
                m_boEmergencyClose = true;
            }
            return result;
        }

        
        
        
        
        
        public void ChangeSnapsServer(string sIPaddr, int nPort)
        {
            this.SendMsg(this, Grobal2.RM_RECONNECTION, 0, 0, 0, 0, sIPaddr + '/' + nPort);
        }
    }
}
