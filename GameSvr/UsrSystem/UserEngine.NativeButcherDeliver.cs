using SystemModule;

namespace GameSvr
{
    public partial class UserEngine
    {
        /// <summary>
        /// 战神 <c>sub_71EC88</c>（0x71EC88–0x71ED7F）——「掉落直入杀手背包」交付路径（DROP-35 / DROP-36）。
        ///
        /// <para>语境：动物（<c>m_boAnimal</c>）死亡后不做地面散落（monster Die 的 <c>!m_boAnimal</c>
        /// 门，<c>TBaseObject.Base.cs</c>），战利品只能靠"挖肉"取得。玩家发 CM_BUTCH，原生
        /// <c>sub_6B8A94</c>（唯一发 RM_BUTCH=0x2787 者）做距离≤2 校验后
        /// <c>0x6B8AE6 call [animal_vmt+0x98]</c> 派发到 TAnimal VMT 槽 <c>+0x98</c> =
        /// <c>sub_71ED80</c>（self=animal，edx=player）。<c>sub_71ED80</c> 分支 A
        /// (<c>m_boAnimal</c>) 每次砍击减皮革(<c>+0x4A4</c>=<c>m_nBodyLeathery</c>)与
        /// 肉质(<c>+0x4A0</c>=<c>m_nMeatQuality</c>)，耗尽时（RC_ANIMAL≤race&lt;RC_MONSTER 则
        /// 骷髅化并发 RM_SKELETON=0x2728）<c>0x71EE5C call sub_71EC88</c> —— 即本方法，把怪物
        /// **自身掉落表**（<c>[self+0x474]</c>=<see cref="TMonItem"/> 表，同
        /// <c>UserEngine.MonGetRandomItems</c> 读的 <c>TMonInfo.ItemList</c>）逐项掷点后
        /// **直接塞进砍杀者背包**，而非散落地面。</para>
        ///
        /// <para>唯一原生调用者 <c>0x71EE5C</c>（属 <c>sub_71ED80</c>）。返回值语义
        /// (<c>[ebp-5]</c>)：**只要成功新建了任意一件物品即为 true**（即使背包满被丢弃），
        /// 一件都没掷出/建出时才 false —— 调用方据此决定是否发"未获取任何物"
        /// (native <c>0x71EE61 test al,al / jne</c> 跳过失败提示)。</para>
        /// </summary>
        /// <param name="mon">死亡的动物（原生 self=eax=ebx）：掉落表宿主 + 肉质耐久来源。</param>
        /// <param name="killer">砍杀者（原生 edx）：接收物品进背包的对象。</param>
        /// <returns>是否成功新建过至少一件物品。</returns>
        public bool MonDeliverDropTableToKillerBag(TBaseObject mon, TBaseObject killer)
        {
            // 0x71EC96  C6 45 FB 00           mov byte [ebp-5],0        ; result = false
            var result = false;

            // 0x71EC9A  83 7D FC 00 / je 0x71ED76 —— killer(edx→[ebp-4]) 为空直接失败。
            if (mon == null || killer == null)
            {
                return false;
            }

            // 0x71ECA4  83 BB 74 04 00 00 00  cmp dword [self+0x474],0 / 0x71ECAB je 0x71ED76
            // [self+0x474] = 模板 TMonInfo.ItemList 的直引用（0x71EB5E mov eax,[esi+0x48] /
            // 0x71EB61 mov [ebx+0x474],eax）；nil 指针 = 无表（非空表），与
            // UserEngine.NativeHasMonsterDropTable / MonGetRandomItems 同判据。
            if (!TryGetMonsterInfo(mon.m_sCharName, out var monInfo) || monInfo.ItemList == null)
            {
                return false;
            }

            // 0x71ECB1  80 BB 7F 04 00 00 00  cmp byte [self+0x47F],0 / 0x71ECB8 jne 0x71ED76
            // 0x71ECBE  C6 83 7F 04 00 00 01  mov byte [self+0x47F],1
            // 一次性哨兵 m_boNativeScatterConsumed —— 与地面散落 sub_71FA20 @0x71FA6C 共享
            // （见 TBaseObject.Base.cs TryEnterNativeScatter，测试+置位同形）：谁先跑谁烧哨兵，
            // 掉落表每实例只消费一次。动物不地面散落，故挖肉时哨兵通常仍为 0。
            if (mon.m_boNativeScatterConsumed)
            {
                return false;
            }
            mon.m_boNativeScatterConsumed = true;

            // 0x71ECC5  mov eax,[self+0x474] / 0x71ECCB mov esi,[eax+8]=Count / dec/jl 空表返回
            var itemList = monInfo.ItemList;
            for (var i = 0; i < itemList.Count; i++)                 // 0x71ECDF..0x71ED70 逐项
            {
                // 0x71ECE8  call 0x424D4C = TList.Get(i) / 0x71ECF0 cmp/je 跳过 nil 项。
                var monItem = itemList[i];
                if (monItem == null)
                {
                    continue;
                }

                // 0x71ECF6  mov eax,[entry+0x14]=MaxPoint / 0x71ECFC call 0x403B4C(Random) /
                // 0x71ED04 cmp eax,[entry+0x10]=SelPoint / 0x71ED07 jg next —— 掷点>SelPoint 不掉。
                // 注意：交付路径**不乘**防沉迷倍率（只有地面 MonGetRandomItems 才 *penalty）。
                // RNG 无条件抽取（含 nil StdItem 的项也抽），故先掷点再建物，与 native 顺序一致。
                if (M2Share.RandomNumber.Random(monItem.MaxPoint) > monItem.SelPoint)
                {
                    continue;
                }

                // 0x71ED0C  mov eax,[entry+0x18]=StdItem 指针 / 0x71ED11 je → item=null（result 不置）。
                // 0x71ED1E  call 0x751BBC(UserEngine, StdItem, ecx=0) → 工厂 0x74C338 建 TUserItem，
                // makeIndex=0 由 0x74DAD0 自动生成。姊妹路径（MonGetRandomItems）按名建同件，
                // CopyToUserItemFromName（name→StdItem 解析后构造，makeIndex 缺省=0）为等价物。
                TUserItem userItem = null;
                if (!CopyToUserItemFromName(monItem.ItemName, ref userItem))
                {
                    continue;                                        // 0x71ED31 cmp [ebp-0x10],0 / je next
                }

                // 0x71ED4A  C6 45 FB 01           mov byte [ebp-5],1 —— 一旦建出物品即 true（入包之前）。
                result = true;

                // 0x71ED36  80 78 14 07           cmp byte [item+0x14],7 / 0x71ED3A je 跳过耐久回填。
                // 0x71ED3F  66 8B 93 A0 04 00 00  mov dx,[self+0x4A0]=m_nMeatQuality
                // 0x71ED46  66 89 50 26           mov [item+0x26]=Dura ← 肉质。DROP-36 交付路径专有。
                // 「实例 KIND 字节 +0x14==7」= 堆叠物；映射谓词 NativeItemFactory.IsPileItem
                // （全项目一致，见 ECON-12/CRAFT-26：pile ctor 0x788118 写 [+0x14]=7）。
                var stdItem = GetStdItem(userItem.wIndex);
                if (!NativeItemFactory.IsPileItem(stdItem))
                {
                    userItem.Dura = mon.m_nMeatQuality;
                }

                // 0x71ED4E  push 0 / 0x71ED50 mov cl,1 / 0x71ED5A call [killer_vmt+0x248]
                //   = killer.AddItemToBag(item, ...). Native AddItemToBag (sub_6B7378):
                //   ECX (param2) is stampEnable — @0x6B739F `test al,bl` gates the stamper
                //   on it — and the pushed stack arg (param3) is reason. So `mov cl,1`
                //   = stampEnable=TRUE, `push 0` = reason=0. C# param order is
                //   (UserItem, reason, stampEnable) → (userItem, 0, true). The earlier
                //   (1, false) had both values inverted, so butchered loot went unstamped.
                // 0x71ED60  test al,al / 0x71ED62 jne skip —— 入包失败：0x71ED67 call 0x404690(Free)。
                // DROP-35：**失败即丢弃物品，绝不落地兜底**。C# 中未挂接的 userItem 由 GC 回收，
                // 等价 Free；返回值仅决定 native 是否 Free，此处无需分支（result 已在建物时置位）。
                killer.AddItemToBag(userItem, 0, true);
            }

            // 0x71ED76  8A 45 FB              mov al,[ebp-5] —— 返回是否建出过任意物品。
            return result;
        }
    }
}
