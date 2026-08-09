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
                SendDefMessage(Grobal2.SM_MERCHANT_QUERY,
                    ObjectId, 0, 0, 23, "请输入密码宝开启斗转箱");
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
            if (m_nPayMent == 1)
            {
                SendNativeAccountStorageFailure(Grobal2.SM_STORAGE_FAIL, 0);
                return;
            }

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

            state.Items.Add(item);
            state.Dirty = true;
            m_ItemList.RemoveAt(itemIndex);
            WeightChanged();
            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_STORAGE_OK,
                EnsureClientItemId(item), 0, 0, 1);
            SendSocket(m_DefMsg, EncodeOwnedClientItemRecord(item));
            LogNativeAccountStorageItem(item, stdItem, '1');
        }

        internal void ClientNativeAccountTakeBackStorageItem(
            int objectId, int clientItemId)
        {
            if (m_boPasswordLocked)
            {
                SendNativeAccountStorageFailure(
                    Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL, -3);
                return;
            }
            if (m_nPayMent == 1)
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
            var itemIndex = -1;
            for (var i = 0; i < state.Items.Count; i++)
            {
                if (!ClientItemIdMatches(state.Items[i], clientItemId))
                    continue;
                itemIndex = i;
                break;
            }

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
            state.Items.RemoveAt(itemIndex);
            state.Dirty = true;
            SendDefMessage(Grobal2.SM_TAKEBACKSTORAGEITEM_OK,
                clientItemId, 0, 0, 1, "");
            LogNativeAccountStorageItem(item, stdItem, '2');
        }

        internal void RejectUnsupportedStorageItem(int series)
        {
            SendDefMessage(Grobal2.SM_STORAGE_FAIL, 0, 0, 0, series, "");
        }

        internal void RejectUnsupportedTakeBackStorageItem(int series)
        {
            SendDefMessage(Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL,
                0, 0, 0, series, "");
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
            TUserItem item, GoodItem stdItem, char action)
        {
            if (stdItem == null) return;
            var quantity = NativeAccountStorageClient.GetGameDataLogQuantity(
                stdItem, item);
            M2Share.AddGameDataLog(action + "\t" + m_sMapName + "\t"
                + m_nCurrX + "\t" + m_nCurrY + "\t" + m_sCharName + "\t"
                + stdItem.Name + "\t" + item.MakeIndex + "\t" + quantity
                + "\t账号仓库");
        }
    }
}
