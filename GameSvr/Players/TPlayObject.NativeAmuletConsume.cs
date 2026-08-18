using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    // =====================================================================================
    // DURA-11 — Native sub_73EA20 (第二护身符消耗例程 / "second amulet routine")
    //
    // 底本: D:\loym2\staging\_reunpack_work\flat_image.bin (ImageBase=0x400000)
    // 反汇编: capstone 5.0.7. 18 个 MAGIC/技能处理器调用它 (E8 xref, 见文末表)。
    //
    // 这是【区别于】sub_73E93C (装备槽 ×100 版, 已移植为 Magic.CheckAmulet/UseAmulet, DURA-10/12/13)
    // 的第二例程: 处理【背包 或 装备槽9】的护身符 TBujuk, 扣减【原始量】(无 ×100), 装备槽在
    // 余量 <100 时经 sub_75F27C 销毁, 背包在余量 <100 时删除; cl=0 仅测试 / cl=1 消耗。
    // (施毒术 wMagicID 6 走的是【相邻的内联路径】0x6ED945 → MagicManager.cs:279-345, 已 FAITHFUL,
    //  不经本例程。)
    //
    // 入参约定 (Delphi register call): eax=self(玩家), edx=nCount, cl=boConsume。
    // 返回值: al = [ebp-6] = found (是否找到合格护身符; 与是否消耗无关)。
    //   0x73EBD2  mov al,byte[ebp-6]   ; 真正的函数出口 (前面的 0x73EBB5 xor eax,eax 属 finally 包装)
    //
    // ---- 类型判定证据 (铁律: 独立复核) ----
    //   0x73EA72 / 0x73EACE  mov edx,[0x75E3F8]  ; 类引用
    //   [0x75E3F8] = 0x75E444 = VMT 'TBujuk' (链: TBujuk→TEquipBujuk→TEquipItem→TBaseItem→TBaseObj→TObject)
    //   兄弟类 [0x75E4E8] = 'TPoisons' (链: TPoisons→TEquipBujuk→...) —— 与 TBujuk 同父, 【不】互为派生,
    //   故 `is TBujuk` 仅含 TBujuk 实例, 【不】含 TPoisons。
    //   物品工厂 sub_74C338: StdMode 25 → 0x74D066, Shape 跳表 @0x74D07B: Shape 5 → new TBujuk (@0x74D0C0
    //   mov eax,[0x75E3F8]), Shape 1,2 → new TPoisons (@0x74D0AA mov eax,[0x75E4E8])。
    //   => 原生 `is TBujuk` == C#  StdMode==25 && Shape==5  (与 Magic.CheckAmulet 的 nType==1 臂同源)。
    //   0x73EA69 cmp [item+0x1C],0 (StdItem 指针非空) == C#  GetStdItem(wIndex)!=null (wIndex>0)。
    //
    // ---- 内存/偏移映射 (item = TUserItem) ----
    //   +0x18 = MakeIndex, +0x1C = StdItem 指针, +0x26 = Dura(word), +0x28 = DuraMax(word)
    //   self+0x4C0 = 装备容器 (slot9=U_BUJUK ↔ m_UseItems[9]),  self+0x508 = 背包 (m_ItemList)
    //     (self+0x508=m_ItemList 已由 PasApiBridge.cs:1796 与 bag-gate sub_73D140@0x73D14B 佐证)
    //
    // ---- 例程体 (逐地址) ----
    //   0x73EA50 mov dl,9 / 0x73EA52 mov eax,[esi+0x4C0] / 0x73EA58 call 0x75EC20  ; GetUseItem(9)
    //   0x73EA60..8B  item!=nil && [item+0x1C]!=0 && is TBujuk && word[item+0x26]>=nCount
    //                 → [ebp-0xD]=inEquip=1, [ebp-6]=found=1
    //   0x73EA95  if(!found) 扫描背包: 0x73EA9B [esi+0x508], 0x73EAA1 count, 0x73EAB4 call 0x424D4C=TList.Get(i)
    //                 同样四检; 首个命中 → [ebp-0xE]=inBag=1, found=1, break (0x73EAF1)
    //   0x73EAF7  al = found & boConsume;  je end (未找到 或 仅测试)
    //   0x73EB0A  sub word[item+0x26], dx        ; Dura -= nCount  (RAW, 无 ×100)
    //   0x73EB15  cmp ax,0x64 / jb (<100 分支)
    //     Dura>=100:
    //       0x73EB1B inEquip → 0x73EB3D call 0x765E68 = SendMsg(self, RM_DURACHANGE, 9, Dura, DuraMax, 0)
    //       0x73EB44 inBag   → 0x73EB60 call [vmt+0x260] = SendUpdateItem(item)
    //     Dura<100:
    //       0x73EB68 inBag → 0x73EB73 call 0x73D140 (bag.IndexOf(item)!=-1) → 0x73EB85 call[vmt+0x268]=SendDelItems
    //                        → 0x73EB8E call 0x414C24 = FreeAndNil(item)
    //       0x73EB95 inEquip → 0x73EBB0 call 0x75F27C(slot9, channel 0xA, "持久耗尽"@0x73EBE4)  ; 销毁
    //   0x75F27C 销毁器 (与 sub_73E93C 同一个, DURA-10 已证): 清槽 (0x75F2BB [ebx+slot*4+8]=0) →
    //     RecalcAbilitys (0x75EE78+vmt[0x8C]) → is THumanKind([0x73BBE8]) → SendDelItems(vmt+0x268) →
    //     AddGameDataLog(channel 0xA) → 辅助槽 {0,1,4,13} 过滤 (slot9 不在其中, 不刷外观) → Free(0x404690)。
    //     C# 用 ConsumeSpentPoisonCharm/UseAmulet 同款 slot=null;RecalcAbilitys;SendDelItems;AddGameDataLog。
    //
    // ---- 18 个原生调用者 (E8 call 0x73EA20) 与 cl/nCount ----
    //   [主 DoSpell sub_6ED62C (不是 sub_6ED2A4); 跳表 1 @0x6ED706 索引即 wMagicID(0..0x24),
    //    跳表 2 @0x6ED7CD 索引 = wMagicID-0x27(39..67), 跳表 3 @0x6ED854 索引 = wMagicID-0x75]
    //     0x6EDC90  wMagicID 62 = SKILL_62(召唤圣兽)  cl=1 消耗 nCount=2000(0x7D0), 前置 30s 冷却门
    //       (SKILL-62 更正: 此前写作 "wMagicID 30 = SKILL_SINSU" 有误 —— 跳表 1 索引 30 指向
    //        0x6EDC4A(走 sub_73E93C 的另一条例程); 本调用点是跳表 2 索引 23 = wMagicID 62,
    //        原始字节 71 DC 6E 00 @0x6ED829, 且同块的提示串是 "圣兽刚收回不到30秒"@0x6EE0F8。)
    //   [英雄魔法并行分派器 sub_68D2F8 (跳表@0x68DE7D/@0x68DEFF 结构与主 DoSpell 同构, 同一技能集)]
    //     0x68E0BD cl=1 n=100 | 0x68E0EB cl=1 n=100 | 0x68E11B cl=1 n=100 | 0x68E15C cl=1 n=100
    //     0x68E1EB cl=1 n=100 | 0x68E214 cl=1 n=100 | 0x68E28D cl=1 n=100 | 0x68E3CB cl=1 n=2000(30s门)
    //     0x68E483 cl=1 n=500 | 0x68E614 cl=1 n=100
    //   [sub_6940D0 (xref 0x693D72)]
    //     0x69414D cl=0 测试 n=500 | 0x694179 cl=0 n=500 | 0x6941A1 cl=0 n=500 | 0x6941C9 cl=0 n=500
    //     0x6941F1 cl=0 n=100 | 0x6943DB cl=0 n=100 (结果存 [ebp-5])
    //   [sub_688650 (xref 0x623B02, 0x6D104B)]
    //     0x688726 cl=0 测试 n=100 (前置 cmp byte[ebx+0x72],2)
    //
    // 无证据分支 fail-closed: 本例程所有分支均有字节支撑, 未新增任何原生无据的副作用
    // (例如 background 分支【不】调用 WeightChanged —— 原生 sub_73EA20 背包删除臂只有 FreeAndNil)。
    // =====================================================================================
    public partial class TPlayObject
    {
        /// <summary>原生 `is TBujuk` (类引用 [0x75E3F8]) 的忠实判定: StdMode==25 && Shape==5。
        /// 见工厂 sub_74C338 Shape 跳表 @0x74D07B。</summary>
        private static bool IsBujukCharm(TUserItem item)
        {
            if (item == null || item.wIndex <= 0)
            {
                return false; // item==nil || [item+0x1C]==0
            }
            var std = M2Share.UserEngine.GetStdItem(item.wIndex);
            return std != null && std.StdMode == 25 && std.Shape == 5;
        }

        /// <summary>Native sub_73EA20. 在装备槽9 (优先) 或背包中查找 Dura>=nCount 的护身符(TBujuk);
        /// boConsume 时扣减【原始】nCount, 余量&gt;=100 发耐久变更包, 余量&lt;100 销毁(装备)/删除(背包)。
        /// 返回是否找到合格护身符 (原生 al=[ebp-6], 与是否消耗无关)。</summary>
        /// <param name="nCount">扣减/门槛量 (原始值, 无 ×100)。</param>
        /// <param name="boConsume">false=仅测试(原生 cl=0) / true=消耗(cl=1)。</param>
        public bool NativeConsumeBujukCharm(int nCount, bool boConsume)
        {
            if (YanshenConfig12Behaviors.AntiPoisonAmuletFree(this))
                return true;

            var found = false;        // [ebp-6]
            var foundInEquip = false; // [ebp-0xD]
            var foundInBag = false;   // [ebp-0xE]
            TUserItem item = null;    // [ebp-0xC]

            // --- 装备槽9 (U_BUJUK) ---  0x73EA50-0x73EA93
            var equipItem = m_UseItems[Grobal2.U_BUJUK];
            if (equipItem != null && IsBujukCharm(equipItem) && equipItem.Dura >= nCount)
            {
                item = equipItem;
                foundInEquip = true;
                found = true;
            }

            // --- 背包扫描 (仅当装备槽未命中) ---  0x73EA95-0x73EAF5
            if (!found && m_ItemList != null)
            {
                for (var i = 0; i < m_ItemList.Count; i++) // 原生: i=0..count-1 正向, 首个命中即 break
                {
                    var bagItem = m_ItemList[i];           // 0x73EAB4 TList.Get(i)
                    if (bagItem != null && IsBujukCharm(bagItem) && bagItem.Dura >= nCount)
                    {
                        item = bagItem;
                        foundInBag = true;
                        found = true;
                        break;                              // 0x73EAF1
                    }
                }
            }

            // --- 决策 ---  0x73EAF7 : al = found & boConsume
            if (found && boConsume)
            {
                item.Dura -= (ushort)nCount;                // 0x73EB0A sub word[+0x26],dx (RAW, 无 ×100)
                if (item.Dura >= 100)                       // 0x73EB15 cmp ax,0x64 / jb
                {
                    if (foundInEquip)                      // 0x73EB1B
                    {
                        // 0x73EB3D call 0x765E68 = SendMsg(self, RM_DURACHANGE, slot9, Dura, DuraMax, 0)
                        SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_BUJUK, item.Dura, item.DuraMax, 0, "");
                    }
                    else if (foundInBag)                   // 0x73EB44
                    {
                        SendUpdateItem(item);              // 0x73EB60 call [vmt+0x260]
                    }
                }
                else                                       // Dura < 100  (0x73EB68)
                {
                    if (foundInBag)
                    {
                        // 0x73EB73 gate sub_73D140 = bag.IndexOf(item)!=-1
                        if (m_ItemList.IndexOf(item) != -1)
                        {
                            SendDelItems(item);            // 0x73EB85 call [vmt+0x268]
                            // 0x73EB8E call 0x414C24 = FreeAndNil(item)。C# Dispose 为空模型
                            // (TBaseObject.cs:7183), 故显式 Remove 实现"背包中消失"的可观测行为。
                            Dispose(item);
                            m_ItemList.Remove(item);
                        }
                    }
                    else if (foundInEquip)                 // 0x73EB95
                    {
                        // 0x73EBB0 call 0x75F27C(slot9, channel 0xA, "持久耗尽")。与 sub_73E93C 同款销毁,
                        // 复用 ConsumeSpentPoisonCharm/UseAmulet 已 FAITHFUL 的 slot=null 模式。
                        m_UseItems[Grobal2.U_BUJUK] = null;
                        RecalcAbilitys();
                        SendDelItems(item);
                        var std = M2Share.UserEngine.GetStdItem(item.wIndex);
                        M2Share.AddNativeGameDataLog(this, 0x0A,
                            std == null ? string.Empty : std.Name,
                            item.MakeIndex, 1, "持久耗尽");
                    }
                }
            }

            return found; // 0x73EBD2 mov al,byte[ebp-6]
        }
    }
}
