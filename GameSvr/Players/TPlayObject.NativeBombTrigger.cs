using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 炸弹触发逻辑 (Bomb trigger logic)
    ///
    /// 战神 sub_7896FC @0x789752: 每300ms扫描背包物品，触发TTimerBomb类型物品(StdMode=3, Shape=32)的效果。
    /// Native sub_7896FC @0x789752: Every 300ms scans bag items and triggers TTimerBomb items (StdMode=3, Shape=32).
    ///
    /// MOVE-89: 炸弹触发逻辑缺失
    /// </summary>
    public partial class TPlayObject
    {
        private int m_dwBombTick = 0;

        /// <summary>
        /// 战神 sub_7896FC: 处理背包中的炸弹物品触发
        /// Native sub_7896FC: Process bomb item triggers in bag
        ///
        /// 调用时机：TPlayObject.Run()，每300ms执行一次
        /// Call timing: TPlayObject.Run(), executes every 300ms
        /// </summary>
        private void ProcessBombTick(int currentTick)
        {
            // 战神 sub_7896FC @0x789752: `cmp dword [ebp-4], 0x12C` (300ms gate)
            // Native: 300ms gate check at 0x789752
            if (currentTick - m_dwBombTick < 300)
                return;

            m_dwBombTick = currentTick;

            // 战神 sub_7896FC @0x78975E-0x7897C8: 遍历背包物品列表
            // Native: Iterate through bag item list
            // 扫描m_ItemList，查找StdMode=3且Shape=32的物品(TTimerBomb)
            // Scan m_ItemList for items with StdMode=3 and Shape=32 (TTimerBomb)

            if (m_boDeath || m_boGhost)
                return;

            for (var i = 0; i < m_ItemList.Count; i++)
            {
                var item = m_ItemList[i];
                if (item == null || item.wIndex == 0)
                    continue;

                var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
                if (stdItem == null)
                    continue;

                // 战神 sub_7896FC @0x7897A4: 检查物品类型
                // `cmp byte [stdItem+0x14], 3` (StdMode == 3)
                // `jnz short loc_7897C3`
                // `cmp byte [stdItem+0x15], 0x20` (Shape == 0x20 = 32)
                // Native: Check item type at 0x7897A4
                if (stdItem.StdMode == 3 && stdItem.Shape == 32)
                {
                    // 战神 sub_7896FC @0x7897B2: 调用炸弹触发处理
                    // `call sub_789810` (bomb trigger handler)
                    // Native: Call bomb trigger handler at 0x7897B2
                    ProcessBombItem(item, stdItem);
                }
            }
        }

        /// <summary>
        /// 战神 sub_789810: 处理单个炸弹物品的触发效果
        /// Native sub_789810: Process single bomb item trigger effect
        /// </summary>
        private void ProcessBombItem(TUserItem item, GoodItem stdItem)
        {
            // 战神 sub_789810 @0x789810-0x789890: 炸弹触发逻辑
            // Native bomb trigger logic at 0x789810-0x789890

            // 检查耐久度
            // Check durability
            if (item.Dura == 0)
            {
                // 耐久度为0，移除物品
                // Durability is 0, remove item
                m_ItemList.Remove(item);

                // 通知客户端删除物品 (使用SendDelItems方法)
                // Notify client to remove item (use SendDelItems method)
                SendDelItems(item);
                return;
            }

            // 战神 sub_789810 @0x789830: 减少耐久度
            // `dec word [item+0x0C]` (减1点耐久)
            // Native: Decrease durability at 0x789830
            if (item.Dura > 0)
            {
                item.Dura--;

                // 通知客户端更新物品 (战神使用RM_DURACHANGE)
                // Notify client to update item (native uses RM_DURACHANGE)
                SendMsg(this, Grobal2.RM_DURACHANGE, 0, item.Dura, item.DuraMax, 0, "");
            }

            // 战神 sub_789810 @0x789840-0x789870: 范围伤害计算
            // Native: Range damage calculation at 0x789840-0x789870

            // AC2字段存储爆炸范围 (stdItem.Ac2 = explosion range)
            // DC2字段存储伤害值 (stdItem.Dc2 = damage value)
            var explosionRange = stdItem.Ac2;
            var damageValue = stdItem.Dc2;

            if (explosionRange <= 0)
                explosionRange = 2; // 默认范围2格 (default range 2 cells)

            if (damageValue <= 0)
                damageValue = 10; // 默认伤害10点 (default damage 10)

            // 战神 sub_789810 @0x789878: 对周围目标造成伤害
            // `call sub_76C590` (range damage application)
            // Native: Apply range damage at 0x789878
            ApplyBombDamage(explosionRange, damageValue);

            // 播放爆炸特效
            // Play explosion effect
            SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, m_nCurrX, m_nCurrY, 0, "");
        }

        /// <summary>
        /// 战神 sub_76C590: 对范围内目标造成伤害
        /// Native sub_76C590: Apply damage to targets in range
        /// </summary>
        private void ApplyBombDamage(int range, int damage)
        {
            if (m_PEnvir == null)
                return;

            // 战神 sub_76C590 @0x76C590-0x76C650: 获取范围内的对象
            // Native: Get objects in range at 0x76C590-0x76C650
            var targets = new System.Collections.Generic.List<TBaseObject>();
            m_PEnvir.GetRangeBaseObject(m_nCurrX, m_nCurrY, range, false, targets);

            foreach (var target in targets)
            {
                if (target == null || target == this)
                    continue;

                if (target.m_boDeath || target.m_boGhost)
                    continue;

                // 检查目标是否在攻击范围内
                // Check if target is in attack range
                if (System.Math.Abs(target.m_nCurrX - m_nCurrX) > range ||
                    System.Math.Abs(target.m_nCurrY - m_nCurrY) > range)
                    continue;

                // 战神 sub_76C590 @0x76C620: 造成伤害
                // `call sub_76E3A0` (apply damage)
                // Native: Apply damage at 0x76C620

                // 炸弹伤害无视防御
                // Bomb damage ignores defense
                var actualDamage = damage;

                if (actualDamage > 0)
                {
                    target.StruckDamage(actualDamage, this);
                    target.SendRefMsg(Grobal2.RM_10101, 0, target.m_WAbil.HP, target.m_WAbil.MaxHP, ObjectId, "");
                }
            }
        }
    }
}
