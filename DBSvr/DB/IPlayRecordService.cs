using System;
using System.Collections.Generic;
using SystemModule;
using DBSvr.Core;

namespace DBSvr
{
    /// <summary>
    /// 角色索引服务接口 (对应 user_index 表)。
    /// 负责角色元数据的 CRUD：创建、查询、删除、改名、排行榜等。
    /// </summary>
    public interface IPlayRecordService
    {
        /// <summary>从数据库加载快速索引列表到内存</summary>
        void LoadQuickList();

        /// <summary>按角色名查找内存索引位置</summary>
        int Index(string sName);

        /// <summary>获取索引记录</summary>
        HumRecordData Get(int nIndex, ref bool success);

        /// <summary>按 Id 获取</summary>
        HumRecordData GetBy(int nIndex, ref bool success);

        /// <summary>按账号 (PTID) 查找所有非删除角色</summary>
        int FindByAccount(string sAccount, ref IList<TQuickID> ChrList);

        /// <summary>按账号查找被删除角色 (用于恢复)</summary>
        int FindDeletedByAccount(string sAccount, ref IList<HumRecordData> DeletedList);

        /// <summary>恢复角色后重建索引</summary>
        bool ReAddToQuickList(string sChrName);

        /// <summary>账号下角色数量</summary>
        int ChrCountOfAccount(string sAccount);
        /// <summary>今日创建数量</summary>
        int TodayCreateCount(string sAccount);

        /// <summary>添加角色索引</summary>
        bool Add(HumRecordData HumRecord);

        /// <summary>删除角色索引 (软删除)</summary>
        bool Delete(string sName);

        /// <summary>物理删除角色索引和存档 (用于清理)</summary>
        bool HardDelete(int idx);

        /// <summary>更新索引</summary>
        bool Update(int nIndex, ref HumRecordData HumDBRecord);

        /// <summary>按 Id 更新</summary>
        void UpdateBy(int nIndex, ref HumRecordData HumDBRecord);

        /// <summary>按名字查找索引列表</summary>
        int FindByName(string sChrName, List<HumRecordData> ChrList);

        // ===== 新增：对应分析文档的 user_index 操作 =====

        /// <summary>创建新角色 (INSERT INTO user_index + user_data)</summary>
        int CreateCharacter(string ptid, string chrName, int job, int sex, int hair, int level = 1);

        /// <summary>
        /// 创建 GM 角色 (原版 0x5A8124 批量导入路径)。
        /// 对应 native: insert Ignore into user_index(PTID, LoginID, ChrName, Level, AdminLevel, ...)
        /// Values("%s", "%s", "%s", 40, 5, Now(), Now());
        /// </summary>
        int CreateGMCharacter(string ptid, string loginId, string chrName);

        /// <summary>
        /// 设置角色管理员等级 (原版 0x5A86E8)。
        /// 对应 native: Update user_index set AdminLevel=%d where idx=%d;
        /// </summary>
        bool SetAdminLevel(int idx, int adminLevel);

        /// <summary>角色名是否已存在</summary>
        bool IsChrNameExists(string chrName);

        /// <summary>按 PTID 查询角色列表 (用于角色选择界面)</summary>
        List<ChrIndexInfo> QueryChrByPtid(string ptid);

        /// <summary>原生 type3/0x0188 查询：返回 PTID 下所有 IsDelete!=1 角色。</summary>
        List<ChrIndexInfo> QueryNativeType3ByPtid(string ptid);
        List<ChrIndexInfo> QueryNativeType3ByPtid(byte[] ptid);

        /// <summary>更新角色等级等元数据 (对应 SAVEHUMANRCD)</summary>
        bool UpdateCharIndex(int idx, int level, int exp, int job, int sex, int forceLv, int forceExp, int fightPoints, int sfLevel,
            int apprenticeNum = int.MinValue, int heroCardLv = int.MinValue,
            int platinaChrLv = int.MinValue);

        /// <summary>Apply the index fields copied by the original native 0x0150 worker.</summary>
        bool UpdateNativeSaveIndex(int idx, NativeSavePersistenceData persistence);
        NativeForceLevelStoreAttempt ApplyNativeForceLevel(
            byte[] characterName, ushort forceLevel);
        bool PersistNativeForceLevel(int idx, ushort forceLevel);

        /// <summary>Register a raw native character name in the in-memory occupancy set.</summary>
        void RegisterNativeCharacterName(byte[] characterName);
        bool IsNativeCharacterNameOccupied(byte[] characterName);
        bool TryGetNativeCharacterByName(byte[] characterName,
            out ChrIndexInfo character);
        bool TryGetNativeCharacterByUserId(long userId,
            out ChrIndexInfo character);
        bool TryRestoreNativeCharacter(byte[] characterName,
            out ChrIndexInfo character);
        bool PersistNativeCharacterRestore(int index);
        NativeAccountRenameResult RenameNativeAccount(byte[] oldPtid,
            byte[] newPtid);
        void ResetAllNativeTransferLocks();
        void ResetNativeTransferLock(byte[] characterName);
        void SetNativeCharacterBusy(byte[] characterName);

        /// <summary>更新 lvChangeTime (等级/战力变更时间, 排行榜用)</summary>
        bool UpdateLvChangeTime(int idx, int oldLevel, int oldForceLv, int oldSfLevel);

        /// <summary>获取角色 Idx (通过名字)</summary>
        int GetIdxByName(string chrName);

        /// <summary>排行榜查询 - 全职业</summary>
        List<RankEntry> GetLevelRank(int limit = 100);

        /// <summary>排行榜查询 - 分职业</summary>
        List<RankEntry> GetLevelRankByJob(int job, int limit = 100);

        /// <summary>战力排行</summary>
        List<RankEntry> GetFightPowerRank(int limit = 100);

        /// <summary>内力排行</summary>
        List<RankEntry> GetForceRank(int limit = 100);

        /// <summary>排序查询 - 分页</summary>
        List<ChrSortEntry> GetCharacterPage(int lastIdx, int limit = 5000);
    }

    /// <summary>
    /// 角色索引简要信息 (用于选角界面)。
    /// </summary>
    public class ChrIndexInfo
    {
        public int Idx;
        public string PTID;
        public byte[] PTIDBytes;
        public string ChrName;
        public byte[] ChrNameBytes;
        public int Job;
        public int Sex;
        public int Level;
        public int Exp;
        public long UserId;
        public bool IsTransLock;
        public int DestinationZoneId;
        public int DestinationGroupId;
        public bool NativeBusy;
        public int DeleteState;
        public bool IsDelete;
        public bool IsSelect;
        public DateTime ModifyDate;
    }

    /// <summary>Result of the native DB-tool account rename transaction.</summary>
    public sealed class NativeAccountRenameResult
    {
        public bool Success { get; init; }
        public IReadOnlyList<int> CharacterIndices { get; init; } =
            Array.Empty<int>();
    }

    /// <summary>
    /// 排行榜条目。
    /// </summary>
    public class RankEntry
    {
        public string ChrName;
        public int Level;
        public int SfLevel;
        public int ForceLv;
        public int Exp;
        public int FightPoints;
        public int ApprenticeNum;
    }

    /// <summary>
    /// 角色排序分页条目。
    /// </summary>
    public class ChrSortEntry
    {
        public int Idx;
        public string PTID;
        public string ChrName;
        public bool IsDelete;
        public bool IsSelect;
        public int Job;
        public int Sex;
        public int Level;
        public int Exp;
        public int HeroCardLv;
        public int PlatinaChrLv;
        public int ForceLv;
        public int ForceExp;
        public int FightPoints;
        public int SfLevel;
        public long UserId;
        public DateTime ModifyDate;
        public int ApprenticeNum;
        public int AdminLevel;
        public int SrcZoneId;
        public int SrcGroupId;
        public bool IsTransLock;
        public int TransferModal;
        public int DestinationZoneId;
        public int DestinationGroupId;
        public DateTime LvChangeTime;
        public int GuardNum;
        public int DarePoint;
        public string SrcCharName;
    }
}
