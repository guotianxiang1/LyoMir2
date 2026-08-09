using DBSvr.Core;
using System.Collections.Generic;

namespace DBSvr
{
    /// <summary>
    /// 英雄存档服务接口 (对应 hero_data 表)。
    /// Index=hero_index.Idx, 存储 Data + dynData 两个 Blob。
    /// </summary>
    public interface IHeroDataService
    {
        void LoadQuickList();
        int Index(int idx);
        (byte[] data, byte[] dynData) LoadBlob(int idx);
        bool SaveBlob(int idx, byte[] data, byte[] dynData = null);
        bool SaveRecord(int idx, byte[] record, byte[] dynData = null,
            bool setConsignation = false, bool setDelete = false);
        NativeHeroSaveResult SaveRecordDetailed(int idx, byte[] record,
            byte[] preparedData, byte[] dynData, bool isDelete, int heroType,
            int consignation, int indexJob, ushort? forceLevelOverride,
            bool exactPrepared = false);
        NativeForceLevelStoreAttempt ApplyNativeForceLevel(int idx,
            byte[] heroName, ushort forceLevel);
        bool TryGetNativeForceLevelOverride(int idx, out ushort forceLevel);
        void SetNativeForceLevelOverride(int idx, ushort forceLevel);
        void ClearNativeForceLevelOverride(int idx);
        NativeHeroSaveResult PersistNativeForceLevel(int idx, ushort forceLevel);
        ushort BuildThreeSlot(string masterName,
            IReadOnlyDictionary<int, NativeHeroLogicalSnapshot> logicalSnapshots,
            out string heroName,
            out NativeHeroLogicalSnapshot[] builtSnapshots);
        bool CreateDataRow(int idx, string heroName);
        void RegisterNativeIndex(int idx);
        void UnregisterNativeIndex(int idx);
        bool DeleteDataRow(int idx);
    }

    public enum NativeHeroSaveResult
    {
        Success,
        RetryableFailure,
        InvalidData
    }
}
