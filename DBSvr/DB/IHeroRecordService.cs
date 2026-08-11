using System;
using System.Collections.Generic;

namespace DBSvr
{
    /// <summary>
    /// 英雄索引服务接口 (对应 hero_index 表)。
    /// 与 user_index 同构设计，共用相同分页/排行模式。
    /// </summary>
    public interface IHeroRecordService
    {
        void LoadQuickList();
        int Index(string heroName);
        int CreateHero(string masterName, string heroName, int heroType, int job, int sex, long heroId);
        bool DeleteHero(int idx);
        bool HardDeleteHero(int idx);
        bool IsHeroNameExists(string heroName);
        List<HeroIndexInfo> QueryHeroesByMaster(string masterName);
        List<HeroIndexInfo> QueryDeletedHeroesByMaster(string masterName);
        int ChrCountOfMaster(string masterName);
        bool UpdateHeroIndex(int idx, int level, int exp, int job, int sex, int forceLv, int forceExp, int sfLevel,
            int isDelete = -1, int heroType = -1, int consignation = -1);
        /// <summary>
        /// 更新 lvChangeTime（原版 0x5B27A8）。
        /// 带旧值重载：仅当 Level/ForceLv/sfLevel 中任一确实改变时才更新时间戳。
        /// native SQL: Update hero_index set lvChangeTime=Now() where idx=%d
        ///             and (Level&lt;&gt;%d or ForceLv&lt;&gt;%d or sfLevel&lt;&gt;%d);
        /// </summary>
        bool UpdateLvChangeTime(int idx, byte oldLevel, byte oldForceLv, byte oldSfLevel);

        /// <summary>
        /// 无条件更新 lvChangeTime（旧接口兼容，按原版内联路径使用）。
        /// BLOCKED: 此重载不携带旧值故无法复现原版 WHERE 谓词，与 native 0x5B27A8 不等价。
        /// 仅在调用方无法提供旧值时使用，会多更新时间戳（影响排行榜 tiebreak，但不破坏等级数据）。
        /// </summary>
        bool UpdateLvChangeTime(int idx);
        bool RestoreHero(string heroName);
        bool RenameHero(string oldName, string newName, int idx);
        bool SetHeroConsignation(int idx, int expectedValue, int newValue);
        bool RenameMaster(string oldMaster, string newMaster);
        int GetIdxByName(string heroName);
        bool TryGetNativeHeroByName(byte[] heroName, out HeroIndexInfo hero);
        bool TryGetNativeForceLevelIndex(byte[] heroName, out int index);
        List<RankEntry> GetHeroLevelRank(int limit = 100);
        List<HeroSortEntry> GetHeroPage(int lastIdx, int limit = 5000);

        /// <summary>
        /// 惰性回填 heroId（原版 0x58CF28: Update hero_index set heroId = %d where idx = %d）。
        /// 仅当 heroId == 0 时调用，分配全局唯一 ID 并写回单行。
        /// 原版触发：hero_index 加载器检测到 heroId=0 时调用 0x5CA174 分配器。
        /// 公式：heroId = ((zoneId * 1000 + groupId + 10000000) * 1000000000) + idx
        /// </summary>
        /// <param name="idx">hero_index.idx 主键</param>
        /// <param name="zoneId">ZoneId 配置（DBService.ini [Setup] ZoneIdx）</param>
        /// <param name="groupId">GroupId 配置（DBService.ini [Setup] GroupIdx）</param>
        /// <returns>新分配的 heroId，失败返回 0</returns>
        long BackfillHeroId(int idx, int zoneId, int groupId);
    }

    public class HeroIndexInfo
    {
        public int Idx;
        public string MasterName;
        public byte[] MasterNameBytes;
        public string HeroName;
        public byte[] HeroNameBytes;
        public bool IsDelete;
        public int HeroType;
        public int Consignation;
        public int Job;
        public int Sex;
        public int Level;
        public int Exp;
        public int ForceLv;
        public int ForceExp;
        public int SfLevel;
        public long HeroId;
        public DateTime ModifyDate;
    }

    public class HeroSortEntry
    {
        public int Idx;
        public string MasterName;
        public string HeroName;
        public bool IsDelete;
        public int HeroType;
        public int Consignation;
        public int Level;
        public int Job;
        public int Sex;
        public int Exp;
        public int ForceLv;
        public int ForceExp;
        public int SfLevel;
        public long HeroId;
        public DateTime ModifyDate;
    }
}
