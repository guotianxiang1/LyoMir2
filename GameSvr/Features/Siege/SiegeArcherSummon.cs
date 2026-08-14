using SystemModule;

namespace GameSvr.Features.Siege
{
    /// <summary>
    /// 城战弓箭手召唤系统 - MVI实现
    /// 基于战神引擎逆向工程：
    /// - ArcherGuard.cs: m_nDirection = -1 (带符号哨兵值，表示"保持当前朝向")
    /// - UserCastle.cs: m_Archer[12] 数组存储弓箭手单元
    /// - CastleConfManager.cs: 配置加载/保存逻辑
    /// </summary>
    public static class SiegeArcherSummon
    {
        // 弓箭手相关常量
        private const int MaxArcherSlots = 12;
        private const string DefaultArcherName = "弓箭手";
        private const ushort DefaultArcherHP = 2000;
        private const int DefaultArcherDirection = 3; // 南向

        // 召唤失败消息
        private const string ArcherSlotOccupiedMessage = "该位置已有弓箭手";
        private const string ArcherSpawnFailedMessage = "弓箭手召唤失败";
        private const string ArcherLimitReachedMessage = "弓箭手数量已达上限";
        private const string InvalidSlotMessage = "无效的弓箭手位置";

        /// <summary>
        /// 召唤城战弓箭手到指定槽位
        /// </summary>
        /// <param name="castle">城堡实例</param>
        /// <param name="slotIndex">槽位索引 (0-11)</param>
        /// <param name="x">X坐标（可选，使用配置值时传null）</param>
        /// <param name="y">Y坐标（可选，使用配置值时传null）</param>
        /// <returns>召唤的弓箭手实例，失败返回null</returns>
        public static TBaseObject SummonArcherToSlot(
            TUserCastle castle,
            int slotIndex,
            short? x = null,
            short? y = null)
        {
            // 验证槽位索引
            if (slotIndex < 0 || slotIndex >= MaxArcherSlots)
            {
                M2Share.ErrorMessage($"[SiegeArcherSummon] {InvalidSlotMessage}: {slotIndex}");
                return null;
            }

            // 验证城堡和地图
            if (castle?.m_MapCastle == null)
            {
                M2Share.ErrorMessage("[SiegeArcherSummon] 城堡或地图未初始化");
                return null;
            }

            var archerSlot = castle.m_Archer[slotIndex];
            if (archerSlot == null)
            {
                M2Share.ErrorMessage($"[SiegeArcherSummon] 弓箭手槽位{slotIndex}未初始化");
                return null;
            }

            // 检查槽位是否已占用
            if (archerSlot.BaseObject != null && !archerSlot.BaseObject.m_boDeath)
            {
                M2Share.ErrorMessage($"[SiegeArcherSummon] {ArcherSlotOccupiedMessage}: {slotIndex}");
                return null;
            }

            // 使用提供的坐标或配置坐标
            var spawnX = x ?? archerSlot.nX;
            var spawnY = y ?? archerSlot.nY;

            if (spawnX == 0 || spawnY == 0)
            {
                M2Share.ErrorMessage($"[SiegeArcherSummon] 弓箭手槽位{slotIndex}坐标未配置");
                return null;
            }

            // 生成弓箭手
            var archerName = string.IsNullOrEmpty(archerSlot.sName)
                ? DefaultArcherName
                : archerSlot.sName;

            var archer = M2Share.UserEngine?.RegenMonsterByName(
                castle.m_MapCastle,
                spawnX,
                spawnY,
                archerName);

            if (archer == null)
            {
                M2Share.ErrorMessage($"[SiegeArcherSummon] {ArcherSpawnFailedMessage}: {archerName} at ({spawnX}, {spawnY})");
                return null;
            }

            // 初始化弓箭手属性
            InitializeArcherProperties(archer, castle, archerSlot, spawnX, spawnY);

            // 更新槽位引用
            archerSlot.BaseObject = archer;

            return archer;
        }

        /// <summary>
        /// 初始化弓箭手属性
        /// 基于 UserCastle.cs:225-229 的原版逻辑
        /// </summary>
        private static void InitializeArcherProperties(
            TBaseObject archer,
            TUserCastle castle,
            TObjUnit archerSlot,
            short spawnX,
            short spawnY)
        {
            // 设置HP（从配置或默认值）
            archer.m_WAbil.HP = archerSlot.nHP > 0 ? archerSlot.nHP : DefaultArcherHP;

            // 关联城堡
            archer.m_Castle = castle;

            // 设置为守卫单位并配置位置和朝向
            if (archer is GuardUnit guardUnit)
            {
                // 记住初始位置（用于巡逻/返回）
                guardUnit.m_nX550 = spawnX;
                guardUnit.m_nY554 = spawnY;

                // 设置朝向
                // 原版代码: m_nDirection = 3 (南向)
                // ArcherGuard.cs 的构造函数使用 -1 作为哨兵值（保持当前朝向）
                // 这里使用配置的朝向或默认南向
                guardUnit.m_nDirection = DefaultArcherDirection;
            }
        }

        /// <summary>
        /// 批量召唤所有配置的弓箭手
        /// 基于 UserCastle.cs:218-235 的城堡初始化逻辑
        /// </summary>
        public static int SummonAllConfiguredArchers(TUserCastle castle)
        {
            if (castle?.m_Archer == null)
            {
                M2Share.ErrorMessage("[SiegeArcherSummon] 城堡弓箭手数组未初始化");
                return 0;
            }

            var summonedCount = 0;

            for (var i = 0; i < castle.m_Archer.Length; i++)
            {
                var archerSlot = castle.m_Archer[i];

                // 跳过未配置HP的槽位（HP <= 0 表示该槽位未启用）
                if (archerSlot.nHP <= 0)
                    continue;

                var archer = SummonArcherToSlot(castle, i);
                if (archer != null)
                {
                    summonedCount++;
                }
            }

            return summonedCount;
        }

        /// <summary>
        /// 移除指定槽位的弓箭手
        /// </summary>
        public static bool DismissArcherFromSlot(TUserCastle castle, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MaxArcherSlots)
                return false;

            if (castle?.m_Archer == null)
                return false;

            var archerSlot = castle.m_Archer[slotIndex];
            if (archerSlot?.BaseObject == null)
                return false;

            // 标记死亡并清理
            var archer = archerSlot.BaseObject;
            if (!archer.m_boDeath && !archer.m_boGhost)
            {
                archer.Die();
            }

            archerSlot.BaseObject = null;
            return true;
        }

        /// <summary>
        /// 检查并清理已死亡的弓箭手引用
        /// 基于 UserCastle.cs:440-442 的清理逻辑
        /// </summary>
        public static void CleanupDeadArchers(TUserCastle castle)
        {
            if (castle?.m_Archer == null)
                return;

            for (var i = 0; i < castle.m_Archer.Length; i++)
            {
                var archerSlot = castle.m_Archer[i];
                if (archerSlot?.BaseObject != null && archerSlot.BaseObject.m_boGhost)
                {
                    // 清理已变为幽灵的弓箭手引用
                    archerSlot.BaseObject = null;
                }
            }
        }

        /// <summary>
        /// 统计当前存活的弓箭手数量
        /// </summary>
        public static int CountAliveArchers(TUserCastle castle)
        {
            if (castle?.m_Archer == null)
                return 0;

            var count = 0;
            for (var i = 0; i < castle.m_Archer.Length; i++)
            {
                var archerSlot = castle.m_Archer[i];
                if (archerSlot?.BaseObject != null &&
                    !archerSlot.BaseObject.m_boDeath &&
                    !archerSlot.BaseObject.m_boGhost)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 获取指定槽位的弓箭手状态
        /// </summary>
        public static ArcherSlotStatus GetArcherSlotStatus(TUserCastle castle, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MaxArcherSlots)
                return ArcherSlotStatus.Invalid;

            if (castle?.m_Archer == null)
                return ArcherSlotStatus.Invalid;

            var archerSlot = castle.m_Archer[slotIndex];
            if (archerSlot == null)
                return ArcherSlotStatus.Unconfigured;

            if (archerSlot.nHP <= 0)
                return ArcherSlotStatus.Disabled;

            if (archerSlot.BaseObject == null)
                return ArcherSlotStatus.Empty;

            if (archerSlot.BaseObject.m_boDeath || archerSlot.BaseObject.m_boGhost)
                return ArcherSlotStatus.Dead;

            return ArcherSlotStatus.Alive;
        }
    }

    /// <summary>
    /// 弓箭手槽位状态枚举
    /// </summary>
    public enum ArcherSlotStatus
    {
        Invalid,        // 无效索引
        Unconfigured,   // 未配置
        Disabled,       // 已禁用（HP <= 0）
        Empty,          // 空槽位
        Dead,           // 弓箭手已死亡
        Alive           // 弓箭手存活
    }
}
