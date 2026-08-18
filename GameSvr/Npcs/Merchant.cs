using SystemModule;
using SystemModule.Common;
using System.IO;

namespace GameSvr
{
    
    
    
    
    public partial class Merchant : NormNpc
    {
        public string m_sScript = string.Empty;
        
        
        
        // ECON §4.18: 原生商人只有【唯一】费率字段 +0x468。SetRebate 处理器 sub_647438 写它,
        // 买价阶段 sub_640208 @0x640232/@0x640278 `fild [ebx+0x468]` 读它,构造器 @0x63D888 默认 100。
        // 原 C# 曾误拆出第二个 m_nRebate(违二元权威),现已并回本字段;PAS `setrebate` 亦改写本字段。
        public int m_nPriceRate = 0;
        public bool m_boCastle = false;
        public int dwRefillGoodsTick = 0;
        
        
        
        public IList<int> m_ItemTypeList = null;
        public IList<TGoods> m_RefillGoodsList = null;
        
        
        
        internal IList<IList<TUserItem>> m_GoodsList = null;

        private bool _nativeGoodsDirty;
        private int _nativeGoodsSaveTick;
        internal bool NativeGoodsDirty => _nativeGoodsDirty;
        internal int NativeGoodsSaveTick => _nativeGoodsSaveTick;
        
        
        
        internal IList<TItemPrice> m_ItemPriceList = null;
        
        
        
        private static readonly object UpgradeWeaponSync = new();
        public bool m_boCanMove = false;
        public int m_dwMoveTime = 0;
        public int m_dwMoveTick = 0;
        
        
        
        public bool m_boBuy = false;
        
        
        
        public bool m_boSell = false;
        public bool m_boMakeDrug = false;
        public bool m_boPrices = false;
        public bool m_boStorage = false;
        public bool m_boGetback = false;
        public bool m_boUpgradenow = false;
        public bool m_boGetBackupgnow = false;
        public bool m_boRepair = false;
        public bool m_boS_repair = false;
        public bool m_boSendmsg = false;
        public bool m_boGetMarry = false;
        public bool m_boGetMaster = false;
        public bool m_boUseItemName = false;
        public bool m_boOffLineMsg = false;
        private readonly Dictionary<int, List<ShopDetailHandle>> _shopDetailHandles = new();
        private int _nextShopDetailHandle = 1;

        private sealed class ShopDetailHandle
        {
            public int Handle;
            public TUserItem Item;
        }

        private void ClearShopDetailHandles(TPlayObject playObject)
        {
            if (playObject == null)
            {
                return;
            }
            _shopDetailHandles.Remove(playObject.ObjectId);
        }

        private int RegisterShopDetailHandle(TPlayObject playObject, TUserItem item)
        {
            if (playObject == null || item == null)
            {
                return 0;
            }
            if (!_shopDetailHandles.TryGetValue(playObject.ObjectId, out var handles))
            {
                handles = new List<ShopDetailHandle>(10);
                _shopDetailHandles[playObject.ObjectId] = handles;
            }
            var handle = _nextShopDetailHandle;
            _nextShopDetailHandle = unchecked(_nextShopDetailHandle + 1);
            if (handle <= 0)
            {
                handle = 1;
                _nextShopDetailHandle = 2;
            }
            handles.Add(new ShopDetailHandle
            {
                Handle = handle,
                Item = item
            });
            return handle;
        }

        private TUserItem ResolveShopDetailItem(TPlayObject playObject, int handle)
        {
            if (playObject == null || handle <= 0)
            {
                return null;
            }
            if (!_shopDetailHandles.TryGetValue(playObject.ObjectId, out var handles))
            {
                return null;
            }
            for (var i = 0; i < handles.Count; i++)
            {
                if (handles[i].Handle == handle)
                {
                    return handles[i].Item;
                }
            }
            return null;
        }

        private void RemoveShopDetailHandle(TPlayObject playObject, int handle)
        {
            if (playObject == null || handle <= 0)
            {
                return;
            }
            if (!_shopDetailHandles.TryGetValue(playObject.ObjectId, out var handles))
            {
                return;
            }
            for (var i = handles.Count - 1; i >= 0; i--)
            {
                if (handles[i].Handle == handle)
                {
                    handles.RemoveAt(i);
                    break;
                }
            }
            if (handles.Count == 0)
            {
                _shopDetailHandles.Remove(playObject.ObjectId);
            }
        }

        private void AddItemPrice(int nIndex, int nPrice)
        {
            TItemPrice ItemPrice;
            ItemPrice = new TItemPrice
            {
                wIndex = (short)nIndex,
                nPrice = nPrice
            };
            m_ItemPriceList.Add(ItemPrice);
        }

        private bool EnsureItemPrice(int nIndex)
        {
            for (var i = 0; i < m_ItemPriceList.Count; i++)
            {
                if (m_ItemPriceList[i].wIndex == nIndex)
                {
                    return false;
                }
            }

            var stdItem = M2Share.UserEngine.GetStdItem(nIndex);
            if (stdItem == null)
            {
                return false;
            }

            m_ItemPriceList.Add(new TItemPrice
            {
                wIndex = (short)nIndex,
                nPrice = HUtil32.Round(stdItem.Price * 1.1)
            });
            return true;
        }

        private bool EnsureGoodsPrices()
        {
            var changed = false;
            for (var i = 0; i < m_GoodsList.Count; i++)
            {
                var items = m_GoodsList[i];
                if (items.Count > 0)
                {
                    changed |= EnsureItemPrice(items[0].wIndex);
                }
            }

            for (var i = 0; i < m_RefillGoodsList.Count; i++)
            {
                var itemIndex = M2Share.UserEngine.GetStdItemIdx(m_RefillGoodsList[i].sItemName);
                if (itemIndex > 0)
                {
                    changed |= EnsureItemPrice(itemIndex);
                }
            }
            return changed;
        }

        internal static string GetNativeGoodsFilePath(string rootPath,
            string scriptName, string mapName)
        {
            return Path.Combine(rootPath ?? string.Empty, "NpcSave",
                $"{scriptName}-{mapName}.Sav");
        }

        internal static string GetNativeGoodsRootPath()
        {
            return Path.GetFullPath(Path.Combine(
                M2Share.sConfigPath ?? string.Empty,
                M2Share.g_Config?.sEnvirDir ?? string.Empty));
        }

        internal void LoadGoodRecord(string rootPath)
        {
            var fileName = GetNativeGoodsFilePath(rootPath, m_sScript, m_sMapName);
            if (!File.Exists(fileName)) return;

            var data = File.ReadAllBytes(fileName);
            m_GoodsList.Clear();
            var count = data.Length / NativeMerchantGoodsCodec.RecordSize;
            for (var i = 0; i < count; i++)
            {
                var item = NativeMerchantGoodsCodec.Decode(data.AsSpan(
                    i * NativeMerchantGoodsCodec.RecordSize,
                    NativeMerchantGoodsCodec.RecordSize));
                if (item.MakeIndex == 0 || item.wIndex == 0) continue;
                AddItemToGoodsList(item);
            }
        }

        internal void SaveGoodRecord(string rootPath)
        {
            var fileName = GetNativeGoodsFilePath(rootPath, m_sScript, m_sMapName);
            Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);

            var count = 0;
            for (var i = 0; i < m_GoodsList.Count; i++)
                count += m_GoodsList[i]?.Count ?? 0;
            if (count == 0)
            {
                File.Delete(fileName);
                return;
            }

            var data = new byte[count * NativeMerchantGoodsCodec.RecordSize];
            var offset = 0;
            for (var i = 0; i < m_GoodsList.Count; i++)
            {
                var items = m_GoodsList[i];
                if (items == null) continue;
                for (var j = 0; j < items.Count; j++)
                {
                    var record = NativeMerchantGoodsCodec.Encode(items[j]);
                    record.CopyTo(data, offset);
                    offset += record.Length;
                }
            }
            AtomicFile.WriteAllBytes(fileName, data);
        }

        public void SaveNPCData()
        {
            SaveGoodRecord(GetNativeGoodsRootPath());
        }

        internal void SaveNativeGoodsIfDue(int currentTick, string rootPath)
        {
            if (!HasNativePasProperty(9) || !_nativeGoodsDirty ||
                unchecked((uint)(currentTick - _nativeGoodsSaveTick)) < 60000)
                return;

            SaveNativeGoods(currentTick, rootPath);
        }

        internal void FlushNativeGoods(int currentTick, string rootPath)
        {
            if (!HasNativePasProperty(9)) return;
            SaveNativeGoods(currentTick, rootPath);
        }

        private void SaveNativeGoods(int currentTick, string rootPath)
        {
            _nativeGoodsDirty = false;
            try
            {
                SaveGoodRecord(rootPath);
                _nativeGoodsSaveTick = currentTick;
            }
            catch
            {
                _nativeGoodsDirty = true;
                throw;
            }
        }

        private void MarkNativeGoodsDirty()
        {
            _nativeGoodsDirty = true;
        }

        internal string StorageAllBagItems(TPlayObject sender)
        {
            // ✅ 战神字节证据 (Tier-1) — ECON-17 同族门。sub_64392C 的两道前置门是
            // 【先权限、后属性】,原先只补了后者:
            //   00643962  8b 45 f8 / e8 96 1b dc ff       mov eax,[ebp-8] / call @LStrClr  ; Result := ''
            //   0064396A  80 bf 75 06 00 00 03            cmp byte [edi+0x675],3   ; edi = PlayObject
            //   00643971  0f 86 2d 01 00 00               jbe 0x643AA4             ; <=3 直接返回空串
            //   00643977  8b 45 fc                        mov eax,[ebp-4]          ; Self = Merchant
            //   0064397A  f6 80 55 04 00 00 02            test byte [eax+0x455],2  ; property 9
            //   00643981  0f 84 1d 01 00 00               je  0x643AA4
            //   0064399E  8b 87 08 05 00 00               mov eax,[edi+0x508]      ; PlayObject.m_ItemList
            // +0x675 = m_btPermission(setter 0x6B1E80 `mov [esi+0x675],al` 紧接
            // GetHumPermission sub_65583C 的返回值)。property-9 商人的四个操作
            // ——本函数、列表查询 sub_643B20 @0x643B71、取回 sub_644244 @0x644274、
            // 寄存 sub_644488 @0x6444AC——全部以同一道 `>3` 门为前置,
            // 即这是一整套 GM 专用寄存子系统,普通玩家在原生上完全无法触发。
            if (sender == null || !sender.m_boReadyRun ||
                sender.m_btPermission <= 3 ||
                !HasNativePasProperty(9))
                return string.Empty;

            for (var i = 0; i < sender.m_ItemList.Count; i++)
            {
                var item = sender.m_ItemList[i];
                if (item == null) continue;
                if (NativeMerchantGoodsCodec.TryEncode(item, out _,
                        out var error))
                    continue;

                M2Share.ErrorMessage(
                    $"StorageAllBagItems rejected {sender.m_sCharName} item " +
                    $"{item.MakeIndex}: {error}");
                return string.Empty;
            }

            var deletedItems = new List<TDeleteItem>(sender.m_ItemList.Count);
            for (var i = sender.m_ItemList.Count - 1; i >= 0; i--)
            {
                var item = sender.m_ItemList[i];
                if (item == null) continue;

                sender.m_ItemList.RemoveAt(i);
                if (!AddItemToGoodsList(item)) continue;
                MarkNativeGoodsDirty();
                EnsureItemPrice(item.wIndex);
                var itemName = M2Share.UserEngine.GetStdItemName(item.wIndex);
                deletedItems.Add(new TDeleteItem
                {
                    sItemName = itemName,
                    MakeIndex = item.MakeIndex,
                    ClientItemID = sender.EnsureClientItemId(item)
                });
                M2Share.AddGameDataLog("10" + "\t" + sender.m_sMapName + "\t" +
                    sender.m_nCurrX + "\t" + sender.m_nCurrY + "\t" +
                    sender.m_sCharName + "\t" + itemName + "\t" + item.MakeIndex +
                    "\t" + '1' + "\t" + m_sCharName);
            }

            if (deletedItems.Count == 0) return "你的背包没有东西";

            sender.SendMsg(sender, Grobal2.RM_SENDDELITEMLIST, 0,
                deletedItems.Count, 0, 0, string.Empty, deletedItems);
            sender.WeightChanged();
            return $"一共收取了您背包里的 {deletedItems.Count} 件物品。";
        }

        private void CheckItemPrice(int nIndex)
        {
            TItemPrice ItemPrice;
            double n10;
            GoodItem StdItem;
            for (var i = 0; i < m_ItemPriceList.Count; i++)
            {
                ItemPrice = m_ItemPriceList[i];
                if (ItemPrice.wIndex == nIndex)
                {
                    n10 = ItemPrice.nPrice;
                    if (Math.Round(n10 * 1.1) > n10)
                    {
                        n10 = HUtil32.Round(n10 * 1.1);
                    }
                    else
                    {
                        n10++;
                    }
                    ItemPrice.nPrice = (int)n10;
                    m_ItemPriceList[i] = ItemPrice;
                    return;
                }
            }
            StdItem = M2Share.UserEngine.GetStdItem(nIndex);
            if (StdItem != null)
            {
                AddItemPrice(nIndex, HUtil32.Round(StdItem.Price * 1.1));
            }
        }

        private IList<TUserItem> GetRefillList(int nIndex)
        {
            IList<TUserItem> result = null;
            IList<TUserItem> List;
            if (nIndex <= 0)
            {
                return result;
            }
            for (var i = 0; i < m_GoodsList.Count; i++)
            {
                List = m_GoodsList[i];
                if (List.Count > 0)
                {
                    if (List[0].wIndex == nIndex)
                    {
                        result = List;
                        break;
                    }
                }
            }
            return result;
        }

        private void RefillGoods_RefillItems(ref IList<TUserItem> List, string sItemName, int nInt)
        {
            TUserItem UserItem;
            var changed = false;
            if (List == null)
            {
                List = new List<TUserItem>();
                m_GoodsList.Add(List);
            }
            for (var i = 0; i < nInt; i++)
            {
                UserItem = new TUserItem();
                if (M2Share.UserEngine.CopyToUserItemFromName(sItemName, ref UserItem))
                {
                    List.Insert(0, UserItem);
                    changed = true;
                }
                else
                {
                    Dispose(UserItem);
                }
            }
            if (changed) MarkNativeGoodsDirty();
        }

        private void RefillGoods_DelReFillItem(ref IList<TUserItem> List, int nInt)
        {
            var changed = false;
            for (var i = List.Count - 1; i >= 0; i--)
            {
                if (nInt <= 0)
                {
                    break;
                }
                Dispose(List[i]);
                List.RemoveAt(i);
                changed = true;
                nInt -= 1;
            }
            if (changed) MarkNativeGoodsDirty();
        }

        public bool FillGoods(string itemName, int count, int interval)
        {
            var itemIndex = M2Share.UserEngine.GetStdItemIdx(itemName);
            if (itemIndex <= 0)
            {
                return false;
            }

            count = Math.Max(0, count);
            interval = Math.Max(1, interval);
            EnsureItemPrice(itemIndex);

            var refillGoods = new TGoods
            {
                sItemName = itemName,
                nCount = count,
                dwRefillTime = interval,
                dwRefillTick = HUtil32.GetTickCount()
            };
            var refillIndex = -1;
            for (var i = 0; i < m_RefillGoodsList.Count; i++)
            {
                if (string.Equals(m_RefillGoodsList[i].sItemName, itemName, StringComparison.OrdinalIgnoreCase))
                {
                    refillIndex = i;
                    break;
                }
            }
            if (refillIndex >= 0)
            {
                m_RefillGoodsList[refillIndex] = refillGoods;
            }
            else
            {
                m_RefillGoodsList.Add(refillGoods);
            }

            var refillList = GetRefillList(itemIndex);
            if (refillList == null)
            {
                refillList = new List<TUserItem>();
                m_GoodsList.Add(refillList);
            }
            if (refillList.Count < count)
            {
                RefillGoods_RefillItems(ref refillList, itemName, count - refillList.Count);
            }
            else if (refillList.Count > count)
            {
                RefillGoods_DelReFillItem(ref refillList, refillList.Count - count);
            }

            m_boBuy = true;
            return true;
        }

        private void RefillGoods()
        {
            TGoods Goods;
            int nIndex;
            int nRefillCount;
            IList<TUserItem> RefillList;
            IList<TUserItem> RefillList20;
            bool bo21;
            const string sExceptionMsg = "[Exception] TMerchant::RefillGoods {0}/{1}:{2} [{3}] Code:{4}";
            try
            {
                for (var i = 0; i < m_RefillGoodsList.Count; i++)
                {
                    Goods = m_RefillGoodsList[i];
                    if ((HUtil32.GetTickCount() - Goods.dwRefillTick) > (Goods.dwRefillTime * 60 * 1000))
                    {
                        Goods.dwRefillTick = HUtil32.GetTickCount();
                        nIndex = M2Share.UserEngine.GetStdItemIdx(Goods.sItemName);
                        if (nIndex >= 0)
                        {
                            RefillList = GetRefillList(nIndex);
                            nRefillCount = 0;
                            if (RefillList != null)
                            {
                                nRefillCount = RefillList.Count;
                            }
                            if (Goods.nCount > nRefillCount)
                            {
                                CheckItemPrice(nIndex);
                                RefillGoods_RefillItems(ref RefillList, Goods.sItemName, Goods.nCount - nRefillCount);
                            }
                            if (Goods.nCount < nRefillCount)
                            {
                                RefillGoods_DelReFillItem(ref RefillList, nRefillCount - Goods.nCount);
                            }
                        }
                    }
                }
                for (var i = 0; i < m_GoodsList.Count; i++)
                {
                    RefillList20 = m_GoodsList[i];
                    if (RefillList20.Count > 1000)
                    {
                        bo21 = false;
                        for (var j = 0; j < m_RefillGoodsList.Count; j++)
                        {
                            Goods = m_RefillGoodsList[j];
                            nIndex = M2Share.UserEngine.GetStdItemIdx(Goods.sItemName);
                            if (RefillList20[0].wIndex == nIndex)
                            {
                                bo21 = true;
                                break;
                            }
                        }
                        if (!bo21)
                        {
                            RefillGoods_DelReFillItem(ref RefillList20, RefillList20.Count - 1000);
                        }
                        else
                        {
                            RefillGoods_DelReFillItem(ref RefillList20, RefillList20.Count - 5000);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                M2Share.MainOutMessage(format(sExceptionMsg, m_sCharName, m_nCurrX, m_nCurrY, e.Message, M2Share.nCHECK));
            }
        }

        private bool CheckItemType(int nStdMode)
        {
            var result = false;
            for (var i = 0; i < m_ItemTypeList.Count; i++)
            {
                if (m_ItemTypeList[i] == nStdMode)
                {
                    result = true;
                    break;
                }
            }
            return result;
        }

        private double GetItemPrice(int nIndex)
        {
            double result = -1;
            TItemPrice ItemPrice;
            GoodItem StdItem;
            for (var i = 0; i < m_ItemPriceList.Count; i++)
            {
                ItemPrice = m_ItemPriceList[i];
                if (ItemPrice.wIndex == nIndex)
                {
                    result = ItemPrice.nPrice;
                    break;
                }
            }
            // ✅ 战神字节证据 (Tier-1) — ECON-02: 价格表未命中(或表值 <= 0)时,回退价是
            // 【ROUND(模板Price * 1.1)】,不是裸模板 Price。EA: sub_63F3B4 @0x63F40D-0x63F43F:
            //   0063F40D  837df800        cmp   dword [ebp-8],0
            //   0063F411  7f2f            jg    0x63F442          ; 【只有 >0】才直接用表价
            //   0063F413  8b45fc          mov   eax,[ebp-4]
            //   0063F416  8b401c          mov   eax,[eax+0x1c]    ; 模板
            //   0063F41B  8a5014          mov   dl,[eax+0x14]     ; StdMode
            //   0063F41E  8bc7            mov   eax,edi
            //   0063F420  e8770e0000      call  0x64029C          ; 许可表 (= CheckItemType)
            //   0063F425  84c0            test  al,al
            //   0063F427  7419            je    0x63F442          ; 不许可 -> 保持哨兵值
            //   0063F429  8b45fc          mov   eax,[ebp-4]
            //   0063F42C  8b401c          mov   eax,[eax+0x1c]
            //   0063F42F  db403c          fild  dword [eax+0x3c]  ; 【dword】有符号读,不是 word
            //   0063F432  db2d68f46300    fld   xword [0x63F468]  ; 1.1 (10 字节 extended)
            //   0063F438  dec9            fmulp st(1)
            //   0063F43A  e83541dcff      call  0x403574          ; @ROUND (半偶入)
            //   0063F43F  8945f8          mov   [ebp-8],eax
            // 常量 @0x63F468 原始 10 字节 = cd cc cc cc cc cc cc 8c ff 3f = 1.1(exp 0x3FFF);
            // 该地址全镜像仅 1 处引用(0x63F434),即本回退分支专用。
            //
            // 【无二次放大】: 表命中分支上面已 break 且【原样返回表值】,从不再乘 1.1;本回退分支
            // 只在表值 <= 0 时进入,两条路径互斥。表内存的值本来就已经是 ROUND(Price*1.1) ——
            // 原生建表点 sub_63EA1C @0x63EAC2-0x63EAD8 逐字为
            //   mov eax,[edi+0x1c] / fild dword [eax+0x3c] / fld xword [0x63EB28] / fmulp /
            //   call 0x403574 / mov [edx+4],eax
            // (0x63EB28 亦为 1.1 extended),故 ×1.1 在原生【本就同时存在于建表点与回退点】,
            // C# 的 EnsureItemPrice(:185) 对应前者、本处对应后者,不是重复放大。
            // 【无 .Sav 迁移问题】: NpcSave/*.Sav 只序列化 TUserItem 记录(208 字节/条,见
            // NativeMerchantGoodsCodec),【不含任何价格字段】;m_ItemPriceList 纯内存,每次
            // LoadNPCData 由 EnsureGoodsPrices() 从 StdItem.Price 重建,改本处不影响存量存档。
            //
            // 回退条件用 <= 0 复刻 `jg`: 价格表里显式写 0 的条目,原生回落到模板价,
            // 旧 C# 的 `result < 0` 会保留 0,使该物品因上层 `nPrice > 0` 门既不能买也不能卖。
            if (result <= 0)
            {
                StdItem = M2Share.UserEngine.GetStdItem(nIndex);
                if (StdItem != null)
                {
                    if (CheckItemType(StdItem.StdMode))
                    {
                        result = HUtil32.Round(StdItem.Price * 1.1);
                    }
                }
            }
            return result;
        }

        private void UpgradeWaponAddValue(TPlayObject User, IList<TUserItem> ItemList,
            ref byte btDc, ref byte btSc, ref byte btMc, ref byte btCc, ref byte btDura)
        {
            TUserItem UserItem;
            GoodItem StdItem;
            TStdItem StdItem80 = null;
            IList<TDeleteItem> DelItemList = null;
            int nDc;
            int nSc;
            int nMc;
            var nDcMin = 0;
            var nDcMax = 0;
            var nScMin = 0;
            var nScMax = 0;
            var nMcMin = 0;
            var nMcMax = 0;
            var nDura = 0;
            var nItemCount = 0;
            IList<double> DuraList = new List<double>();
            for (var i = ItemList.Count - 1; i >= 0; i--)
            {
                UserItem = ItemList[i];
                if (M2Share.UserEngine.GetStdItemName(UserItem.wIndex) == M2Share.g_Config.sBlackStone)
                {
                    DuraList.Add(Math.Round(UserItem.Dura / 1.0e3));
                    if (DelItemList == null)
                    {
                        DelItemList = new List<TDeleteItem>();
                    }
                    DelItemList.Add(new TDeleteItem()
                    {
                        MakeIndex = UserItem.MakeIndex,
                        ClientItemID = User.EnsureClientItemId(UserItem),
                        sItemName = M2Share.g_Config.sBlackStone
                    });
                    DisPose(UserItem);
                    ItemList.RemoveAt(i);
                }
                else
                {
                    if (M2Share.IsAccessory(UserItem.wIndex))
                    {
                        StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                        if (StdItem != null)
                        {
                            StdItem.GetStandardItem(ref StdItem80);
                            StdItem.GetItemAddValue(UserItem, ref StdItem80);
                            nDc = 0;
                            nSc = 0;
                            nMc = 0;
                            switch (StdItem80.StdMode)
                            {
                                case 19:
                                case 20:
                                case 21:
                                    nDc = HUtil32.HiWord(StdItem80.DC) + HUtil32.LoWord(StdItem80.DC);
                                    nSc = HUtil32.HiWord(StdItem80.SC) + HUtil32.LoWord(StdItem80.SC);
                                    nMc = HUtil32.HiWord(StdItem80.MC) + HUtil32.LoWord(StdItem80.MC);
                                    break;
                                case 22:
                                case 23:
                                    nDc = HUtil32.HiWord(StdItem80.DC) + HUtil32.LoWord(StdItem80.DC);
                                    nSc = HUtil32.HiWord(StdItem80.SC) + HUtil32.LoWord(StdItem80.SC);
                                    nMc = HUtil32.HiWord(StdItem80.MC) + HUtil32.LoWord(StdItem80.MC);
                                    break;
                                case 24:
                                case 26:
                                    nDc = HUtil32.HiWord(StdItem80.DC) + HUtil32.LoWord(StdItem80.DC) + 1;
                                    nSc = HUtil32.HiWord(StdItem80.SC) + HUtil32.LoWord(StdItem80.SC) + 1;
                                    nMc = HUtil32.HiWord(StdItem80.MC) + HUtil32.LoWord(StdItem80.MC) + 1;
                                    break;
                            }
                            if (nDcMin < nDc)
                            {
                                nDcMax = nDcMin;
                                nDcMin = nDc;
                            }
                            else
                            {
                                if (nDcMax < nDc)
                                {
                                    nDcMax = nDc;
                                }
                            }
                            if (nScMin < nSc)
                            {
                                nScMax = nScMin;
                                nScMin = nSc;
                            }
                            else
                            {
                                if (nScMax < nSc)
                                {
                                    nScMax = nSc;
                                }
                            }
                            if (nMcMin < nMc)
                            {
                                nMcMax = nMcMin;
                                nMcMin = nMc;
                            }
                            else
                            {
                                if (nMcMax < nMc)
                                {
                                    nMcMax = nMc;
                                }
                            }
                            if (DelItemList == null)
                            {
                                DelItemList = new List<TDeleteItem>();
                            }
                            DelItemList.Add(new TDeleteItem()
                            {
                                sItemName = StdItem.Name,
                                MakeIndex = UserItem.MakeIndex,
                                ClientItemID = User.EnsureClientItemId(UserItem)
                            });
                            if (StdItem.NeedIdentify == 1)
                            {
                                M2Share.AddGameDataLog("26" + "\t" + User.m_sMapName + "\t" + User.m_nCurrX + "\t" + User.m_nCurrY + "\t" + User.m_sCharName + "\t" + StdItem.Name + "\t" + UserItem.MakeIndex + "\t" + '1' + "\t" + '0');
                            }
                            DisPose(UserItem);
                            ItemList.RemoveAt(i);
                        }
                    }
                }
            }
            for (var i = 0; i < DuraList.Count; i++)
            {
                for (var j = DuraList.Count - 1; j > i; j--)
                {
                    if (DuraList[j] > DuraList[j - 1])
                    {
                        var temp = DuraList[j];
                        DuraList[j] = DuraList[j - 1];
                        DuraList[j - 1] = temp;
                    }
                }
            }
            for (var i = 0; i < DuraList.Count; i++)
            {
                nDura = nDura + (int)DuraList[i];
                nItemCount++;
                if (nItemCount > 5)
                {
                    break;
                }
            }
            if (nItemCount == 0) return;
            btDura = (byte)HUtil32.Round(HUtil32._MIN(5, nItemCount) + HUtil32._MIN(5, nItemCount) * (nDura / nItemCount / 5.0));
            btDc = (byte)(nDcMin + nDcMin / 5 + nDcMax / 3);
            btSc = (byte)(nScMin + nScMin / 5 + nScMax / 3);
            btMc = (byte)(nMcMin + nMcMin / 5 + nMcMax / 3);
            btCc = 0;
            if (DelItemList != null)
            {
                User.SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0,
                    DelItemList.Count, 0, 0, "", DelItemList);
            }
            if (DuraList != null)
            {
                DuraList = null;
            }
        }

        private void UpgradeWapon(TPlayObject User)
        {
            switch (ClickUpWeaponNow(User))
            {
                case 1:
                    GotoLable(User, M2Share.sUPGRADEING, false);
                    break;
                case 2:
                    GotoLable(User, M2Share.sUPGRADEOK, false);
                    break;
                default:
                    GotoLable(User, M2Share.sUPGRADEFAIL, false);
                    break;
            }
        }

        public int ClickUpWeaponNow(TPlayObject user)
        {
            return QueueNativeWeaponUpgrade(user, false);
        }

        public int ClickUpWeaponNoBreak(TPlayObject user)
        {
            return QueueNativeWeaponUpgrade(user, true);
        }

        public int ClickGetBackUpWeapon(TPlayObject user)
        {
            lock (UpgradeWeaponSync)
            {
                try
                {
                    if (user == null) return -1;
                    if (!user.IsEnoughBag())
                    {
                        // sub_6447A4 @0x644C2E SysMsg(0x644C84, cx=0x38FF)
                        user.SysMsg("对不起，你无法再携带了", MsgColor.Red, MsgType.Hint);
                        return -1;
                    }
                    var record = M2Share.WeaponUpgrades.GetByCharacter(user.m_sCharName);
                    if (record == null) return 0;
                    if (!record.Built && user.m_btPermission < 4) return 1;
                    if (!LegacyUserItem208Codec.TryDecode(record.WeaponData, out var item, out var error))
                    {
                        M2Share.ErrorMessage($"WeaponUpg decode rejected for {user.m_sCharName}, idx={record.Idx}: {error}");
                        return 0;
                    }
                    if (record.ItemIdx != item.wIndex || record.ItemId != unchecked((uint)item.MakeIndex))
                    {
                        M2Share.ErrorMessage($"WeaponUpg identity mismatch for {user.m_sCharName}, idx={record.Idx}");
                        return 0;
                    }

                    var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
                    if (stdItem == null)
                    {
                        M2Share.ErrorMessage($"WeaponUpg item definition missing for {user.m_sCharName}, item={item.wIndex}");
                        return 0;
                    }
                    ApplyNativeWeaponUpgrade(user, item, record);
                    if (!M2Share.WeaponUpgrades.Delete(record.Idx)) return 0;
                    if (!user.AddItemToBag(item))
                    {
                        user.SysMsg("对不起，你无法再携带了", MsgColor.Red, MsgType.Hint);
                        M2Share.ErrorMessage($"WeaponUpg bag changed after delete for {user.m_sCharName}, idx={record.Idx}");
                        return -1;
                    }
                    if (stdItem.NeedIdentify == 1)
                    {
                        M2Share.AddGameDataLog("24" + "\t" + user.m_sMapName + "\t" + user.m_nCurrX + "\t" + user.m_nCurrY + "\t" + user.m_sCharName + "\t" + stdItem.Name + "\t" + item.MakeIndex + "\t" + '1' + "\t" + '0');
                    }
                    user.SendAddItem(item);
                    return 2;
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage($"WeaponUpg get-back failed for {user?.m_sCharName}: {ex.Message}");
                    return 0;
                }
            }
        }

        private int QueueNativeWeaponUpgrade(TPlayObject user, bool noBreak)
        {
            lock (UpgradeWeaponSync)
            {
                try
                {
                    if (user == null) return 0;
                    if (M2Share.WeaponUpgrades.HasPending(user.m_sCharName)) return 1;
                    return CommitNativeWeaponUpgrade(user, noBreak) ? 2 : 0;
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage($"WeaponUpg submit failed for {user?.m_sCharName}: {ex.Message}");
                    return 0;
                }
            }
        }

        private bool CommitNativeWeaponUpgrade(TPlayObject user, bool noBreak)
        {
            const int normalPrice = 10000;
            const int noBreakPrice = 30000;
            const string refineScroll = "武器修炼卷";
            const string sureSuccessOre = "紫金神矿";

            var weapon = user.m_UseItems[Grobal2.U_WEAPON];
            var price = noBreak ? noBreakPrice : normalPrice;
            if (weapon == null || weapon.wIndex == 0 || weapon.Dura <= 2000 ||
                weapon.btValue == null || weapon.btValue.Length != 14 || weapon.btValue[9] != 0 ||
                user.m_nGold < price || user.CheckItems(M2Share.g_Config.sBlackStone) == null)
            {
                return false;
            }
            if (noBreak && (GetBlackStoneDuraTotal(user) < 30 || user.CheckItems(refineScroll) == null))
            {
                return false;
            }

            var queuedItem = new TUserItem(weapon);
            var useSureSuccessOre = noBreak && user.CheckItems(sureSuccessOre) != null;
            // sub_6CA020 (the 不破碎 submit) ORs the flags in and never touches the rest of
            // the byte: @0x6CA0F3 `or byte [esi+0x47],0x80`, @0x6CA10D `or byte [esi+0x47],0x40`.
            // The plain submit has no writer at all for item+0x47. Assigning the whole byte
            // wiped the low six bits, which in production carry the GBK trail byte of the
            // 眼神 provenance map title at record 0x20..0x2B.
            if (noBreak)
            {
                queuedItem.UpgradeFlags |= 0x80;
                if (useSureSuccessOre) queuedItem.UpgradeFlags |= 0x40;
            }
            if (!LegacyUserItem208Codec.TryEncode(queuedItem, out _, out var codecError))
            {
                M2Share.ErrorMessage($"WeaponUpg submit rejected for {user.m_sCharName}: {codecError}");
                return false;
            }

            byte upDc = 0;
            byte upSc = 0;
            byte upMc = 0;
            byte upCc = 0;
            byte upDura = 0;
            UpgradeWaponAddValue(user, user.m_ItemList, ref upDc, ref upSc, ref upMc, ref upCc, ref upDura);
            if (noBreak)
            {
                if (!ConsumeOneBagItem(user, refineScroll)) return false;
                if (useSureSuccessOre) ConsumeOneBagItem(user, sureSuccessOre);
            }
            if (!LegacyUserItem208Codec.TryEncode(queuedItem, out var weaponData, out codecError))
            {
                M2Share.ErrorMessage($"WeaponUpg encode failed for {user.m_sCharName}: {codecError}");
                return false;
            }

            var idx = M2Share.WeaponUpgrades.Insert(user.m_sUserID, user.m_sCharName, queuedItem,
                upDc, upSc, upMc, upCc, upDura, weaponData);
            if (idx <= 0) return false;

            user.DecGold(price);
            AddWeaponUpgradeTax(price);
            user.GoldChanged();
            var stdItem = M2Share.UserEngine.GetStdItem(weapon.wIndex);
            if (stdItem?.NeedIdentify == 1)
            {
                M2Share.AddGameDataLog("25" + "\t" + user.m_sMapName + "\t" + user.m_nCurrX + "\t" + user.m_nCurrY + "\t" + user.m_sCharName + "\t" + stdItem.Name + "\t" + weapon.MakeIndex + "\t" + '1' + "\t" + '0');
            }
            user.SendDelItems(weapon);
            weapon.wIndex = 0;
            user.RecalcAbilitys();
            user.FeatureChanged();
            user.SendMsg(user, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
            return true;
        }

        private static int GetBlackStoneDuraTotal(TPlayObject user)
        {
            var total = 0;
            foreach (var item in user.m_ItemList)
            {
                if (item != null && string.Equals(M2Share.UserEngine.GetStdItemName(item.wIndex),
                        M2Share.g_Config.sBlackStone, StringComparison.OrdinalIgnoreCase))
                {
                    total += HUtil32.Round(item.Dura / 1000.0);
                }
            }
            return total;
        }

        private static bool ConsumeOneBagItem(TPlayObject user, string itemName)
        {
            var item = user.CheckItems(itemName);
            if (item == null || !user.DelBagItem(item.MakeIndex, itemName)) return false;
            var deleteItems = new List<TDeleteItem>
            {
                new()
                {
                    MakeIndex = item.MakeIndex,
                    ClientItemID = user.EnsureClientItemId(item),
                    sItemName = itemName
                }
            };
            user.SendMsg(user, Grobal2.RM_SENDDELITEMLIST, 0,
                deleteItems.Count, 0, 0, "", deleteItems);
            return true;
        }

        private void AddWeaponUpgradeTax(int price)
        {
            // ✅ 战神字节证据 (Tier-1)。EA: sub_6CA020 @0x6CA163-0x6CA182 (不磨损档 K=0x7530=30000) /
            //     sub_6C9D98 @0x6C9E89-0x6C9EA7 (普通档 K=0x2710=10000):
            //   mov edx,K ; call sub_6C7D64(DecGold) ; cmp byte [ebp-5],0 ; je <skip> ;
            //   mov eax,[0x7D6214] ; mov eax,[eax] ; mov edx,K ; call sub_65B31C(IncRateGold)
            // => 税额 == 【本次实际扣款额】:同一个立即数 K 同时喂给 DecGold 和 IncRateGold。
            // 故此处必须用形参 price(= noBreak?30000:10000,与上游 user.DecGold(price) 同值),
            // 不能另取 nUpgradeWeaponPrice —— 后者在不磨损档只累 10000,少收 2/3 的税。
            // 门只有 ONE 条: `cmp byte [ebp-5],0 / je`,其值来自 [merchant+0x578](税开关);
            // caller 佐证 sub_6446D5 `mov dl,byte [esi+0x578]` / @0x6446DD call sub_6CA020。
            // 原生【没有】 castle==nil 的第二分支: sub_65B31C 全 CODE 段(E8 rel32 全扫描)仅 5 个 caller
            // (0x63ECF2 买 / 0x63F020 修 / 0x63F28E 卖 / 0x6C9EA7 普通升级 / 0x6CA182 不磨损升级),
            // 已逐一反汇编,全部单门单分支,接收者恒是【单个城堡对象 [[0x7D6214]]】而非 CastleManager 列表;
            // 且 sub_65B31C 不出现在 1349 个 Delphi VMT 的任何槽位(无虚派发入口)。
            // 镜像内 "GetAllNpcTax" / "UpgradeWeaponPrice" 字符串各 0 hits(GBK+latin1 双编码搜过)——
            // 战神这两个价是编译期硬编码立即数,boGetAllNpcTax 本身就是 ref 分支引入的概念。
            // => 曾按 ref 加的 `else if (boGetAllNpcTax) → CastleManager.IncRateGold(...)` 回退分支
            //    在战神不存在,已删除(原生无城主时【不累计任何税】,删除即等价)。
            // 并列 ref 引用(保留,勿删;来源=GameOfMir 参考分支,非战神,仅算术形态线索):
            //   ObjNpc.pas:1190/1194 主张两个分支都累计固定 nUpgradeWeaponPrice —— 已被上述字节证据否证。
            if (!m_boCastle) return;
            if (m_Castle != null)
            {
                m_Castle.IncRateGold(price);
            }
        }

        private static void ApplyNativeWeaponUpgrade(TPlayObject user, TUserItem item, WeaponUpgradeRecord record)
        {
            if (record.UpDura <= 8)
            {
                item.DuraMax = item.DuraMax > 3000
                    ? (ushort)(item.DuraMax - 3000)
                    : (ushort)(item.DuraMax >> 1);
                if (item.Dura > item.DuraMax) item.Dura = item.DuraMax;
            }
            else if (record.UpDura <= 15)
            {
                if (M2Share.RandomNumber.Random(record.UpDura) < 6 && item.DuraMax > 1000)
                {
                    item.DuraMax -= 1000;
                }
                if (item.Dura > item.DuraMax) item.Dura = item.DuraMax;
            }
            else if (record.UpDura >= 18)
            {
                var value = M2Share.RandomNumber.Random(record.UpDura - 18);
                var loss = value < 5 ? 1000 : value < 8 ? 2000 : 4000;
                item.DuraMax = unchecked((ushort)(item.DuraMax - loss));
            }

            var tieChoice = record.UpDc == record.UpSc && record.UpDc == record.UpMc && record.UpDc == record.UpCc
                ? M2Share.RandomNumber.Random(4)
                : -1;
            var status = (byte)1;
            if ((record.UpDc >= record.UpMc && record.UpDc >= record.UpSc && record.UpDc >= record.UpCc) || tieChoice == 0)
                status = RollNativeWeaponUpgrade(user, item, record.UpDc, 10);
            if ((record.UpMc >= record.UpDc && record.UpMc >= record.UpSc && record.UpMc >= record.UpCc) || tieChoice == 1)
                status = RollNativeWeaponUpgrade(user, item, record.UpMc, 20);
            if ((record.UpSc >= record.UpMc && record.UpSc >= record.UpDc && record.UpSc >= record.UpCc) || tieChoice == 2)
                status = RollNativeWeaponUpgrade(user, item, record.UpSc, 30);
            if ((record.UpCc >= record.UpMc && record.UpCc >= record.UpDc && record.UpCc >= record.UpSc) || tieChoice == 2)
                status = RollNativeWeaponUpgrade(user, item, record.UpCc, 40);
            item.btValue[9] = status;
        }

        private static byte RollNativeWeaponUpgrade(TPlayObject user, TUserItem item, byte points, int successCode)
        {
            var capped = HUtil32._MIN(11, points);
            var chance = HUtil32._MIN(85, capped * 7 + 10 + item.btValue[3] - item.btValue[4] + user.m_nBodyLuckLevel);
            var noBreak = (item.UpgradeFlags & 0x80) != 0;
            var sureSuccess = (item.UpgradeFlags & 0x40) != 0;
            if (!sureSuccess && M2Share.RandomNumber.Random(noBreak ? 390 : 130) >= chance) return 1;
            var result = successCode;
            if (chance > 63 && M2Share.RandomNumber.Random(30) == 0) result = successCode + 1;
            if (chance > 79 && M2Share.RandomNumber.Random(200) == 0) result = successCode + 2;
            return (byte)result;
        }

        
        
        
        
        private void GetBackupgWeapon(TPlayObject user)
        {
            switch (ClickGetBackUpWeapon(user))
            {
                case -1:
                    GotoLable(user, M2Share.sGETBACKUPGFULL, false);
                    break;
                case 1:
                    GotoLable(user, M2Share.sGETBACKUPGING, false);
                    break;
                case 2:
                    GotoLable(user, M2Share.sGETBACKUPGOK, false);
                    break;
                default:
                    GotoLable(user, M2Share.sGETBACKUPGFAIL, false);
                    break;
            }
        }

        
        
        
        
        
        
        private int GetUserPrice(TPlayObject PlayObject, double nPrice)
        {
            int result;
            if (m_boCastle)
            {
                if (m_Castle != null && m_Castle.IsMasterGuild(PlayObject.m_MyGuild))
                {
                    var n14 = HUtil32._MAX(60, HUtil32.Round(m_nPriceRate * (M2Share.g_Config.nCastleMemberPriceRate / 100.0)));//80%
                    result = HUtil32.Round(nPrice / 100 * n14);
                }
                else
                {
                    result = HUtil32.Round(nPrice / 100 * m_nPriceRate);
                }
            }
            else
            {
                result = HUtil32.Round(nPrice / 100 * m_nPriceRate);
            }
            // ✅ ECON §4.18 二元权威并回【已完成】。以下为字节证据,结论见末尾:
            //  1) 原生【确实有】PAS API `SetRebate`:
            //     帮助文本 @0x736B55 "procedure SetRebate(nRebate : Word);"
            //     名字串 @0x739EF4,注册点 @0x738E34:
            //       00738E34  ba 38 74 64 00  mov  edx,0x647438  ; 处理器
            //       00738E39  b9 f4 9e 73 00  mov  ecx,0x739EF4  ; 名字 "SetRebate"
            //       00738E40  e8 3b b3 db ff  call 0x4F4180      ; 注册
            //  2) 处理器 sub_647438 写的字段是【+0x468】,并带输入校验:
            //       00647450  66 85 db              test  bx,bx
            //       00647453  76 12                 jbe   0x647467     ; <=0     -> 非法
            //       00647455  66 81 fb ff ff        cmp   bx,0xFFFF
            //       0064745A  73 0b                 jae   0x647467     ; >=65535 -> 非法
            //       0064745C  0f b7 d3              movzx edx,bx
            //       0064745F  89 90 68 04 00 00     mov   [eax+0x468],edx
            //       00647467  c7 80 68 04 00 00 64 00 00 00  mov dword [eax+0x468],100
            //                 非法值复位 100 并格式化 "[Rebate Err]:" (@0x6474CD) 报错
            //     构造器默认值 @0x63D888  c7 86 68 04 00 00 64 00 00 00  mov [esi+0x468],100
            //  3) 而费率阶段 sub_640208 读的【也是 +0x468】,全函数只有这一个费率字段:
            //       00640232  db 83 68 04 00 00  fild dword [ebx+0x468]  ; 城堡会员臂
            //       00640278  db 83 68 04 00 00  fild dword [ebx+0x468]  ; 普通臂
            // => 原生只有【一个】费率字段,SetRebate(sub_647438)就是它的 PAS 设置器。原 C# 误拆为
            //    m_nPriceRate + m_nRebate 两个权威(违 §4.18)。现已并回【完成】:setrebate 改写
            //    m_nPriceRate(钳制/复位见 PasApiBridge.cs),m_nRebate 字段删除,原本段多余的第二次
            //    `if (m_nRebate!=100) result=Round(result*m_nRebate/100)` 一并删除 —— 买价只缩放一次。
            return result;
        }

        private void UserSelect_SuperRepairItem(TPlayObject User)
        {
            User.SendNativeScriptRepair(this, 2);
        }

        private const int NewMarketInfoNameSize = 16;

        internal static byte[] EncodeNewMarketInfo(string itemName, int nextFlag, int price, int count, int itemIndex)
        {
            var nameBuffer = new byte[NewMarketInfoNameSize];
            HUtil32.GbkEncoding.GetEncoder().Convert(
                (itemName ?? string.Empty).AsSpan(),
                nameBuffer.AsSpan(1),
                true,
                out _,
                out var bytesUsed,
                out _);
            nameBuffer[0] = (byte)bytesUsed;

            using var stream = new MemoryStream(32);
            using var writer = new BinaryWriter(stream);
            writer.Write(nameBuffer);
            writer.Write(nextFlag);
            writer.Write(price);
            writer.Write(count);
            writer.Write(itemIndex);
            return stream.ToArray();
        }

        public void UserSelect_BuyItem(TPlayObject User, int nInt)
        {
            ClearShopDetailHandles(User);
            using var goodsStream = new MemoryStream();
            var n10 = 0;
            for (var i = 0; i < m_GoodsList.Count; i++)
            {
                var List14 = m_GoodsList[i];
                if (List14.Count == 0) continue;
                var UserItem = List14[0];
                var StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                if (StdItem != null)
                {
                    var sName = ItmUnit.GetItemName(UserItem);
                    var nPrice = GetUserPrice(User, GetItemPrice(UserItem.wIndex));
                    var nStock = List14.Count;
                    short nSubMenu;
                    // ✅ ECON-14 战神字节证据 @0x63EC2A三梯级:
                    //   sub al,5 / jb       → <5 跳过MakeIndex比较
                    //   sub al,0x1a / je    → ==31 (5+26) 跳过
                    //   sub al,0xb / je     → ==42 (31+11) 跳过
                    // 注意：原版**没有30**这个梯级，从5直接跳到31。
                    if (StdItem.StdMode <= 4 || StdItem.StdMode == 31 || StdItem.StdMode == 42)
                    {
                        nSubMenu = 0;
                    }
                    else
                    {
                        nSubMenu = 1;
                    }
                    var record = EncodeNewMarketInfo(sName, nSubMenu, nPrice, nStock, UserItem.wIndex);
                    goodsStream.Write(record, 0, record.Length);
                    n10++;
                }
            }
            User.SendMsg(this, Grobal2.RM_SENDGOODSLIST, 0, ObjectId, n10, 0,
                string.Empty, goodsStream.ToArray());
        }

        private void UserSelect_SellItem(TPlayObject User)
        {
            User.SendMsg(this, Grobal2.RM_SENDUSERSELL, 0, ObjectId, 0, 0, "");
        }

        private void UserSelect_RepairItem(TPlayObject User)
        {
            User.SendNativeScriptRepair(this, 1);
        }

        private void UserSelect_MakeDurg(TPlayObject User)
        {
            IList<TUserItem> List14;
            TUserItem UserItem;
            GoodItem StdItem;
            var sSendMsg = string.Empty;
            for (var i = 0; i < m_GoodsList.Count; i++)
            {
                List14 = m_GoodsList[i];
                UserItem = List14[0];
                StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                if (StdItem != null)
                {
                    sSendMsg = sSendMsg + StdItem.Name + '/' + 0 + '/' + M2Share.g_Config.nMakeDurgPrice + '/' + 1 + '/';
                }
            }
            if (sSendMsg != "")
            {
                User.SendMsg(this, Grobal2.RM_USERMAKEDRUGITEMLIST, 0, ObjectId, 0, 0, sSendMsg);
            }
        }

        private void UserSelect_ItemPrices(TPlayObject User)
        {
        }

        private void UserSelect_Storage(TPlayObject User)
        {
            User.m_nStoragePage = 0;
            User.SendMsg(this, Grobal2.RM_USERSTORAGEITEM, 0, ObjectId, 0, 0, "");
        }

        private void UserSelect_GetBack(TPlayObject User)
        {
            User.m_nStoragePage = 0;
            User.SendMsg(this, Grobal2.RM_USERGETBACKITEM, 0, ObjectId, 0, 0, "");
        }

        private void UserSelect_GetNextPage(TPlayObject User)
        {
            var totalPages = HUtil32._MAX(2, (User.m_StorageItemList.Count + TPlayObject.STORAGE_PAGE_SIZE - 1) / TPlayObject.STORAGE_PAGE_SIZE);
            if (User.m_nStoragePage < totalPages - 1)
            {
                User.m_nStoragePage++;
            }
            User.SendMsg(this, Grobal2.RM_USERGETBACKITEM, 0, ObjectId, 0, 0, "");
        }

        private void UserSelect_GetPreviousPage(TPlayObject User)
        {
            if (User.m_nStoragePage > 0)
            {
                User.m_nStoragePage--;
            }
            User.SendMsg(this, Grobal2.RM_USERGETBACKITEM, 0, ObjectId, 0, 0, "");
        }


        public override void UserSelect(TPlayObject PlayObject, string sData)
        {
            var sLabel = string.Empty;
            const string sExceptionMsg = "[Exception] TMerchant::UserSelect... Data: {0}";
            base.UserSelect(PlayObject, sData);
            if (this is not Merchant)// 如果类名不是 TMerchant 则不执行以下处理函数
            {
                return;
            }
            try
            {
                if (!m_boCastle || !(m_Castle != null && m_Castle.m_boUnderWar))
                {
                    if (!PlayObject.m_boDeath && sData != "" && sData[0] == '@')
                    {
                        string sMsg = HUtil32.GetValidStr3(sData, ref sLabel, new char[] { '\r' });
                        PlayObject.m_sScriptLable = sData;
                        if (string.Compare(sLabel, M2Share.sGETNEXTPAGE, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boGetback)
                            {
                                UserSelect_GetNextPage(PlayObject);
                            }
                            return;
                        }
                        if (string.Compare(sLabel, M2Share.sGETPREVIOUSPAGE, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boGetback)
                            {
                                UserSelect_GetPreviousPage(PlayObject);
                            }
                            return;
                        }
                        bool boCanJmp = PlayObject.LableIsCanJmp(sLabel);
                        if (!boCanJmp)
                        {
                            return;
                        }
                        if (string.Compare(sLabel, M2Share.sSL_SENDMSG, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (sMsg == "")
                            {
                                return;
                            }
                        }
                        if (TryGotoPascalLabel(PlayObject, sLabel))
                        {
                            return;
                        }
                        if (string.Compare(sLabel, M2Share.sOFFLINEMSG, StringComparison.OrdinalIgnoreCase) == 0)// 增加挂机
                        {
                            if (m_boOffLineMsg)
                            {
                                SetOffLineMsg(PlayObject, sMsg);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sSL_SENDMSG, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boSendmsg)
                            {
                                SendCustemMsg(PlayObject, sMsg);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sSUPERREPAIR, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boS_repair)
                            {
                                UserSelect_SuperRepairItem(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sBUY, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boBuy)
                            {
                                UserSelect_BuyItem(PlayObject, 0);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sSELL, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boSell)
                            {
                                UserSelect_SellItem(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sREPAIR, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boRepair)
                            {
                                UserSelect_RepairItem(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sMAKEDURG, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boMakeDrug)
                            {
                                UserSelect_MakeDurg(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sPRICES, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boPrices)
                            {
                                UserSelect_ItemPrices(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sSTORAGE, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boStorage)
                            {
                                UserSelect_Storage(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sGETBACK, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boGetback)
                            {
                                UserSelect_GetBack(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sGETNEXTPAGE, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boGetback)
                            {
                                UserSelect_GetNextPage(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sGETPREVIOUSPAGE, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boGetback)
                            {
                                UserSelect_GetPreviousPage(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sUPGRADENOW, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boUpgradenow)
                            {
                                UpgradeWapon(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sGETBACKUPGNOW, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (m_boGetBackupgnow)
                            {
                                GetBackupgWeapon(PlayObject);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sGETMARRY, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            // Faithful no-op: this M2Server baseline has NO native merchant marry command.
                            // The labels "getmarry"/"getmaster" and the script commands GETMARRY(303)/
                            // GETMASTER(304) have ZERO occurrences in M2Server_unpacked_fixed.exe (raw grep
                            // 2026-08-02), and m_boGetMarry/m_boGetMaster are never armed. The only native
                            // marriage flow is the PAS 月老 path (RequestMarry/AgreeMarry/DisAgreeMarry/
                            // NpcDivMarry), already ported (NativeCorpsService marry cluster). No merchant
                            // transaction is invented. (Previously a copy-paste stub that wrongly called
                            // GetBackupgWeapon.)
                        }
                        else if (string.Compare(sLabel, M2Share.sGETMASTER, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            // See sGETMARRY above: no native merchant master command; flag never armed; no-op.
                        }
                        else if (HUtil32.CompareLStr(sLabel, M2Share.sUSEITEMNAME, M2Share.sUSEITEMNAME.Length))
                        {
                            if (m_boUseItemName)
                            {
                                ChangeUseItemName(PlayObject, sLabel, sMsg);
                            }
                        }
                        else if (string.Compare(sLabel, M2Share.sEXIT, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            PlayObject.SendMsg(this, Grobal2.RM_MERCHANTDLGCLOSE, 0, ObjectId, 0, 0, "");
                        }
                        else if (string.Compare(sLabel, M2Share.sBACK, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            if (PlayObject.m_sScriptGoBackLable == "")
                            {
                                PlayObject.m_sScriptGoBackLable = M2Share.sMAIN;
                            }
                            GotoLable(PlayObject, PlayObject.m_sScriptGoBackLable, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage(format(sExceptionMsg, sData));
                M2Share.MainOutMessage(ex.StackTrace);
            }
        }

        public override void Run()
        {
            try
            {
                SaveNativeGoodsIfDue(HUtil32.GetTickCount(),
                    GetNativeGoodsRootPath());
                if ((HUtil32.GetTickCount() - dwRefillGoodsTick) > 30000)
                {
                    dwRefillGoodsTick = HUtil32.GetTickCount();
                    RefillGoods();
                }
                if (M2Share.RandomNumber.Random(50) == 0)
                {
                    TurnTo((byte)M2Share.RandomNumber.Random(8));
                }
                else
                {
                    if (M2Share.RandomNumber.Random(50) == 0)
                    {
                        SendRefMsg(Grobal2.RM_HIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
                    }
                }
                if (m_boCastle && m_Castle != null && m_Castle.m_boUnderWar)
                {
                    if (!m_boFixedHideMode)
                    {
                        SendRefMsg(Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
                        m_boFixedHideMode = true;
                    }
                }
                else
                {
                    if (m_boFixedHideMode)
                    {
                        m_boFixedHideMode = false;
                        SendRefMsg(Grobal2.RM_HIT, m_btDirection, m_nCurrX, m_nCurrY, 0, "");
                    }
                }
                if (m_boCanMove && (HUtil32.GetTickCount() - m_dwMoveTick) > m_dwMoveTime * 1000)
                {
                    m_dwMoveTick = HUtil32.GetTickCount();
                    SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, 0, 0, 0, "");
                    MapRandomMove(m_sMapName, 0);
                }
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(e.Message);
            }
            base.Run();
        }

        public override bool Operate(TProcessMessage ProcessMsg)
        {
            return base.Operate(ProcessMsg);
        }

        public void LoadNPCData()
        {
            LoadGoodRecord(GetNativeGoodsRootPath());
            EnsureGoodsPrices();
        }

        public Merchant() : base()
        {
            m_btRaceImg = Grobal2.RCC_MERCHANT;
            m_wAppr = 0;
            m_nPriceRate = 100;  // 原生构造器 @0x63D888 `mov [esi+0x468],0x64`
            m_boCastle = false;
            m_ItemTypeList = new List<int>();
            m_RefillGoodsList = new List<TGoods>();
            m_GoodsList = new List<IList<TUserItem>>();
            m_ItemPriceList = new List<TItemPrice>();
            dwRefillGoodsTick = HUtil32.GetTickCount();
            _nativeGoodsSaveTick = HUtil32.GetTickCount();
            m_boBuy = false;
            m_boSell = false;
            m_boMakeDrug = false;
            m_boPrices = false;
            m_boStorage = false;
            m_boGetback = false;
            m_boUpgradenow = false;
            m_boGetBackupgnow = false;
            m_boRepair = false;
            m_boS_repair = false;
            m_boGetMarry = false;
            m_boGetMaster = false;
            m_boUseItemName = false;
            m_dwMoveTick = HUtil32.GetTickCount();
        }

        
        
        
        public void LoadMerchantScript()
        {
            m_ItemTypeList.Clear();
            var sScriptDir = string.IsNullOrEmpty(m_sFilePath) ? M2Share.sPsNpcscripts : m_sFilePath;
            m_sPath = sScriptDir;
            // 战神版: .pas 脚本由 PasEngine 在 GotoLable 中动态加载，不加载 .txt
        }

        public override void Click(TPlayObject PlayObject)
        {
            base.Click(PlayObject);
        }

        protected override void GetVariableText(TPlayObject PlayObject, ref string sMsg, string sVariable)
        {
            string sText;
            base.GetVariableText(PlayObject, ref sMsg, sVariable);
            switch (sVariable)
            {
                case "$PRICERATE":
                    sText = m_nPriceRate.ToString();
                    sMsg = ReplaceVariableText(sMsg, "<$PRICERATE>", sText);
                    break;
                case "$UPGRADEWEAPONFEE":
                    sText = M2Share.g_Config.nUpgradeWeaponPrice.ToString();
                    sMsg = ReplaceVariableText(sMsg, "<$UPGRADEWEAPONFEE>", sText);
                    break;
                case "$USERWEAPON":
                    {
                        if (PlayObject.m_UseItems[Grobal2.U_WEAPON].wIndex != 0)
                        {
                            sText = M2Share.UserEngine.GetStdItemName(PlayObject.m_UseItems[Grobal2.U_WEAPON].wIndex);
                        }
                        else
                        {
                            sText = "无";
                        }
                        sMsg = ReplaceVariableText(sMsg, "<$USERWEAPON>", sText);
                        break;
                    }
            }
        }

        private double GetUserItemPrice(TUserItem UserItem)
        {
            double result;
            GoodItem StdItem;
            double n20;
            int nC;
            int n14;
            var n10 = GetItemPrice(UserItem.wIndex);
            StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
            // ✅ 战神字节证据 (Tier-1) — ECON-25: 堆叠物品的基础价要【乘堆叠数量】。
            // EA: sub_63F3B4(基础价解析器) 尾部 @0x63F442-0x63F45B,两条取价分支(商人价格表命中 /
            // 模板价 ROUND(Price*1.1))汇合之后【无条件】执行:
            //   0063F442  8b45fc      mov   eax,[ebp-4]          ; eax = 物品【实例】
            //   0063F445  80781407    cmp   byte [eax+0x14],7    ; 实例 KIND 字节 == 7 ?
            //   0063F449  7513        jne   0x63f45e             ; 非堆叠 -> 跳过
            //   0063F44B  837df800    cmp   dword [ebp-8],0
            //   0063F44F  7e0d        jle   0x63f45e             ; 价 <=0 -> 跳过
            //   0063F454  0fb74026    movzx eax,word [eax+0x26]  ; 数量 = 实例+0x26 (= Dura)
            //   0063F458  f76df8      imul  dword [ebp-8]        ; 单操作数 imul,只留低 32 位
            //   0063F45B  8945f8      mov   [ebp-8],eax
            // 位置决定性: 乘法在 sub_63F3B4 【内部】,即在 sub_63F380 @0x63F3A9 派发
            // VMT+0x20(磨损/属性段)【之前】,也在 sub_640208(费率段)与卖价 sar 1 之前。故
            // 买(0x63EC47)/卖(0x63F22E)/修(0x63EF73) 三条钱路共用这一段,放在本函数顶部即三路齐覆盖。
            // 门是【实例】+0x14 == 7,不是模板 StdMode —— 注意同函数 @0x63F41B 的姊妹读法
            // `mov eax,[eax+0x1c] / mov dl,[eax+0x14]` 先解引用模板才取 +0x14(那个才是 StdMode),
            // 两个 +0x14 是不同字段。实例 +0x14 是各子类构造器写的 KIND 标记:
            // 基类 TBaseItem 构造器 sub_783788 @0x7837AE 写 0,堆叠基类 sub_7880F0 @0x788118 写 7
            // (同处 @0x788112 写 word[+0x26]=1 = 新建堆叠计数 1),全镜像内【只有堆叠族写 7】
            // (idat_bounded_close_20260802.md TARGET 1)。C# 对该谓词的既有约定是
            // NativeItemFactory.IsPileItem(StdMode>=150 && 有运行时类名),已被物品使用 mode 4 /
            // 钻石锻造 / 英雄吞噬三处沿用,此处保持一致。
            // 乘法是 32 位 imul,溢出【静默截断】不做 64 位加宽,故用 unchecked 整数乘复刻。
            // 数量取【实例】+0x26 (UserItem.Dura),不是模板 DuraMax。
            if (n10 > 0 && NativeItemFactory.IsPileItem(StdItem))
            {
                n10 = unchecked((int)n10 * (int)UserItem.Dura);
            }
            if (n10 > 0)
            {
                // ✅ 战神字节证据 (Tier-1) — PRICE-04: 原生门集合是【liveMax>0 && cur>0 && StdMode>4】。
                // EA: TBaseItem sub_783D70 @0x783D7D-0x783DA1:
                //   00783D7D  0f b7 73 28        movzx esi,word [ebx+0x28]   ; liveMax = 实例 +0x28
                //   00783D81  0f b7 43 26        movzx eax,word [ebx+0x26]   ; cur     = 实例 +0x26
                //   00783D85  89 45 fc           mov   [ebp-4],eax
                //   00783D88  85 f6              test  esi,esi
                //   00783D8A  0f 8e 5b 01 00 00  jle   0x783EEB              ; liveMax<=0 -> 原价返回
                //   00783D90  83 7d fc 00        cmp   dword [ebp-4],0
                //   00783D94  0f 8e 51 01 00 00  jle   0x783EEB              ; cur<=0    -> 原价返回
                //   00783D9A  8b 43 1c           mov   eax,[ebx+0x1c]
                //   00783D9D  80 78 14 04        cmp   byte [eax+0x14],4
                //   00783DA1  0f 86 44 01 00 00  jbe   0x783EEB              ; StdMode<=4 -> 原价返回
                // 肉/矿的 override 各自也有同样的前两道门(sub_786208 @0x78621E `test edi,edi / jle`
                // + @0x786222 `test esi,esi / jle`;sub_7862B4 @0x7862CA / @0x7862D2 同形),
                // 三个函数在 C# 被折叠进本条 if,故这一道 UserItem.Dura > 0 同时覆盖三者。
                // 缺 cur>0 门时,零耐久装备会误入 stage B:磨损项 n10/2/liveMax*(liveMax-0) = n10/2,
                // 定价直接砍半;原生根本不进这段,直接原价返回。耐久打空是常态,可达性高。
                //
                // ⚠ StdItem.DuraMax > 0 是【有意保留的护栏,不是遗漏】—— 原生无此门:
                // stage A 的除法 @0x783E9B `fdivp` 之前【没有任何零检查】,Delphi 默认 x87 控制字
                // 未屏蔽 ZeroDivide,模板 DuraMax==0 时原生会抛 EZeroDivide;C# 保留此门则静默
                // 跳过整段返回原价。方向上 C# 更安全但不等价。发布版 stditems.MYD 的
                // idx241「牢犯匕首」(StdMode=5,DuraMax=0,NeedConf=0x40) 与 NativeItemPlus28
                // 的 AddDura 路径已证明零模板状态在配置/随机掉落链上可达；只是现有样本未命中。
                // 因此此处仍是已知的 fail-closed 差异，不再标为“可达性未证”。在异常传播边界
                // 完成审计前保留护栏，避免把原生 EZeroDivide 直接放大到服务进程。
                // 本条契约已在台账里来回翻过一次,就地钉死:【不要再把它当成新 bug 删掉】。
                if (StdItem != null && StdItem.StdMode > 4 && StdItem.DuraMax > 0 &&
                    UserItem.DuraMax > 0 && UserItem.Dura > 0)
                {
                    if (StdItem.StdMode == 40)// 肉
                    {
                        if (UserItem.Dura <= UserItem.DuraMax)
                        {
                            n20 = n10 / 2.0 / UserItem.DuraMax * (UserItem.DuraMax - UserItem.Dura);
                            n10 = HUtil32._MAX(2, HUtil32.Round(n10 - n20));
                        }
                        else
                        {
                            // ✅ 战神字节证据 (Tier-1) — PRICE-13: 超上限奖励的差值是
                            // 【cur - liveMax】(正数),不是 liveMax - cur。EA: TMeatItem
                            // sub_786208 的 over 臂 @0x78627B-0x78629B(edi=cur[+0x26],
                            // esi=liveMax[+0x28],本臂由 @0x78623C `cmp edi,esi / jge` 进入,
                            // 即 cur > liveMax):
                            //   0078627B  db 45 fc           fild  dword [ebp-4]      ; price
                            //   0078627E  89 75 ec           mov   [ebp-0x14],esi     ; liveMax
                            //   00786281  db 45 ec           fild  dword [ebp-0x14]
                            //   00786284  de f9              fdivp st(1)              ; price/liveMax
                            //   00786286  d8 0d b0 62 78 00  fmul  dword [0x7862B0]   ; * 2.0f
                            //   0078628C  2b fe              sub   edi,esi            ; 【cur - liveMax】
                            //   0078628E  89 7d e8           mov   [ebp-0x18],edi
                            //   00786291  db 45 e8           fild  dword [ebp-0x18]
                            //   00786294  de c9              fmulp st(1)
                            //   00786296  e8 d9 d2 c7 ff     call  0x403574           ; @ROUND
                            //   0078629B  01 45 fc           add   [ebp-4],eax        ; 累加,本臂无 _MAX 钳位
                            // 写反后差值恒为负(本分支的进入条件就是 Dura > DuraMax),负的 n10
                            // 继续流入下方 StdMode>4 段被逐级放大,最后被 :1721 的 _MAX(2,…) 兜到 2 ——
                            // 不崩不报错,只静默给出荒谬低价。
                            // ✅ 战神字节证据 (Tier-1) — PRICE-13 over 臂 @0x78627B-0x78629B:
                            // fild price / fdivp liveMax / fmul 2.0f / fild (cur-liveMax) / fmulp / @ROUND.
                            // 全程 x87 80 位; double 链在 price=1,live=196,delta=147 等处 ±1。
                            n10 = n10 + HUtil32.RoundRational(
                                (long)n10 * 2 * (UserItem.Dura - UserItem.DuraMax),
                                UserItem.DuraMax);
                        }
                    }
                    if (StdItem.StdMode == 43)
                    {
                        // ✅ 战神字节证据 (Tier-1)。StdMode 43 = TOreItem,原生是 VMT slot+0x20 的 override
                        // sub_7862B4(基类 = sub_783D70,@0x786366 算完再 call 落回基类)。
                        // 10000 下限钳位 @0x7862DA-0x7862E2:全程【只动 EBX 这个局部寄存器】,
                        // 【从不写回 [esi+0x28]】(UserItem.DuraMax 字段)。
                        // 旧 C# 写成 `UserItem.DuraMax = 10000;` —— 为了【算个价】就把玩家物品的耐久上限
                        // 永久改掉(查询卖价/买价/修理报价都会触发),是持久化污染。现改为只钳位到局部。
                        // 另: 基类 sub_783D70 @0x783E9D `mov [ebp-0x1C],esi` 里的 ESI 是从 [ebx+0x28]
                        // 【重新读取】的原始字段值,故下面 StdMode>4 段仍应使用未钳位的 UserItem.DuraMax。
                        // Dura>DuraMax 分支的 1.3 是 10 字节 extended 常量 [0x786378](不是 float32)。
                        var oreDuraMax = UserItem.DuraMax < 10000 ? 10000 : UserItem.DuraMax;
                        if (UserItem.Dura <= oreDuraMax)
                        {
                            n20 = n10 / 2.0 / oreDuraMax * (oreDuraMax - UserItem.Dura);
                            n10 = HUtil32._MAX(2, HUtil32.Round(n10 - n20));
                        }
                        else
                        {
                            // ✅ 战神字节证据 (Tier-1) — PRICE-16: 与肉同形,超上限差值是
                            // 【cur - liveMaxF】(liveMaxF = 已按 10000 下限钳位的局部值),
                            // 不是 liveMaxF - cur。EA: TOreItem sub_7862B4 的 over 臂
                            // @0x78633C-0x78635E(edi=cur[+0x26],ebx=liveMaxF,本臂由
                            // @0x7862FD `cmp edi,ebx / jge` 进入,即 cur > liveMaxF):
                            //   0078633C  db 45 fc           fild  dword [ebp-4]      ; price
                            //   0078633F  89 5d ec           mov   [ebp-0x14],ebx     ; liveMaxF
                            //   00786342  db 45 ec           fild  dword [ebp-0x14]
                            //   00786345  de f9              fdivp st(1)              ; price/liveMaxF
                            //   00786347  db 2d 78 63 78 00  fld   xword [0x786378]   ; 1.3 (10 字节 extended)
                            //   0078634D  de c9              fmulp st(1)
                            //   0078634F  2b fb              sub   edi,ebx            ; 【cur - liveMaxF】
                            //   00786351  89 7d e8           mov   [ebp-0x18],edi
                            //   00786354  db 45 e8           fild  dword [ebp-0x18]
                            //   00786357  de c9              fmulp st(1)
                            //   00786359  e8 16 d2 c7 ff     call  0x403574           ; @ROUND
                            //   0078635E  01 45 fc           add   [ebp-4],eax        ; 累加,本臂无 _MAX 钳位
                            // RoundOrePriceBonus 第三参就是这个 delta;换成正值后走其主路径,
                            // 内部对负分子的 floor 修正(HUtil32.cs:212-216)不再被触发。
                            n10 = n10 + HUtil32.RoundOrePriceBonus((long)n10, oreDuraMax, UserItem.Dura - oreDuraMax);
                        }
                    }
                    if (StdItem.StdMode > 4)
                    {
                        n14 = 0;
                        nC = 0;
                        while (true)
                        {
                            if (StdItem.StdMode == 5 || StdItem.StdMode == 6)
                            {
                                if (nC != 4 && nC != 9)
                                {
                                    if (nC == 6)
                                    {
                                        if (UserItem.btValue[nC] > 10)
                                        {
                                            n14 = n14 + (UserItem.btValue[nC] - 10) * 2;
                                        }
                                        else
                                        {
                                            // ✅ 战神字节证据 (Tier-1) — PRICE-06: StdMode 5/6 (武器族) 的 index==6
                                            // 属性,当 v<=10 时走【明文加 v】臂,不是零贡献。EA: TBaseItem
                                            // 价格虚方法 sub_783D70 @0x783DCC-0x783DED:
                                            //   0x783DCC  8a5330        mov dl,byte [ebx+0x30]   ; v = btValue[6]
                                            //   0x783DCF  80fa0a        cmp dl,0x0A
                                            //   0x783DD2  7613          jbe 0x783DE7             ; v<=10 -> 明文臂
                                            //   0x783DD4  81e2ff000000  and edx,0xFF             ; v>10 臂:
                                            //   0x783DDA  83ea0a        sub edx,0x0A             ;   (v-10)
                                            //   0x783DDD  03d2          add edx,edx              ;   *2
                                            //   0x783DDF  0155f8        add [ebp-8],edx          ;   accum += (v-10)*2
                                            //   0x783DE7  81e2ff000000  and edx,0xFF             ; 明文臂:
                                            //   0x783DED  0155f8        add [ebp-8],edx          ;   accum += v
                                            // 缺此 else 时,武器 index-6 属性值<=10(绝大多数属性的常态)对
                                            // 加成累加项 accum【零贡献】,经下方 `n10 += (n10/5)*accum` 传导,
                                            // 系统性压低武器的买/卖/修报价。
                                            n14 = n14 + UserItem.btValue[nC];
                                        }
                                    }
                                    else
                                    {
                                        n14 = n14 + UserItem.btValue[nC];
                                    }
                                }
                            }
                            else
                            {
                                n14 += UserItem.btValue[nC];
                            }
                            nC++;
                            if (nC >= 8)
                            {
                                break;
                            }
                        }
                        if (n14 > 0)
                        {
                            // ✅ 战神字节证据 (Tier-1)。EA: sub_783D70 = TBaseItem VMT slot+0x20 价格虚方法,
                            // 调用链 sub_63F380(GetUserItemPrice 包装) @0x63F39A call sub_63F3B4(基础价) →
                            // @0x63F3A7 `mov ecx,[eax] / call dword [ecx+0x20]` 虚派发 → sub_783D70。
                            // 关键算术 @0x783E73-0x783E86 (raw: ... F7 F9 / F7 6D F8 / 03 F8):
                            //   cmp dword[ebp-8],0 ; jle skip        ; n14 > 0 ?
                            //   mov eax,edi ; mov ecx,5 ; cdq ; idiv ecx    ; eax = n10 div 5 (32-bit signed 真整除)
                            //   imul dword[ebp-8]                            ; eax = (n10 div 5) * n14
                            //   add edi,eax                                  ; <<< n10 := n10 + (n10 div 5)*n14
                            // => `div 5` 确实是整除,但【基础价 n10 本身仍在】——不是 `n10 := n10 div 5 * n14`。
                            // 丢掉 `n10 +` 会把带属性装备(StdMode>4)定价压成 0.2*n14 倍:n14=1 时 -83%
                            // (正确 1.2*n10 / 错值 0.2*n10),n14=5 时 -50%(正确 2.0 / 错值 1.0)。
                            // 该函数同时服务【买/卖/修】三条钱路(买 sub_63EB34@0x63EC47、卖 sub_63F200@0x63F22E、
                            // 修 sub_63EE9C 经 GetUserPrice),故三处一起偏低。
                            // 属性累加循环 @0x783DAC-0x783E6D (跳表 0x783E03,btValue 基址 item+0x2A;
                            // StdMode 5/6 时 nC==4 跳过、nC==6 走阈值 10 的 (v-10)*2)。
                            // StdMode 40/43 不在本函数: TMeatItem sub_786208 / TOreItem sub_7862B4 是 override,
                            // 各自算完再 call sub_783D70 落回基类(@0x7862A3 / @0x786366)。
                            // 原生此步的 n10 是【整数 EDI】(idiv 前必须是整数);C# 的 n10 是 double,故显式 (int)
                            // 截断以复刻 idiv。原生 idiv 对负数向零截断(Math.Floor 向下取整,方向不同),
                            // 基础价非负故不触发,记录备查。
                            // 并列 ref 引用(保留,勿删;来源=GameOfMir 参考分支,非战神,仅算术形态线索):
                            //   ObjNpc.pas:1910 `n10 := n10 div 5 * n14` —— 该 ref 行【漏了 `n10 +`】,
                            //   照抄它造成本次 -83% 定价 bug;`div` 是整除这半句 ref 恰好说对。
                            n10 = n10 + (double)((int)n10 / 5 * n14);
                        }
                        // ✅ 战神字节证据 (Tier-1) — PRICE-08 Stage A @0x783E8B-0x783EA5:
                        // fild price / fdivp templateMax / fmulp liveMax / call @ROUND — 无 fstp qword。
                        // double 链在 price=1,live=147,template=98 等处 ±1。
                        n10 = HUtil32.RoundRational(
                            (long)n10 * UserItem.DuraMax,
                            StdItem.DuraMax);
                        n20 = n10 / 2.0 / UserItem.DuraMax * (UserItem.DuraMax - UserItem.Dura);
                        n10 = HUtil32._MAX(2, HUtil32.Round(n10 - n20));
                    }
                }
            }
            result = n10;
            return result;
        }

        public void ClientBuyItem(TPlayObject PlayObject, string sItemName, int nInt)
        {
            IList<TUserItem> List20;
            TUserItem UserItem;
            GoodItem StdItem;
            int nPrice;
            string sUserItemName;
            var bo29 = false;
            var n1C = 1;
            var detailItem = ResolveShopDetailItem(PlayObject, nInt);
            for (var i = 0; i < m_GoodsList.Count; i++)
            {
                if (bo29)
                {
                    break;
                }
                List20 = m_GoodsList[i];
                if (List20.Count == 0) continue;
                UserItem = List20[0];
                StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                if (StdItem != null)
                {
                    sUserItemName = ItmUnit.GetItemName(UserItem);
                    if (PlayObject.IsAddWeightAvailable(StdItem.Weight))
                    {
                        if (sUserItemName == sItemName)
                        {
                            for (var j = 0; j < List20.Count; j++)
                            {
                                UserItem = List20[j];
                                var isStaticGoods = StdItem.StdMode <= 4 ||
                                                    StdItem.StdMode == 31 || StdItem.StdMode == 42;
                                if (isStaticGoods || ReferenceEquals(UserItem, detailItem))
                                {
                                    nPrice = GetUserPrice(PlayObject, GetUserItemPrice(UserItem));
                                    if (PlayObject.m_nGold >= nPrice && nPrice > 0)
                                    {
                                        // ✅ 战神字节证据 (Tier-1) — ECON-34 买路盖印 @0x63EC6F-0x63EC7A:
                                        //   0063EC6F  push 0          ; reason=0
                                        //   0063EC71  mov cl, 1       ; stampEnable=true (ECX 门控 sub_6B739F test al,bl)
                                        //   0063EC7A  call [edi+0x248] ; AddItemToBag
                                        if (PlayObject.AddItemToBag(UserItem, 0, true))
                                        {
                                            // ✅ 战神字节证据 (Tier-1) — ECON-34: 买入扣款走的是
                                            // 【DecGold(0x6C7D64)】,不是裸减字段。EA: sub_63EB34 @0x63EC7A-0x63EC8E:
                                            //   0063EC7A  ff 97 48 02 00 00  call dword [edi+0x248]  ; AddItemToBag
                                            //   0063EC80  84 c0              test al,al
                                            //   0063EC82  0f 84 ae 00 00 00  je   0x63ED36           ; 入包失败 -> 不扣款
                                            //   0063EC88  8b 55 ec           mov  edx,[ebp-0x14]     ; nPrice
                                            //   0063EC8B  8b 45 f8           mov  eax,[ebp-8]        ; player
                                            //   0063EC8E  e8 d1 90 08 00     call 0x6C7D64           ; DecGold(返回值丢弃)
                                            // DecGold 体内 @0x6C7D7B `call 0x6C19B4` 就是客户端金币刷新
                                            // (RM_GOLDCHANGED 10136),裸 `m_nGold -= nPrice` 会把它漏掉 ——
                                            // 卖出路径由 IncGold 内联触发、修理路径由 DecGold 内联触发,唯独买入没有,
                                            // 表现为买完金币显示不刷新,直到下一次任意金币变动。
                                            // 金额本身不差:上面 :1860 的 `m_nGold >= nPrice && nPrice > 0` 已使
                                            // DecGold 的两道门(@0x6C7D6B `jl` 负数、@0x6C7D73 `jg` 余额不足)必然通过。
                                            // 与原生一致地忽略返回值(0x63EC8E 之后没有 test al,al)。
                                            PlayObject.DecGold(nPrice);
                                            // ✅ 战神字节证据 (Tier-1)。买入税点 sub_63EB34 @0x63ECDC-0x63ECF2:
                                            //   mov eax,[ebp-4] ; cmp byte [eax+0x578],0 ; je 0x63ECF7   <== 唯一门,无 else
                                            //   mov eax,[0x7D6214] ; mov eax,[eax] ; mov edx,[ebp-0x14]  ; <== 本次成交价 nPrice
                                            //   call sub_65B31C(IncRateGold)
                                            // 累计的恒是【本次实际动的钱】,接收者恒是【单个城堡对象 [[0x7D6214]]】
                                            // (静态值 0x7DC2C0,运行期填对象指针——不是 CastleManager 列表)。
                                            // sub_65B31C 全 CODE 段仅 5 个 caller(E8 rel32 全扫描,已逐一反汇编:
                                            // 0x63ECF2 买 / 0x63F020 修 / 0x63F28E 卖 / 0x6C9EA7+0x6CA182 升级),
                                            // 全部单门单分支,且不在 1349 个 Delphi VMT 的任何槽位。
                                            // 字符串 "GetAllNpcTax"/"UpgradeWeaponPrice" 镜像内各 0 hits。
                                            // => 曾按 ref(ObjNpc.pas:1982) 加的 `else if (boGetAllNpcTax) →
                                            //    CastleManager.IncRateGold(nUpgradeWeaponPrice)` 回退分支在战神
                                            //    不存在(发明出来的钱路),已删除:原生无城主时【不累计任何税】。
                                            //    ref-MIR2/GameOfMir 是另一个 Mir2 分支,非战神,仅算术形态线索。
                                            if (m_boCastle && m_Castle != null)
                                            {
                                                m_Castle.IncRateGold(nPrice);
                                            }
                                            PlayObject.SendAddItem(UserItem);

                                            M2Share.AddNativeGameDataLog(
                                                PlayObject, 0x09, StdItem.Name,
                                                UserItem.MakeIndex, 1,
                                                m_sCharName);
                                            List20.RemoveAt(j);
                                            MarkNativeGoodsDirty();
                                            if (List20.Count <= 0)
                                            {
                                                m_GoodsList.RemoveAt(i);
                                            }
                                            if (!isStaticGoods)
                                            {
                                                RemoveShopDetailHandle(PlayObject, nInt);
                                            }
                                            n1C = 0;
                                        }
                                        else
                                        {
                                            n1C = 2;
                                        }
                                    }
                                    else
                                    {
                                        n1C = 3;
                                    }
                                    bo29 = true;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        n1C = 2;
                    }
                }
            }
            if (n1C == 0)
            {
                PlayObject.SendMsg(this, Grobal2.RM_BUYITEM_SUCCESS, 0, PlayObject.m_nGold, nInt, 0, "");
            }
            else
            {
                PlayObject.SendMsg(this, Grobal2.RM_BUYITEM_FAIL, 0, n1C, 0, 0, "");
            }
        }

        public void ClientGetDetailGoodsList(TPlayObject PlayObject, string sItemName, int nInt)
        {
            int nItemCount;
            IList<TUserItem> List20;
            TStdItem StdItem = null;
            TOClientItem OClientItem = new TOClientItem();
            var sSendMsg = string.Empty;
            GoodItem Item;
            TUserItem UserItem;
            ClearShopDetailHandles(PlayObject);
            if (PlayObject.m_nSoftVersionDateEx == 0)
            {
                nItemCount = 0;
                for (var i = 0; i < m_GoodsList.Count; i++)
                {
                    List20 = m_GoodsList[i];
                    UserItem = List20[0];
                    Item = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                    if (Item != null && Item.Name == sItemName)
                    {
                        if (List20.Count - 1 < nInt)
                        {
                            nInt = HUtil32._MAX(0, List20.Count - 10);
                        }
                        for (var j = List20.Count - 1; j >= 0; j--)
                        {
                            UserItem = List20[j];
                            var detailHandle = RegisterShopDetailHandle(PlayObject, UserItem);
                            Item.GetStandardItem(ref StdItem);
                            Item.GetItemAddValue(UserItem, ref StdItem);
                            M2Share.CopyStdItemToOStdItem(StdItem, OClientItem.Item);
                            OClientItem.Dura = UserItem.Dura;
                            OClientItem.DuraMax = (ushort)GetUserPrice(PlayObject, GetUserItemPrice(UserItem));
                            OClientItem.MakeIndex = detailHandle;
                            sSendMsg = sSendMsg + EDcode.EncodeBuffer(OClientItem) + '/';
                            nItemCount++;
                            if (nItemCount >= 10)
                            {
                                break;
                            }
                        }
                        break;
                    }
                }
                PlayObject.SendMsg(this, Grobal2.RM_SENDDETAILGOODSLIST, 0,
                    ObjectId, nItemCount, nInt, string.Empty,
                    HUtil32.GetBytes(sSendMsg));
            }
            else
            {
                nItemCount = 0;
                using var detailBody = new MemoryStream();
                for (var i = 0; i < m_GoodsList.Count; i++)
                {
                    List20 = m_GoodsList[i];
                    if (List20.Count <= 0)
                    {
                        continue;
                    }
                    UserItem = List20[0];
                    Item = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                    if (Item != null && Item.Name == sItemName)
                    {
                        if (List20.Count - 1 < nInt)
                        {
                            nInt = HUtil32._MAX(0, List20.Count - 10);
                        }
                        for (var j = List20.Count - 1; j >= 0; j--)
                        {
                            UserItem = List20[j];
                            var detailHandle = RegisterShopDetailHandle(PlayObject, UserItem);
                            var detailItem = new TUserItem(UserItem)
                            {
                                DuraMax = (ushort)GetUserPrice(PlayObject,
                                    GetUserItemPrice(UserItem)),
                                ClientItemID = detailHandle
                            };
                            var record = TPlayObject.EncodeClientItemRecord(detailItem);
                            detailBody.Write(record, 0, record.Length);
                            nItemCount++;
                            if (nItemCount >= 10)
                            {
                                break;
                            }
                        }
                        break;
                    }
                }
                PlayObject.SendMsg(this, Grobal2.RM_SENDDETAILGOODSLIST, 0,
                    ObjectId, nItemCount, nInt, string.Empty, detailBody.ToArray());
            }
        }

        public void ClientQuerySellPrice(TPlayObject PlayObject, TUserItem UserItem)
        {
            var nC = GetSellItemPrice(GetUserItemPrice(UserItem));
            if (nC > 0)
            {
                PlayObject.SendMsg(this, Grobal2.RM_SENDBUYPRICE, 0, nC, 0, 0, "");
            }
            else
            {
                PlayObject.SendMsg(this, Grobal2.RM_SENDBUYPRICE, 0, 0, 0, 0, "");
            }
        }

        private int GetSellItemPrice(double nPrice)
        {
            // ✅ 战神字节证据 (Tier-1)。卖价 = GetUserItemPrice **div 2**(向零截断),不是 Round(/2.0)。
            // EA: sub_63F200 @0x63F22E-0x63F23E:
            //   call sub_63F380(GetUserItemPrice) ; mov esi,eax ;
            //   sar esi,1 ; jns +3 ; adc esi,0    ; <== Delphi `div 2` 的标准代码生成(负数向零修正)
            //   jle 0x63F315                      ; <=0 -> 失败
            // `HUtil32.Round(nPrice/2.0)` 是银行家舍入,在【奇数价】上多付 1 金:
            //   n=7 → C# 4 / 原生 3;n=11 → C# 6 / 原生 5;n=3 → C# 2 / 原生 1(n=5/9 恰好同值)。
            // 每笔卖出都走这条路,故是全服系统性偏高。改为整数截断除 2。
            // 另: 原生卖出侧 sub_63F200 @0x63F22E `call GetUserItemPrice` → @0x63F235 `sar esi,1`
            // (+ jns/adc 负数向零修正 = div 2),全函数【不读】费率字段 +0x468(全扫无 fild/fmul/fdiv),
            // 即卖价不吃 rate/rebate —— 与本函数一致。
            // ECON §4.18 二元权威并回【已完成】: 原生只有唯一费率字段 +0x468(= m_nPriceRate),
            // 原 C# 误拆出 m_nRebate 并在此再乘一次(违 §4.18)。现 m_nRebate 已并回 m_nPriceRate
            // (setrebate 改写它,见 PasApiBridge.cs),故下方多余的 m_nRebate 乘法整体删除。
            var result = (int)nPrice / 2;
            return result;
        }

        private bool ClientSellItem_sub_4A1C84(TUserItem UserItem)
        {
            var result = true;
            var StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
            // ECON sell gate sub_63F194 @0x63F1B8-0x63F1D4 (byte-verified): the durability
            // floor applies to StdMode 25 (`cmp al,0x19`) OR StdMode 30 (`cmp al,0x1e`);
            // `cmp word [item+0x26],0xFA0 / jae` then `xor ebx,ebx` rejects Dura < 4000.
            // Prior code only checked StdMode 25, so right-hand/torch items (StdMode 30)
            // could be sold at half durability.
            if (StdItem != null && (StdItem.StdMode == 25 || StdItem.StdMode == 30))
            {
                if (UserItem.Dura < 4000)
                {
                    result = false;
                }
            }
            // ECON sell transfer gate sub_63F194 @0x63F1DC-0x63F1F6:
            //   mov cl,[merchant+0x4B7] / mov edx,1 / call 0x78389C
            //   test eax,eax / jle pass / xor ebx,ebx (reject)
            // mode 1 body @0x783901: test byte [std+3],1 → reject code 2.
            if (result && StdItem != null
                && NativeItemDropDestroy.CheckTransferPermission(
                    UserItem, StdItem, NativeItemDropDestroy.TransferModeSell) > 0)
            {
                result = false;
            }
            return result;
        }

        public bool ClientSellItem(TPlayObject PlayObject, TUserItem UserItem)
        {
            var result = false;
            GoodItem StdItem;
            var nPrice = GetSellItemPrice(GetUserItemPrice(UserItem));
            if (nPrice > 0 && PlayObject.NativeMerchantSellEquipLockGate()
                && ClientSellItem_sub_4A1C84(UserItem))
            {
                if (PlayObject.IncGold(nPrice))
                {
                    // ✅ 战神字节证据 (Tier-1)。卖出税点 sub_63F200 @0x63F27C-0x63F28E:
                    //   call dword [ecx+0x28C](IncGold 虚派发) ; test al,al ; je 0x63F315 ;
                    //   cmp byte [edi+0x578],0 ; je 0x63F293      <== 唯一门,无 else
                    //   mov eax,[0x7D6214] ; mov eax,[eax] ; mov edx,esi   ; <== 本次成交价(半价)
                    //   call sub_65B31C(IncRateGold)
                    // sub_65B31C 全 CODE 段仅 5 个 caller(全扫描+逐一反汇编),全部单门单分支,
                    // 接收者恒是【单个城堡对象 [[0x7D6214]]】,不存在"遍历城堡列表"的第二形态。
                    // => 曾按 ref(ObjNpc.pas:2176) 加的 `else if (boGetAllNpcTax) →
                    //    CastleManager.IncRateGold(nUpgradeWeaponPrice)` 回退分支在战神不存在,已删除
                    //    (原生无城主时不累计任何税);"GetAllNpcTax" 字符串镜像内 0 hits。
                    //    ref-MIR2/GameOfMir 是另一个 Mir2 分支,非战神,仅算术形态线索。
                    if (m_boCastle && m_Castle != null)
                    {
                        m_Castle.IncRateGold(nPrice);
                    }
                    PlayObject.SendMsg(this, Grobal2.RM_USERSELLITEM_OK, 0, PlayObject.m_nGold, 0, 0, "");
                    // 0x63F2B5..0x63F2CF: the worker performs its own fresh order-4 query.
                    // Rejection skips merchant ownership but does not undo the completed sale.
                    if (PlayObject.NativeMerchantSellAuthenticated())
                    {
                        AddItemToGoodsList(UserItem);
                        MarkNativeGoodsDirty();
                    }
                    StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                    // 0x63F2D4..0x63F304 has no NeedIdentify gate.
                    M2Share.AddNativeGameDataLog(PlayObject, 0x0A,
                        StdItem.Name, UserItem.MakeIndex, 1, m_sCharName);
                    result = true;
                    // Native worker 0x63F30E runs this before sub_6B9220 removes the bag slot.
                    PlayObject.WeightChanged();
                }
                else
                {
                    PlayObject.SendMsg(this, Grobal2.RM_USERSELLITEM_FAIL, 0, 0, 0, 0, "");
                }
            }
            else
            {
                PlayObject.SendMsg(this, Grobal2.RM_USERSELLITEM_FAIL, 0, 0, 0, 0, "");
            }
            return result;
        }

        internal bool RemoveExactGoodsReference(TUserItem userItem)
        {
            if (userItem == null)
            {
                return false;
            }
            for (var i = 0; i < m_GoodsList.Count; i++)
            {
                var items = m_GoodsList[i];
                for (var j = 0; j < items.Count; j++)
                {
                    if (!ReferenceEquals(items[j], userItem))
                    {
                        continue;
                    }
                    items.RemoveAt(j);
                    if (items.Count == 0)
                    {
                        m_GoodsList.RemoveAt(i);
                    }
                    MarkNativeGoodsDirty();
                    return true;
                }
            }
            return false;
        }

        private bool AddItemToGoodsList(TUserItem UserItem)
        {
            var result = false;
            if (UserItem == null)
            {
                return result;
            }
            var ItemList = GetRefillList(UserItem.wIndex);
            if (ItemList == null)
            {
                ItemList = new List<TUserItem>();
                m_GoodsList.Add(ItemList);
            }
            ItemList.Insert(0, UserItem);
            result = true;
            return result;
        }

        // 原生 sub_74089C -> sub_7408D0：按 stdIdx 数背包，堆叠物加整堆数量而不是加 1。
        //   0x74090E  80 78 14 07  cmp byte [eax+0x14], 7
        //   0x740914  0FB74026     movzx eax, word [eax+0x26]   ; 堆叠：加 Dura
        //   0x74091D  FF45F8       inc [ebp-8]                  ; 非堆叠：加 1
        private static int NativeCountOwnedByStdIdx(TPlayObject PlayObject, int nStdIdx)
        {
            var result = 0;
            for (var i = 0; i < PlayObject.m_ItemList.Count; i++)
            {
                var UserItem = PlayObject.m_ItemList[i];
                if (UserItem == null || UserItem.wIndex != nStdIdx)
                {
                    continue;
                }
                if (NativeItemFactory.IsPileItem(M2Share.UserEngine.GetStdItem(UserItem.wIndex)))
                {
                    result += UserItem.Dura;
                }
                else
                {
                    result += 1;
                }
            }
            return result;
        }

        private bool ClientMakeDrugItem_sub_4A28FC(TPlayObject PlayObject, string sItemName)
        {
            // 原生 sub_6C4BCC。ok 标志 0x6C4BEE mov byte [ebp-0x19],0 在两趟循环【之前】置 0，
            // 0x6C4C28 才在 pass1 循环体【顶部】置 1 —— 配方节为空时 0x6C4C1B jl 0x6C4C64
            // 直接跳过循环，ok 保持 0，返回失败。
            bool result = false;
            IList<TMakeItem> List10 = M2Share.GetMakeItemInfo(sItemName);
            TUserItem UserItem = null;
            IList<TDeleteItem> List28;
            string s20 = string.Empty;
            int n1C = 0;
            if (List10 == null)
            {
                return result;
            }
            for (var i = 0; i < List10.Count; i++)
            {
                result = true;
                s20 = List10[i].ItemName;
                n1C = List10[i].ItemCount;
                // 0x6C4C52 cmp edi,eax / 0x6C4C54 jle：need > have 才失败并 break
                if (n1C > NativeCountOwnedByStdIdx(PlayObject, M2Share.UserEngine.GetStdItemIdx(s20)))
                {
                    result = false;
                    break;
                }
            }
            if (result)
            {
                List28 = null;
                for (var i = 0; i < List10.Count; i++)
                {
                    s20 = List10[i].ItemName;
                    n1C = List10[i].ItemCount;
                    // 0x6C4CC1 call 0x74C1E0：每条配方行把名字解析成一个 stdIdx，
                    // 0x6C4CF1 movzx eax,word[esi+0x24] / 0x6C4CF5 cmp 只比索引不比名字。
                    var nStdIdx = M2Share.UserEngine.GetStdItemIdx(s20);
                    for (var j = PlayObject.m_ItemList.Count - 1; j >= 0; j--)
                    {
                        if (n1C <= 0)
                        {
                            break;
                        }
                        UserItem = PlayObject.m_ItemList[j];
                        if (UserItem.wIndex == nStdIdx)
                        {
                            if (List28 == null)
                            {
                                List28 = new List<TDeleteItem>();
                            }
                            List28.Add(new TDeleteItem()
                            {
                                sItemName = s20,
                                MakeIndex = UserItem.MakeIndex,
                                ClientItemID = PlayObject.EnsureClientItemId(UserItem)
                            });
                            Dispose(UserItem);
                            PlayObject.m_ItemList.RemoveAt(j);
                            n1C -= 1;
                        }
                    }
                    // 0x6C4D2C mov byte [ebp-0x19],0 后落到 0x6C4D30 inc [ebp-8]：置失败但【不 break】，
                    // 后续配方行照样被消耗。
                    if (n1C > 0)
                    {
                        result = false;
                    }
                }
                // 0x6C4D3C cmp byte [ebp-0x19],0 / 0x6C4D40 je 0x6C4D6B：缺料时删除包不发。
                if (result && List28 != null)
                {
                    PlayObject.SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0,
                        List28.Count, 0, 0, "", List28);
                }
            }
            return result;
        }

        public void ClientMakeDrugItem(TPlayObject PlayObject, string sItemName)
        {
            IList<TUserItem> List1C;
            TUserItem MakeItem;
            TUserItem UserItem;
            GoodItem StdItem;
            // 原版货架循环体 0x63FE78..0x63FFA4，四个出口全部 jmp 0x63FFAA 跳出：
            //   0x63FF7E  EB 2A                    result 0 (成功)
            //   0x63FF8E  EB 1A                    result 2 (背包满，先 call 0x404690 释放)
            //   0x63FF97  EB 11                    result 4 (材料不足)
            //   0x63FFA0  EB 08                    result 3 (金币不足)
            // 只有「本条货架不匹配」才落到 0x63FFA2 inc esi / dec edi / jne 继续扫。
            // 之前只有成功路径 break：失败后继续扫，遇到第二条同名货架会把材料再扣
            // 一次（材料在 ClientMakeDrugItem_sub_4A28FC 里已经消耗掉了）。
            var n14 = 1;
            for (var i = 0; i < m_GoodsList.Count; i++)
            {
                List1C = m_GoodsList[i];
                // 0x63FE93 cmp [ebp-0x14],0 / je 0x63FFA2、0x63FEA0 cmp [group+0x10],0 / je、
                // 0x63FEB0 cmp [inner+8],0 / jle —— 三道守卫都跳到 0x63FFA2 继续扫下一条货，
                // 不是中止整个 1034。C# 把 group 与 inner 合并成一层，两道并成一道。
                if (List1C == null || List1C.Count <= 0)
                {
                    continue;
                }
                MakeItem = List1C[0];
                StdItem = M2Share.UserEngine.GetStdItem(MakeItem.wIndex);
                if (StdItem != null && StdItem.Name == sItemName)
                {
                    if (PlayObject.m_nGold >= M2Share.g_Config.nMakeDurgPrice)
                    {
                        if (ClientMakeDrugItem_sub_4A28FC(PlayObject, sItemName))
                        {
                            UserItem = new TUserItem();
                            M2Share.UserEngine.CopyToUserItemFromName(sItemName, ref UserItem);
                            if (PlayObject.AddItemToBag(UserItem))
                            {
                                PlayObject.SendAddItem(UserItem);
                                PlayObject.m_nGold -= M2Share.g_Config.nMakeDurgPrice;
                                // 0x63FF42 call 0x6C7D64（扣金核心）：0x6C7D75 sub [self+0x15c],edx 之后
                                // 0x6C7D7B call 0x6C19B4，后者 0x6C19C3 mov cx,0x2798 / 0x6C19C9 call
                                // 0x765E68 = SendMsg(self, RM_GOLDCHANGED)。原生每次成功扣金都自投这条
                                // 金币刷新，且与 0x63FFCE 的 RM_MAKEDRUG_SUCCESS 同走 0x765E68 追加队列，
                                // 故排在成功包之前。此处沿用同一 SendMsg 原语以保持相对顺序。
                                PlayObject.SendMsg(PlayObject, Grobal2.RM_GOLDCHANGED, 0, 0, 0, 0, "");
                                StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                                // 0x63FF74 call 0x768BE0 无条件执行（不看 NeedIdentify），
                                // 0x63FF6F xor edx,edx 给出 type = 0，末列 0x63FF53 add edx,0x106
                                // 取的是【玩家自己】的 ShortString 名字。
                                M2Share.AddGameDataLog('0' + "\t" + PlayObject.m_sMapName + "\t" + PlayObject.m_nCurrX + "\t" + PlayObject.m_nCurrY + "\t" + PlayObject.m_sCharName + "\t" + StdItem.Name + "\t" + UserItem.MakeIndex + "\t" + '1' + "\t" + PlayObject.m_sCharName);
                                n14 = 0;
                                break;
                            }
                            else
                            {
                                DisPose(UserItem);
                                n14 = 2;
                                break;
                            }
                        }
                        else
                        {
                            n14 = 4;
                            break;
                        }
                    }
                    else
                    {
                        n14 = 3;
                        break;
                    }
                }
            }
            if (n14 == 0)
            {
                PlayObject.SendMsg(this, Grobal2.RM_MAKEDRUG_SUCCESS, 0, PlayObject.m_nGold, 0, 0, "");
            }
            else
            {
                PlayObject.SendMsg(this, Grobal2.RM_MAKEDRUG_FAIL, 0, n14, 0, 0, "");
            }
        }

        
        
        
        
        
        private int GetNativeRepairPrice(TPlayObject PlayObject,
            TUserItem UserItem, GoodItem StdItem)
        {
            if (StdItem == null)
            {
                return -1;
            }

            if (PlayObject.m_btNativeRepairMode == 3)
            {
                // sub_6402BC/sub_63EE9C mode 3: raw template price, signed
                // durability delta and a float32 1000.0 divisor. No rate or Abs.
                if (!CheckItemType(StdItem.StdMode))
                {
                    return -1;
                }
                return Math.Max(-1, HUtil32.RoundX87DivideThenMultiply(
                    (int)UserItem.DuraMax - UserItem.Dura, 1000,
                    StdItem.Price));
            }

            var nPrice = GetUserPrice(PlayObject, GetUserItemPrice(UserItem));
            if (nPrice <= 0)
            {
                return -1;
            }

            int nRepairPrice;
            if (UserItem.DuraMax > 0)
            {
                // PRICE-21 @0x63EF92-0x63EFCC: integer /3, x87 divide
                // by live max, x87 multiply by Abs(delta), then @ROUND.
                nRepairPrice = HUtil32.RoundX87DivideThenMultiply(
                    nPrice / 3, UserItem.DuraMax, Math.Abs(
                        (int)UserItem.DuraMax - UserItem.Dura));
            }
            else
            {
                nRepairPrice = nPrice;
            }

            if (PlayObject.m_btNativeRepairMode == 2)
            {
                // 0x63EFDF/0x64039C: hardcoded post-Round x3.
                nRepairPrice = unchecked(nRepairPrice * 3);
            }
            return nRepairPrice;
        }

        public void ClientQueryRepairCost(TPlayObject PlayObject, TUserItem UserItem)
        {
            var StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
            var nRepairPrice = GetNativeRepairPrice(PlayObject, UserItem, StdItem);
            PlayObject.SendMsg(this, Grobal2.RM_SENDREPAIRCOST,
                0, nRepairPrice, 0, 0, string.Empty);
        }

        
        
        
        
        
        
        public bool ClientRepairItem(TPlayObject PlayObject, TUserItem UserItem)
        {
            if (PlayObject.m_sScriptLable == "@fail_s_repair")
            {
                SendMsgToUser(PlayObject, "对不起!我不能帮你修理这个物品。\\ \\ \\<返回/@main>");
                PlayObject.SendMsg(this, Grobal2.RM_USERREPAIRITEM_FAIL, 0, 0, 0, 0, "");
                return false;
            }
            GoodItem StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
            var repairMode = PlayObject.m_btNativeRepairMode;
            if (StdItem == null ||
                !NativeRepairEligibility.CanExecute(UserItem, StdItem, repairMode))
            {
                PlayObject.SendMsg(this, Grobal2.RM_USERREPAIRITEM_FAIL,
                    0, 0, 0, 0, string.Empty);
                return false;
            }

            var nRepairPrice = GetNativeRepairPrice(PlayObject, UserItem, StdItem);
            // 0x63EFE4 checks positive cost, then 0x63EFF2 rejects StdMode
            // 43, and only then reaches DecGold.
            if (nRepairPrice <= 0 || StdItem.StdMode == 43 ||
                !PlayObject.DecGold(nRepairPrice))
            {
                PlayObject.SendMsg(this, Grobal2.RM_USERREPAIRITEM_FAIL,
                    0, 0, 0, 0, string.Empty);
                return false;
            }

            // 修理税点 sub_63EE9C @0x63F00E-0x63F020 uses the actual
            // amount and has no fallback when this merchant is not a castle NPC.
            if (m_boCastle && m_Castle != null)
            {
                m_Castle.IncRateGold(nRepairPrice);
            }

            if (repairMode is 2 or 3)
            {
                UserItem.Dura = UserItem.DuraMax;
                // 0x63F044..0x63F0A4: the completion callback runs first;
                // the success packet then re-reads gold and durability.
                GotoLable(PlayObject, M2Share.sSUPERREPAIROK, false);
                PlayObject.SendMsg(this, Grobal2.RM_USERREPAIRITEM_OK,
                    0, PlayObject.m_nGold, UserItem.Dura, UserItem.DuraMax,
                    string.Empty);
            }
            else
            {
                if (UserItem.Dura < UserItem.DuraMax)
                {
                    UserItem.DuraMax -= (ushort)((UserItem.DuraMax - UserItem.Dura) / 30);
                }
                UserItem.Dura = UserItem.DuraMax;
                GotoLable(PlayObject, M2Share.sREPAIROK, false);
                PlayObject.SendMsg(this, Grobal2.RM_USERREPAIRITEM_OK,
                    0, PlayObject.m_nGold, UserItem.Dura, UserItem.DuraMax,
                    string.Empty);
            }
            return true;
        }

        public override void ClearScript()
        {
            // 注意：不重置商店功能标志 (m_boBuy, m_boSell, m_boRepair 等)
            // 这些标志应该在 OnInitialize 或 OpenXXX API 中设置，并在整个会话期间保持有效
            // 如果在这里重置，会导致玩家打开商店界面后，查询价格和交易操作失败

            // m_boBuy = false;
            // m_boSell = false;
            // m_boMakeDrug = false;
            // m_boPrices = false;
            // m_boStorage = false;
            // m_boGetback = false;
            // m_boUpgradenow = false;
            // m_boGetBackupgnow = false;
            // m_boRepair = false;
            // m_boS_repair = false;

            m_boGetMarry = false;
            m_boGetMaster = false;
            m_boUseItemName = false;

            // PasEngine: clear persistent NPC script state on reload to prevent state leak
            M2Share.PasEngine?.ClearNpcState(this);

            base.ClearScript();
        }

        
        
        
        
        
        protected void SetOffLineMsg(TPlayObject PlayObject, string sMsg)
        {
            PlayObject.m_sOffLineLeaveword = sMsg;
        }

        protected override void SendCustemMsg(TPlayObject PlayObject, string sMsg)
        {
            base.SendCustemMsg(PlayObject, sMsg);
        }

        
        
        
        public void ClearData()
        {
            TUserItem UserItem;
            IList<TUserItem> ItemList;
            TItemPrice ItemPrice;
            const string sExceptionMsg = "[Exception] TMerchant::ClearData";
            try
            {
                for (var i = 0; i < m_GoodsList.Count; i++)
                {
                    ItemList = m_GoodsList[i];
                    for (var j = 0; j < ItemList.Count; j++)
                    {
                        UserItem = ItemList[j];
                        Dispose(UserItem);
                    }
                }
                m_GoodsList.Clear();
                for (var i = 0; i < m_ItemPriceList.Count; i++)
                {
                    ItemPrice = m_ItemPriceList[i];
                    Dispose(ItemPrice);
                }
                m_ItemPriceList.Clear();
            }
            catch (Exception e)
            {
                M2Share.ErrorMessage(sExceptionMsg);
                M2Share.ErrorMessage(e.Message);
            }
        }

        private void ChangeUseItemName(TPlayObject PlayObject, string sLabel, string sItemName)
        {
            if (!PlayObject.m_boChangeItemNameFlag)
            {
                return;
            }
            PlayObject.m_boChangeItemNameFlag = false;
            var sWhere = sLabel.Substring(M2Share.sUSEITEMNAME.Length, sLabel.Length - M2Share.sUSEITEMNAME.Length);
            var btWhere = (byte)HUtil32.Str_ToInt(sWhere, -1);
            if (btWhere >= PlayObject.m_UseItems.GetLowerBound(0) && btWhere <= PlayObject.m_UseItems.GetUpperBound(0))
            {
                var UserItem = PlayObject.m_UseItems[btWhere];
                if (UserItem.wIndex == 0)
                {
                    var sMsg = format(M2Share.g_sYourUseItemIsNul, M2Share.GetUseItemName(btWhere));
                    PlayObject.SendMsg(this, Grobal2.RM_MENU_OK, 0, PlayObject.ObjectId, 0, 0, sMsg);
                    return;
                }
                if (UserItem.btValue[13] == 1)
                {
                    M2Share.ItemUnit.DelCustomItemName(UserItem.MakeIndex, UserItem.wIndex);
                }
                if (!string.IsNullOrEmpty(sItemName))
                {
                    M2Share.ItemUnit.AddCustomItemName(UserItem.MakeIndex, UserItem.wIndex, sItemName);
                    UserItem.btValue[13] = 1;
                }
                else
                {
                    M2Share.ItemUnit.DelCustomItemName(UserItem.MakeIndex, UserItem.wIndex);
                    UserItem.btValue[13] = 0;
                }
                M2Share.ItemUnit.SaveCustomItemName();
                PlayObject.SendMsg(PlayObject, Grobal2.RM_SENDUSEITEMS, 0, 0, 0, 0, "");
                PlayObject.SendMsg(this, Grobal2.RM_MENU_OK, 0, PlayObject.ObjectId, 0, 0, "");
            }
        }

        private void DisPose(object obj)
        {
            obj = null;
        }
    }
}
