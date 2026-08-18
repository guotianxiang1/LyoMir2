using SystemModule;

namespace GameSvr
{
    // =====================================================================================
    // HERO-MAGIC-1 — Native sub_73EA20 on the HERO receiver.
    //
    // 底本: D:\loym2\staging\_reunpack_work\flat_image.bin (ImageBase=0x400000), capstone 5.0.7.
    //
    // sub_73EA20 是 **THumanKind 的方法**(函数体位于 THumanKind 单元 0x73xxxx 段), 玩家与英雄
    // 共用同一份机器码。类树 (VMT 自指针 [vmt-0x4C]==vmt + 类名 [vmt-0x2C] 逐条枚举得到):
    //     TWarHero(0x685968) <- THeroAct(0x685630) <- THumanKind(0x73BC34) <- TCreature(0x764608)
    //     TPlayer(0x6AC8C8)                        <- THumanKind(0x73BC34) <- TCreature(0x764608)
    // 英雄四叶 TWarHero/TMagHero/TTaosHero/TSecWarHero/TSecMagHero/TSecTaosHero 全部继承
    // THeroAct 的两个物品通知覆写, 因此 sub_73EA20 在英雄身上的**唯一**差异就是那两个虚槽:
    //
    //   VMT 槽        THumanKind            THeroAct/英雄            玩家 TPlayer
    //   +0x260        0x73D384 (空 ret 8)   0x68CAF4  SM 0x39A=922   0x6D7974
    //   +0x268        0x73CBAC (SM 0xCA)    0x68CAC4  SM 0x38A=906   0x73CBAC (继承)
    //
    //   0x68CAC4 (英雄 SendDelItems, VMT+0x268):
    //     0068CACB  test edx,edx / je end            ; item=nil -> 什么都不发
    //     0068CACF  cmp dword [edx+0x1C],0 / je end  ; StdItem 指针为空 -> 什么都不发 (原生额外门)
    //     0068CAD5  push 0 / push 0 / push 1 / push 0
    //     0068CADD  mov ecx,[edx+0x18]               ; nRecog = MakeIndex
    //     0068CAE2  mov dx,0x38A                     ; SM_HERO_DELITEM = 906
    //     0068CAE8  call [vmt+0x250]                 ; = 0x6899F4 (THeroAct 转发到主人)
    //   0x68CAF4 (英雄 "SendUpdateItem"/耐久刷新, VMT+0x260, ret 8):
    //     0068CAFE  push ecx                         ; = 调用方 ecx = Dura
    //     0068CAFF  push word[ebp+0x0C]              ; = 调用方第 1 个栈参 = DuraMax
    //     0068CB04  push word[ebp+0x08]              ; = 调用方第 2 个栈参 = 0
    //     0068CB09  push 0
    //     0068CB0B  mov dx,0x39A                     ; SM_HERO_BAGITEMDURACHG = 922
    //     0068CB11  mov ecx,edi (=调用方 edx = MakeIndex)
    //     0068CB15  call [vmt+0x250]
    //   0x6899F4 (THeroAct VMT+0x250): master=[self+0x68C]; 要求 master!=nil 且 byte[master+0x73]==0,
    //     然后把 4 个栈参 + ecx + edx(ident) 原样转发到 master 的 [vmt+0x250]。
    //     == C# HeroObject.SendToMaster(...) -> master.SendDefMessage(...)。
    //
    //   => C# 侧英雄的这两个槽已经存在且语义一致:
    //        VMT+0x260 == HeroObject.SendHeroBagItemDuraChange(item)   (SM_HERO_BAGITEMDURACHG=922)
    //        VMT+0x268 == HeroObject.SendHeroDelItem(item)             (SM_HERO_DELITEM=906)
    //      故本文件**不新增**任何原生无据的发包, 只是把 DURA-11 已勘定的 sub_73EA20 例程体
    //      接到英雄的两个覆写上。装备槽分支的 RM_DURACHANGE(0x278D) 走的是 **非虚** 的
    //      TCreature.SendMsg(sub_765E68), 英雄与玩家完全同一条路径。
    //
    // ---- 例程体逐地址 (自行复核, 与 TPlayObject.NativeAmuletConsume.cs 同源) ----
    //   0x73EA50  mov dl,9 / mov eax,[esi+0x4C0] / call 0x75EC20   ; GetUseItem(9=U_BUJUK)
    //   0x73EA60..0x73EA93  item!=nil && [item+0x1C]!=0 && `is TBujuk`([0x75E3F8]) &&
    //                       word[item+0x26] >= nCount  -> inEquip=1, found=1
    //   0x73EA95  if(!found) 正向扫描背包 [esi+0x508] (0x73EAB4 TList.Get(i)), 同样四检,
    //                       首个命中 -> inBag=1, found=1, break(0x73EAF1)
    //   0x73EAF7  al = found & boConsume; je 尾部
    //   0x73EB0A  sub word[item+0x26], dx        ; Dura -= nCount  (原始量, 无 x100)
    //   0x73EB15  cmp ax,0x64 / jb
    //     Dura>=100: 0x73EB1B inEquip -> 0x73EB3D SendMsg(self,RM_DURACHANGE,9,Dura,DuraMax,0)
    //                0x73EB44 inBag   -> 0x73EB60 call [vmt+0x260]   (英雄: SM 922)
    //     Dura<100 : 0x73EB68 inBag   -> 0x73EB73 sub_73D140(bag.IndexOf!=-1)
    //                                 -> 0x73EB85 call [vmt+0x268]   (英雄: SM 906)
    //                                 -> 0x73EB8E FreeAndNil(item)
    //                0x73EB95 inEquip -> 0x73EBB0 sub_75F27C(slot9, 频道 0xA, "持久耗尽"@0x73EBE4)
    //   0x73EBD2  返回 al = [ebp-6] = found (与是否消耗无关)
    //
    // 英雄侧的 10 个调用点全部在 sub_68DD88 (英雄逐技能分派器) 内, 见 HeroObject.NativeDoSpell.cs。
    // =====================================================================================
    public partial class HeroObject
    {
        /// <summary>原生 `is TBujuk` (类引用 [0x75E3F8] = VMT 0x75E444 'TBujuk') 的忠实判定。
        /// 物品工厂 sub_74C338 的 StdMode 25 Shape 跳表 @0x74D07B: Shape 5 -> new TBujuk
        /// (@0x74D0C0), Shape 1/2 -> new TPoisons (@0x74D0AA)。TPoisons 与 TBujuk 同父
        /// TEquipBujuk 而非派生关系, 故 `is TBujuk` 不含 TPoisons。
        /// 0x73EA69 `cmp [item+0x1C],0` == C# GetStdItem(wIndex)!=null。</summary>
        private static bool IsHeroBujukCharm(TUserItem item)
        {
            if (item == null || item.wIndex <= 0)
            {
                return false;
            }
            var std = M2Share.UserEngine.GetStdItem(item.wIndex);
            return std != null && std.StdMode == 25 && std.Shape == 5;
        }

        /// <summary>Native sub_73EA20 with the HERO receiver (THeroAct VMT+0x260/+0x268 overrides).
        /// 在装备槽9 (优先) 或背包中查找 Dura&gt;=nCount 的护身符(TBujuk); boConsume 时扣减
        /// 【原始】nCount, 余量&gt;=100 发耐久变更, 余量&lt;100 销毁(装备)/删除(背包)。
        /// 返回是否找到合格护身符 (原生 al=[ebp-6], 与是否消耗无关)。</summary>
        /// <param name="nCount">扣减/门槛量 (原始值, 无 x100)。</param>
        /// <param name="boConsume">false=仅测试(原生 cl=0) / true=消耗(cl=1)。</param>
        public bool NativeConsumeBujukCharm(int nCount, bool boConsume)
        {
            var found = false;        // [ebp-6]
            var foundInEquip = false; // [ebp-0xD]
            var foundInBag = false;   // [ebp-0xE]
            TUserItem item = null;    // [ebp-0xC]

            // --- 装备槽9 (U_BUJUK) ---  0x73EA50-0x73EA93
            var equipItem = m_UseItems == null ? null : m_UseItems[Grobal2.U_BUJUK];
            if (equipItem != null && IsHeroBujukCharm(equipItem) && equipItem.Dura >= nCount)
            {
                item = equipItem;
                foundInEquip = true;
                found = true;
            }

            // --- 背包扫描 (仅当装备槽未命中) ---  0x73EA95-0x73EAF5
            if (!found && m_ItemList != null)
            {
                for (var i = 0; i < m_ItemList.Count; i++) // 原生正向 i=0..count-1, 首个命中即 break
                {
                    var bagItem = m_ItemList[i];           // 0x73EAB4 TList.Get(i)
                    if (bagItem != null && IsHeroBujukCharm(bagItem) && bagItem.Dura >= nCount)
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
                item.Dura -= (ushort)nCount;                // 0x73EB0A sub word[+0x26],dx (无 x100)
                if (item.Dura >= 100)                       // 0x73EB15 cmp ax,0x64 / jb
                {
                    if (foundInEquip)                      // 0x73EB1B
                    {
                        // 0x73EB3D call 0x765E68 (非虚 TCreature.SendMsg), 玩家/英雄同路径
                        SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_BUJUK, item.Dura, item.DuraMax, 0, "");
                    }
                    else if (foundInBag)                   // 0x73EB44
                    {
                        // 0x73EB60 call [vmt+0x260] -> 英雄覆写 0x68CAF4 -> SM_HERO_BAGITEMDURACHG(922)
                        SendHeroBagItemDuraChange(item);
                    }
                }
                else                                       // Dura < 100  (0x73EB68)
                {
                    if (foundInBag)
                    {
                        // 0x73EB73 gate sub_73D140 = bag.IndexOf(item)!=-1
                        if (m_ItemList != null && m_ItemList.IndexOf(item) != -1)
                        {
                            // 0x73EB85 call [vmt+0x268] -> 英雄覆写 0x68CAC4 -> SM_HERO_DELITEM(906),
                            // 该覆写自带 `cmp [item+0x1C],0` 门 (StdItem 为空则不发包)。
                            if (M2Share.UserEngine.GetStdItem(item.wIndex) != null)
                            {
                                SendHeroDelItem(item);
                            }
                            // 0x73EB8E call 0x414C24 FreeAndNil(item)。C# Dispose 为空模型,
                            // 故显式 Remove 实现"背包中消失"的可观测行为 (与玩家侧同款)。
                            Dispose(item);
                            m_ItemList.Remove(item);
                        }
                    }
                    else if (foundInEquip)                 // 0x73EB95
                    {
                        // 0x73EBB0 call 0x75F27C(slot9, 频道 0xA, "持久耗尽"@0x73EBE4)。
                        // 销毁器内部: 清槽 -> [vmt+0x8C] RecalcAbilitys -> `is THumanKind`
                        // ([0x73BBE8]; TWarHero 是 THumanKind 的一支, 通过) -> [vmt+0x268]
                        // (英雄 = SM_HERO_DELITEM) -> AddGameDataLog(频道 0xA) ->
                        // 辅助槽 {0,1,4,13} 外观过滤 (槽9 不在其中) -> Free。
                        m_UseItems[Grobal2.U_BUJUK] = null;
                        RecalcAbilitys();
                        if (M2Share.UserEngine.GetStdItem(item.wIndex) != null)
                        {
                            SendHeroDelItem(item);
                        }
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
