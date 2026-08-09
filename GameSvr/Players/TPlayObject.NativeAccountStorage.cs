using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private NativeAccountStorageState _nativeAccountStorageState;

        internal NativeAccountStorageState GetNativeAccountStorageState() =>
            _nativeAccountStorageState ??= new NativeAccountStorageState();

        internal void PublishNativeAccountStorage(
            NativeAccountStorageState state, int baseObject = 0)
        {
            if (state == null) return;
            using var stream = new MemoryStream();
            var itemCount = 0;
            foreach (var item in state.Items)
            {
                if (item == null || item.wIndex == 0
                                 || M2Share.UserEngine?.GetStdItem(item.wIndex)
                                 == null)
                    continue;
                var record = EncodeOwnedClientItemRecord(item);
                stream.Write(record, 0, record.Length);
                itemCount++;
                if (itemCount >= 300) break;
            }

            m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_SAVEITEMLIST,
                baseObject, itemCount, state.Capacity, 1);
            SendSocket(m_DefMsg, stream.ToArray());
        }
    }
}
