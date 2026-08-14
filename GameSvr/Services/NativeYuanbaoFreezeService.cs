using System;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// 元宝预冻结/解冻服务 - 战神引擎 0x0063BF9C
    /// MVI - 最小可行实现
    /// </summary>
    public static class NativeYuanbaoFreezeService
    {
        /// <summary>
        /// 预冻结元宝
        /// </summary>
        public static bool FreezeYuanbao(TPlayObject player, int amount)
        {
            if (player == null || amount <= 0)
                return false;

            try
            {
                // Phase 1: 检查元宝余额
                if (player.m_nGameGold < amount)
                {
                    player.SysMsg("元宝余额不足", MsgColor.Red, MsgType.Hint);
                    return false;
                }

                // Phase 2: 检查冻结额度
                int currentFrozen = GetFrozenYuanbao(player);
                if (currentFrozen + amount > player.m_nGameGold)
                {
                    player.SysMsg("冻结失败：超过可用额度", MsgColor.Red, MsgType.Hint);
                    return false;
                }

                // Phase 3: 执行冻结
                SetFrozenYuanbao(player, currentFrozen + amount);

                // Phase 4: 记录日志
                M2Share.AddGameDataLog(string.Join('\t', "YuanbaoFreeze", player.m_sCharName,
                    amount, currentFrozen + amount, "冻结元宝"));

                // Phase 5: 发送消息
                player.SysMsg($"成功冻结{amount}元宝", MsgColor.Green, MsgType.Hint);

                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 元宝冻结异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解冻元宝
        /// </summary>
        public static bool UnfreezeYuanbao(TPlayObject player, int amount)
        {
            if (player == null || amount <= 0)
                return false;

            try
            {
                // Phase 1: 检查冻结余额
                int currentFrozen = GetFrozenYuanbao(player);
                if (currentFrozen < amount)
                {
                    player.SysMsg("冻结余额不足", MsgColor.Red, MsgType.Hint);
                    return false;
                }

                // Phase 2: 执行解冻
                SetFrozenYuanbao(player, currentFrozen - amount);

                // Phase 3: 记录日志
                M2Share.AddGameDataLog(string.Join('\t', "YuanbaoUnfreeze", player.m_sCharName,
                    amount, currentFrozen - amount, "解冻元宝"));

                // Phase 4: 发送消息
                player.SysMsg($"成功解冻{amount}元宝", MsgColor.Green, MsgType.Hint);

                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 元宝解冻异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 消费冻结的元宝
        /// </summary>
        public static bool ConsumeFrozenYuanbao(TPlayObject player, int amount, string reason)
        {
            if (player == null || amount <= 0)
                return false;

            try
            {
                // Phase 1: 检查冻结余额
                int currentFrozen = GetFrozenYuanbao(player);
                if (currentFrozen < amount)
                {
                    player.SysMsg("冻结余额不足", MsgColor.Red, MsgType.Hint);
                    return false;
                }

                // Phase 2: 扣除冻结元宝
                SetFrozenYuanbao(player, currentFrozen - amount);

                // Phase 3: 扣除实际元宝
                player.m_nGameGold -= amount;

                // Phase 4: 记录日志
                M2Share.AddGameDataLog(string.Join('\t', "YuanbaoConsume", player.m_sCharName,
                    amount, currentFrozen - amount, reason));

                // Phase 5: 发送消息
                player.SysMsg($"消费{amount}元宝：{reason}", MsgColor.Green, MsgType.Hint);

                return true;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage($"[Exception] 元宝消费异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 查询冻结的元宝数量
        /// </summary>
        public static int QueryFrozenYuanbao(TPlayObject player)
        {
            if (player == null)
                return 0;

            int frozen = GetFrozenYuanbao(player);
            player.SysMsg($"当前冻结元宝：{frozen}", MsgColor.Green, MsgType.Hint);
            return frozen;
        }

        // 辅助方法：获取冻结元宝数量
        private static int GetFrozenYuanbao(TPlayObject player)
        {
            // MVI: 使用玩家的某个字段存储冻结金额
            // 实际应该从数据库或玩家对象的特定字段读取
            // 这里暂时返回0，需要实际字段支持
            return 0;
        }

        // 辅助方法：设置冻结元宝数量
        private static void SetFrozenYuanbao(TPlayObject player, int amount)
        {
            // MVI: 设置玩家的冻结金额
            // 实际应该写入数据库或玩家对象的特定字段
            // 需要实际字段支持
        }
    }
}
