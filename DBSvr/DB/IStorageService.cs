using System.Collections.Generic;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 账号仓库服务接口 (对应 user_storage 表)。
    /// PTID 为账号唯一标识, Data 为压缩 Blob。
    /// </summary>
    public interface IStorageService
    {
        bool CreateStorage(string ptid);
        byte[] LoadStorage(int idx);
        byte[] LoadStorageByPtid(string ptid);
        bool SaveStorage(int idx, byte[] data);
        bool DeleteStorage(int idx);
        bool DeleteStorageByPtid(string ptid);
        bool RenamePtid(string oldPtid, string newPtid);
        (int idx, string ptid) GetStorageInfo(int idx);
        int GetMaxIdx();
        NativeAccountStorageBlobResult LoadNativeStorage(int idx);
        List<NativeStorageIndexEntry> GetNativeStoragePage(int lastIdx,
            int limit = 5000);
        int EnsureNativeStorage(byte[] account);
        bool SaveNativeStorage(int idx, byte[] data);
    }

    public sealed class NativeStorageIndexEntry
    {
        public int Index;
        public byte[] Account;
    }
}
