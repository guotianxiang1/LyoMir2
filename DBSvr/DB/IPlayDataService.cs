using SystemModule;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 角色存档服务接口 (对应 user_data 表)。
    /// 负责角色完整游戏数据的 Blob 读写、压缩/解压。
    /// </summary>
    public interface IPlayDataService
    {
        void LoadQuickList();

        /// <summary>按名字查找内存索引</summary>
        int Index(string sName);

        /// <summary>Publish an already-loaded native index without querying SQL.</summary>
        void RegisterNativeIndex(int index, string characterName);

        /// <summary>Remove only the in-memory native lookup entries for an index.</summary>
        void UnregisterNativeIndex(int index);

        /// <summary>获取存档 (含 Blob 解压)</summary>
        int Get(int nIndex, ref THumDataInfo HumanRCD);

        /// <summary>按角色名获取存档 (直接 SQL 查询)</summary>
        bool GetByChrName(string sChrName, ref THumDataInfo HumanRCD);

        /// <summary>翻转删除标记</summary>
        bool ResetDeletedFlagByChrName(string sChrName, bool boDeleted);

        /// <summary>获取查询角色简略信息</summary>
        int GetQryChar(int nIndex, ref TQueryChr QueryChrRcd);

        /// <summary>更新存档 (含 Blob 压缩)</summary>
        bool Update(int nIndex, ref THumDataInfo HumanRCD,
            int forceLv = 0, int forceExp = 0, int fightPoints = 0, int sfLevel = 0,
            int apprenticeNum = int.MinValue, int heroCardLv = int.MinValue,
            int platinaChrLv = int.MinValue);

        bool UpdateQryChar(int nIndex, TQueryChr QueryChrRcd);

        /// <summary>添加角色存档</summary>
        bool Add(ref THumDataInfo HumanRCD);

        /// <summary>删除</summary>
        bool Delete(int nIndex);
        bool Delete(string sChrName);

        int Count();

        // ===== 新增：对应分析文档的 user_data Blob 操作 =====

        /// <summary>
        /// 按 Idx 加载完整存档 (SELECT Data, ScriptData FROM user_data WHERE idx=@id)。
        /// 返回解压后的 Data 和 ScriptData。
        /// </summary>
        (byte[] data, byte[] scriptData) LoadBlob(int idx);

        /// <summary>
        /// 按 Idx 保存完整存档 (UPDATE user_data SET Data=@data, ScriptData=@script WHERE Idx=@idx)。
        /// 自动压缩后写入。
        /// </summary>
        bool SaveBlob(int idx, byte[] data, byte[] scriptData = null);

        /// <summary>Persist the exact envelopes produced by the original 0x0150 path.</summary>
        bool SaveNativeBlobExact(int idx, NativeSavePersistenceData persistence);

        /// <summary>
        /// 创建存档行 (INSERT IGNORE INTO user_data(Idx, ChrName) VALUES(@idx, @name))。
        /// </summary>
        bool CreateDataRow(int idx, string chrName);

        /// <summary>
        /// 物理删除存档行。
        /// </summary>
        bool DeleteDataRow(int idx);
    }
}
