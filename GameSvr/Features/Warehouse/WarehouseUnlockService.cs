using System;
using SystemModule;

namespace GameSvr.Features.Warehouse
{
    /// <summary>
    /// 战神仓库容量管理服务。
    ///
    /// 运行时容量存储在玩家对象 <c>[player+0x516]</c> (word, 原生默认值待逆向确认)。
    /// 持久化字段位于人物存档 <c>rec[0x516]</c> (2字节)。
    ///
    /// 原生逻辑:
    /// - 仓库容量检查发生在 CM_USERSTORAGEITEM (1031) 处理流程中
    /// - 容量上限控制物品存入操作的成功与否
    /// - 解锁操作可能通过脚本或专用协议触发(待native EA确认)
    ///
    /// 参考 memory: trade-storage-subsystems-reversed.md
    /// 仓库容量=rec+0x516非0x50E (已在memory中明确标注)
    /// </summary>
    public static class WarehouseUnlockService
    {
        /// <summary>
        /// 原生默认仓库容量 (待从二进制确认初始化值)。
        /// 暂定为常见配置值，实际应从 M2Server 初始化代码逆向。
        /// </summary>
        private const int DefaultWarehouseCapacity = 40;

        /// <summary>
        /// 仓库容量理论上限 (word 类型最大值 65535，实际受客户端UI限制)。
        /// 需从原生代码确认是否有显式上限检查。
        /// </summary>
        private const int MaxWarehouseCapacity = ushort.MaxValue;

        /// <summary>
        /// 获取玩家当前仓库容量。
        ///
        /// 对应原生字段 <c>[player+0x516]</c> (word)。
        /// 该值在玩家登录时从存档 rec[0x516] 加载。
        /// </summary>
        /// <param name="player">玩家对象</param>
        /// <returns>当前仓库容量上限</returns>
        public static int GetWarehouseCapacity(TPlayObject player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            // 访问 player+0x516 (运行时字段)
            // 实际字段名需根据 TPlayObject 定义确认
            // 如果字段尚未在 C# 中建模，需添加:
            //   public ushort m_wWarehouseCapacity; // +0x516 word

            // 临时实现:假设字段已存在且命名为 m_wWarehouseCapacity
            // return player.m_wWarehouseCapacity;

            // 当前占位返回默认值，待字段接线后移除
            return DefaultWarehouseCapacity;
        }

        /// <summary>
        /// 设置玩家仓库容量。
        ///
        /// 修改运行时字段 <c>[player+0x516]</c>，并标记存档dirty以触发持久化。
        /// 容量变更应受以下约束:
        /// 1. 不得小于当前已存储物品数量 (避免数据丢失)
        /// 2. 不得超过 <see cref="MaxWarehouseCapacity"/>
        /// 3. 操作需记录日志 (如涉及付费解锁)
        /// </summary>
        /// <param name="player">玩家对象</param>
        /// <param name="newCapacity">新容量值</param>
        /// <returns>设置是否成功</returns>
        public static bool SetWarehouseCapacity(TPlayObject player, int newCapacity)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            if (newCapacity < 0 || newCapacity > MaxWarehouseCapacity)
            {
                M2Share.ErrorMessage(
                    $"[WarehouseUnlock] 非法容量值: player={player.m_sCharName} capacity={newCapacity}");
                return false;
            }

            // 检查新容量是否小于当前已存储物品数
            var currentItemCount = GetCurrentStorageItemCount(player);
            if (newCapacity < currentItemCount)
            {
                M2Share.ErrorMessage(
                    $"[WarehouseUnlock] 容量不足以容纳现有物品: player={player.m_sCharName} " +
                    $"newCapacity={newCapacity} currentItems={currentItemCount}");
                return false;
            }

            // 实际实现需访问 player+0x516 字段
            // player.m_wWarehouseCapacity = (ushort)newCapacity;

            // 标记存档需要保存
            // player.MarkRecordDirty(); // 或类似机制

            M2Share.MainOutMessage(
                $"[WarehouseUnlock] 容量变更: player={player.m_sCharName} " +
                $"newCapacity={newCapacity}");

            return true;
        }

        /// <summary>
        /// 解锁额外仓库容量 (累加模式)。
        ///
        /// 典型使用场景:
        /// - 通过NPC脚本购买仓库扩容 (消耗金币/元宝)
        /// - GM命令赠送仓库空间
        /// - 任务奖励解锁
        ///
        /// 原生实现可能通过 PAS 脚本函数触发，需逆向确认:
        /// - 是否有专用的 CM_* 协议
        /// - 或仅通过脚本调用 SetWarehouseCapacity
        /// </summary>
        /// <param name="player">玩家对象</param>
        /// <param name="additionalSlots">增加的槽位数</param>
        /// <returns>解锁是否成功</returns>
        public static bool UnlockAdditionalCapacity(TPlayObject player, int additionalSlots)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            if (additionalSlots <= 0)
            {
                M2Share.ErrorMessage(
                    $"[WarehouseUnlock] 无效的增量: player={player.m_sCharName} " +
                    $"additionalSlots={additionalSlots}");
                return false;
            }

            var currentCapacity = GetWarehouseCapacity(player);
            var newCapacity = currentCapacity + additionalSlots;

            if (newCapacity > MaxWarehouseCapacity)
            {
                M2Share.ErrorMessage(
                    $"[WarehouseUnlock] 超过最大容量: player={player.m_sCharName} " +
                    $"current={currentCapacity} additional={additionalSlots} " +
                    $"max={MaxWarehouseCapacity}");
                return false;
            }

            return SetWarehouseCapacity(player, newCapacity);
        }

        /// <summary>
        /// 获取玩家当前仓库已存储物品数量。
        ///
        /// 需访问玩家的仓库物品列表 (具体字段待确认，可能是 m_StorageItemList)。
        /// 原生存储逻辑在 CM_USERSTORAGEITEM / CM_USERTAKEBACKSTORAGEITEM 处理流程中。
        /// </summary>
        /// <param name="player">玩家对象</param>
        /// <returns>当前存储物品数量</returns>
        private static int GetCurrentStorageItemCount(TPlayObject player)
        {
            // 实际实现需访问仓库物品列表
            // 字段可能是 m_StorageItemList (TList<TUserItem>)
            // return player.m_StorageItemList?.Count ?? 0;

            // 临时占位返回0，待接线后移除
            return 0;
        }

        /// <summary>
        /// 检查玩家是否可以存入更多物品到仓库。
        ///
        /// 该检查应在 CM_USERSTORAGEITEM (1031) 处理前调用。
        /// 原生逻辑可能在 <c>sub_6B9298</c> (CM 派发器) 或具体handler中。
        /// </summary>
        /// <param name="player">玩家对象</param>
        /// <returns>仓库是否还有空余容量</returns>
        public static bool HasAvailableCapacity(TPlayObject player)
        {
            if (player == null)
                return false;

            var capacity = GetWarehouseCapacity(player);
            var currentCount = GetCurrentStorageItemCount(player);
            return currentCount < capacity;
        }

        /// <summary>
        /// 获取仓库剩余容量。
        /// </summary>
        /// <param name="player">玩家对象</param>
        /// <returns>剩余可用槽位数</returns>
        public static int GetAvailableCapacity(TPlayObject player)
        {
            if (player == null)
                return 0;

            var capacity = GetWarehouseCapacity(player);
            var currentCount = GetCurrentStorageItemCount(player);
            return Math.Max(0, capacity - currentCount);
        }

        /// <summary>
        /// 重置仓库容量为默认值 (用于特殊场景，如GM命令或数据修复)。
        /// </summary>
        /// <param name="player">玩家对象</param>
        /// <returns>重置是否成功</returns>
        public static bool ResetToDefaultCapacity(TPlayObject player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            return SetWarehouseCapacity(player, DefaultWarehouseCapacity);
        }
    }
}
