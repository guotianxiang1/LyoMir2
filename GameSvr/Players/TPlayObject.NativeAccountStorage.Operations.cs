using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal void OpenNativeAccountStorageForDeposit(int baseObject)
        {
            var state = GetNativeAccountStorageState();
            if (state.Capacity == -1)
                NativeAccountStorageClient.SendLoadRequest(this, 0);
            SendDefMessage(Grobal2.SM_SENDUSERSTORAGEITEM,
                baseObject, 0, 0, 1, "");
        }

        internal void OpenNativeAccountStorageForRetrieval(int baseObject)
        {
            if (m_boPasswordLocked)
            {
                // 战神 sub_6C1860, mode-2 arm (mode 1 is the 仓库 twin at 0x6C189F):
                //   006C18BF  6A 17              push 0x17        ; Param  = 23
                //   006C18C1  6A 00              push 0           ; Tag    = 0
                //   006C18C3  6A 00              push 0           ; Series = 0
                //   006C18C5  68 9C 19 6C 00     push 0x6C199C    ; sMsg
                //   006C18CA  8B CB              mov ecx,ebx      ; nRecog = Self
                //   006C18CC  66 BA 0F 0B        mov dx,0xB0F     ; ident  = 2831
                //   006C18D4  FF 93 50 02 00 00  call [ebx+0x250]
                // 23 was going out in Series with Param zero. And the literal at 0x6C199C
                // has declen 20 = ten GBK chars,
                //   c7eb cae4 c8eb c3dc b1a6 bfaa c6f4 b6b7 d7aa cfe4
                // = 请输入密宝开启斗转箱. There is no 码 in it; the eleven-char version
                // here was one character too long.
                SendDefMessage(Grobal2.SM_MERCHANT_QUERY,
                    ObjectId, 23, 0, 0, "请输入密宝开启斗转箱");
                return;
            }

            var state = GetNativeAccountStorageState();
            if (state.Capacity == -1)
            {
                NativeAccountStorageClient.SendLoadRequest(this, 1);
                return;
            }
            PublishNativeAccountStorage(state, baseObject);
        }

        internal bool AddNativeAccountStorageCapacity(int delta)
        {
            var state = GetNativeAccountStorageState();
            if (state.Capacity == -1)
            {
                NativeAccountStorageClient.SendLoadRequest(this, 0);
                return false;
            }
            if (!NativeAccountStorageClient.TryChangeCapacity(state, delta))
                return false;
            if (!m_boPasswordLocked)
                PublishNativeAccountStorage(state);
            return true;
        }

        internal int GetNativeAccountStorageCapacity() =>
            GetNativeAccountStorageState().Capacity;

        internal bool SaveNativeAccountStorageIfDirty() =>
            NativeAccountStorageClient.SendDirtySave(this);

        internal void ClientNativeAccountStorageItem(
            int objectId, int clientItemId)
        {
            var state = GetNativeAccountStorageState();
            if (state.Capacity == -1) return;

            if (!IsNativeAccountStorageNpc(objectId))
            {
                SendNativeAccountStorageFailure(Grobal2.SM_STORAGE_FAIL, 0);
                return;
            }

            var itemIndex = -1;
            for (var i = m_ItemList.Count - 1; i >= 0; i--)
            {
                if (!ClientItemIdMatches(m_ItemList[i], clientItemId))
                    continue;
                itemIndex = i;
                break;
            }

            if (itemIndex < 0)
            {
                SendNativeAccountStorageFailure(Grobal2.SM_STORAGE_FAIL, 0);
                return;
            }

            var item = m_ItemList[itemIndex];
            var stdItem = M2Share.UserEngine?.GetStdItem(item.wIndex);
            if (NativeAccountStorageClient.IsDepositRestricted(stdItem, item))
            {
                SendNativeAccountStorageFailure(Grobal2.SM_STORAGE_FAIL, 0);
                return;
            }
            if (state.Items.Count + 1 > state.Capacity)
            {
                SendDefMessage(Grobal2.SM_STORAGE_FULL, 0, 0, 0, 1, "");
                SendNativeAccountStorageFailure(Grobal2.SM_STORAGE_FAIL, 0);
                return;
            }

            state.Dirty = true;
            state.Items.Add(item);
            m_ItemList.RemoveAt(itemIndex);
            WeightChanged();
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_STORAGE_OK,
                EnsureClientItemId(item), 0, 0, 1);
            SendSocket(m_DefMsg, EncodeOwnedClientItemRecord(item));
            LogNativeAccountStorageItem(item, stdItem, 0x01);
        }

        internal void ClientNativeAccountTakeBackStorageItem(
            int objectId, int clientItemId)
        {
            // sub_6C2D7C applies these two player gates before dispatching to
            // personal/account/drug storage. The managed allow flag is the inverse
            // of native byte [+0x683].
            if (!m_boCanGetBackItem)
            {
                SendNativeAccountStorageFailure(
                    Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL, -3);
                return;
            }
            if (m_boDealing)
            {
                SendNativeAccountStorageFailure(
                    Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL, -2);
                return;
            }

            if (!IsNativeAccountStorageNpc(objectId))
            {
                SendNativeAccountStorageFailure(
                    Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL, 0);
                return;
            }

            var state = GetNativeAccountStorageState();
            var itemIndex = FindNativeStorageItemIndex(state.Items, clientItemId);

            if (itemIndex < 0)
            {
                SendNativeAccountStorageFailure(
                    Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL, 0);
                return;
            }

            var item = state.Items[itemIndex];
            var stdItem = M2Share.UserEngine?.GetStdItem(item.wIndex);
            if (stdItem == null)
            {
                SendNativeAccountStorageFailure(
                    Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL, 0);
                return;
            }
            if (!IsAddWeightAvailable(stdItem.Weight))
            {
                SendNativeAccountStorageFailure(
                    Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL, -1);
                return;
            }
            if (!AddItemToBag(item))
            {
                SendDefMessage(Grobal2.SM_TAKEBACKSTORAGEITEM_FULLBAG,
                    0, 0, 0, 1, "");
                SendNativeAccountStorageFailure(
                    Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL, 0);
                return;
            }

            SendAddItem(item);
            state.Dirty = true;
            state.Items.RemoveAt(itemIndex);
            SendDefMessage(Grobal2.SM_TAKEBACKSTORAGEITEM_OK,
                clientItemId, 0, 0, 1, "");
            LogNativeAccountStorageItem(item, stdItem, 0x02);
        }

        // TRADE-35: series 2 = 药品仓库 / DrugStore（原生容器 [player+0x6D8]，构造 0x74a7f4，
        // 名字串 0x6c2d70 "药品仓库"）。它与账号仓（[+0x6D4]）一样是 DBSvr 加载式容器：
        // 构造时 [obj+8]=[obj+0xc]=-1（0x74A803/0x74A80A），必须先向 LogServer/DBSvr 发
        //   InitialDrugStore / GetDrugStore / GetDrugStoreMaxCount / GetDrugStoreMaxDiffKind
        // （0x74a868：call 0x69aeb8）拿回「最大件数」+「最大种类数」双上限才算已加载
        // （开箱判据 0x74a854：两字段都 != -1）。本 C# 重写没有 DrugStore 的 DBSvr 后端，
        // 该容器永远处于「未加载」态。
        //
        // 原生「未加载」态的存仓可观测行为（**不是发失败包**）：
        //   存仓 sub_6C2A34 @0x6C2A93 mov eax,[ebx+0x6d8] / 0x6C2A99 call 0x74a854 /
        //     0x6C2AA0 je 0x6c2d15 —— 0x6c2d15 只做局部串析构 + ret，**不发任何包**（静默）。
        // 取仓 sub_6C2D7C 不调用 0x74A854；三个容器共用 +0x683、交易和失败出口。
        // DrugStore 后端未实现时，仍可严格复刻 Series=2 的 -3/-2/0 失败子集。
        // 两个方法保留签名（NativeAccountStorageCompatCheck 用其做源码切片边界）：
        // 存仓保持静默，取仓发送可证明的 fail-closed 子集。DrugStore 全量落地仍是
        // DBSvr 依赖项，见 docs/m_trade35_drugstore_20260813.md。
        internal void RejectUnsupportedStorageItem(int series)
        {
            _ = series; // DrugStore 未加载：原生 0x6c2d15 静默返回，不发 SM_STORAGE_FAIL。
        }

        internal void RejectUnsupportedTakeBackStorageItem(int series)
        {
            var status = !m_boCanGetBackItem
                ? -3
                : m_boDealing
                    ? -2
                    : 0;
            SendDefMessage(Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL,
                status, 0, 0, series, "");
        }

        private static int FindNativeStorageItemIndex(
            IList<TUserItem> items, int clientItemId)
        {
            if (items == null) return -1;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item != null && item.wIndex > 0 &&
                    item.ClientItemID == clientItemId)
                    return i;
            }
            return -1;
        }

        private bool IsNativeAccountStorageNpc(int objectId)
        {
            if (m_NPC == null
                || m_NPC.ObjectId != objectId
                || m_NPC.m_PEnvir != m_PEnvir
                || Math.Abs(m_NPC.m_nCurrX - m_nCurrX) > 15
                || Math.Abs(m_NPC.m_nCurrY - m_nCurrY) > 15)
                return false;
            return true;
        }

        private void SendNativeAccountStorageFailure(short ident, int status)
        {
            SendDefMessage(ident, status, 0, 0, 1, "");
        }

        private void LogNativeAccountStorageItem(
            TUserItem item, GoodItem stdItem, byte logType)
        {
            if (stdItem == null) return;
            var quantity = NativeAccountStorageClient.GetGameDataLogQuantity(
                stdItem, item);
            M2Share.AddNativeGameDataLog(this, logType, stdItem.Name,
                item.MakeIndex, quantity, "账号仓库");
        }
    }
}
