using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    // === HeroSpiritBead subsystem ===
    //
    // Faithful port of the two hero item CMs that cm-2 left blind-dropped in
    // TPlayObject.NativeCmProtocol_Q2.cs:
    //
    //   CM 1291  leaf 0x6DA3CA -> worker 0x69059C   英雄灵珠 (THeroHighExpItem)
    //   CM 1316  leaf 0x6DAACF -> worker 0x746908   英雄生肖/神佑袋镶嵌 (TAnimalMascot)
    //
    // This partial OWNS both idents through TryHandleHeroSpiritBeadCm(), which the
    // integrator wires in BEFORE TryHandleNativeCmQ2 (see the hook remark). The two
    // Q2 cases in cm-2's file therefore become shadowed; that file is left untouched.
    //
    // Disposition (每条腿都逐字节反汇编，不信旧账): every gate that the image
    // evaluates BEFORE the first unported call — and that this port can evaluate
    // from modelled state — is reproduced 1:1 (hero presence, CM 1316 Series
    // routing). The terminal actions are fail-closed because they reach subsystems
    // that have no C# model, and any reply/return this port could build there would
    // put invented bytes on the wire (see the per-CM blockers below). The mask math
    // itself IS reproduced as callable, byte-faithful helpers (see HeroZodiac*), but
    // it is NOT committed by the handler: the surrounding cluster (login-load of the
    // mask, the attribute recompute, the SM 0xFC1 broadcast, persistence, and the
    // SM 0xCFD Param) is unmodelled, so half-applying the OR would desync rather than
    // reproduce 战神.
    //
    // ------------------------------------------------------------------------------
    // 数据结构三件套 (offset -> C# 映射)
    // ------------------------------------------------------------------------------
    //
    // (1) 门 GATES — leaf-level, reproducible
    //   [self+0xBB0]  英雄对象指针      -> m_HeroObject (null == 无英雄)
    //   msg+0x00      Recog             -> TProcessMessage.nParam1
    //   msg+0x06      Param             -> TProcessMessage.nParam2
    //   msg+0x08      Tag               -> TProcessMessage.nParam3
    //   msg+0x0A      Series            -> TProcessMessage.wParam
    //   CM 1291 leaf 0x6DA3CA: [self+0xBB0]==0 -> je 0x6DBC2C (原生静默); 否则
    //                worker(EAX=self.Hero, EDX=Recog, CX=Param, push 0).
    //   CM 1316 leaf 0x6DAACF: Series==1 -> 有英雄 worker(EAX=self.Hero,...) 否则静默;
    //                Series==0 -> worker(EAX=self,...); Series>=2 -> 静默.
    //
    // (2) 英雄灵珠链 SPIRIT-BEAD (CM 1291 worker 0x69059C) — 终末 fail-closed
    //   0x69079C(Hero) 可提升门: word[hero+0x278]=Level(<0x3E7), byte[hero+0x514]=职业(==2?),
    //                dword[hero+0x68C]=主人玩家(owner), word[owner+0x1880]=上限.
    //                假 -> SysMsg 0x38FF "您的英雄已经无法再获得提升"@0x690720.
    //   0x73CF08(Hero,Recog) 取 [hero+0x508] 链中 [item+0x18]==Recog 的物品; 空/类不符 -> 静默.
    //   is THeroHighExpItem  -> class ref [0x780A74] (StdMode 33; NativeItemFactory).
    //   [item+0x1C]=StdItem; word[StdItem+0x1E]=灵珠等级(0x7892F8); dword[StdItem+0x4C]=经验值(0x7892F0).
    //   等级>0 && Param==2: 荣耀确认 — 0x6E1FBC(owner, 0x278B, 0x64, 1, "开启白日门灵珠"@0x690744, 等级)
    //                从 [owner+0x1824] 灵符/荣耀账户扣减; 不足 -> SysMsg 0x38FF
    //                "您没有足够的荣耀点, 无法开启灵珠"@0x69075C.
    //   等级>0 && Param!=2: [owner+0x18B0]=[item+0x18]; 0x6E1BF8(owner,0x278B,名) 弹确认框.
    //   终末: 0x687714(Hero, dword[StdItem+0x4C], 1) 加英雄经验(word[hero+0x2BC]); SM 0xA
    //                "使用灵珠获取经验值"@0x690788 (0x768BE0); [Hero.vmt+0x24C] 移除物品; Free.
    //   BLOCKERS: word[owner+0x1880] 未建模 -> 首门 0x69079C 无法完整求值; 荣耀账户
    //                [owner+0x1824] 与身份串 [owner+0xAF4]/[+0xB09]/[+0x106]/[+0xB33] 未建模;
    //                英雄加经验 0x687714 未移植; SM 0xA/0x278B body 无法推导 -> fail-closed.
    //
    // (3) 神佑袋/生肖掩码集群 ZODIAC-MASK (CM 1316 worker 0x746908) — 掩码位运算已复现,
    //     终末 fail-closed
    //   [self+0x60C]  dword 生肖位掩码   -> HeroZodiacBlessMask  (1<<(生肖-1))
    //   [self+0x610]  dword 神佑袋门控    -> HeroZodiacBlessGate  (0x747CF4 `cmp dword[+0x610],0/jg`)
    //   [self+0x59C]/[+0x5A0]/[+0x5A4]    神佑袋派生属性 (0x747CF4 由 [0x60C] 奇/偶位 popcount 求)
    //   [self+0x5A8]  10 word 数组         神佑袋槽 (0x747CF4/0x74730C)
    //   0x73CF08(target,Recog) 取 [target+0x508] 链; 空 -> SM 0xCFD "找不到镶嵌的饰物"@0x746C14.
    //   is TAnimalMascot -> class ref [0x7825C8] (StdMode 62; NativeItemFactory).
    //   byte[StdItem+0x15]=Shape=生肖(1..12) -> GoodItem.Shape.
    //   0x789C90 校验: target is THumanKind([0x73BBE8]); Tag<=6; sub_789CFC 该位未置
    //                (读 [target+0x60C], Shape=[StdItem+0x15]); Tag==byte[StdItem+0x17]-8.
    //                假 -> SM 0xCFD "您只能镶嵌对应属相的饰物！"@0x746BF0.
    //   置位: [target+0x60C] |= 1<<(Shape-1)  (0x74697D).
    //   0x789C50(Shape) 跳表 -> 1/2/其它 选 "您在神佑袋中镶嵌了："@0x746B78 /
    //                "您在极品神佑袋中镶嵌了："@0x746B98 / "shape值异常"@0x746BBC(记日志).
    //   0x747CF4 由 [0x60C] 重算派生属性(门控 [0x610]>0); 0x74730C 广播 SM 0xFC1.
    //   SM 0xCFD: Recog=[target+0x60C], Param=word[target+0x610], sMsg=文本 (target.vmt+0x250);
    //                [target.vmt+0x24C] 移除物品; Free.
    //   BLOCKERS: 持久化源已由 SoulWash.cs 映射为 record+0x580/+0x57C，登录 SM 3324
    //                也已从该源恢复；但本文件的会话属性仍未接到完整镶嵌提交链，且派生属性
    //                [0x59C..0x5A8]/重算 0x747CF4/广播 SM 0xFC1 未建模。因此掩码提交
    //                仍缺重算/广播/会话状态衔接 -> fail-closed.
    //
    //     另: 0x73CF08 比较 dword[item+0x18]==Recog; item+0x18 在 C# 模型里究竟对应
    //     MakeIndex 还是 ClientItemID 两处注释相互矛盾 (TUserItem.ClientItemID vs
    //     NativeFixedCoordStone 的 0x73CF08 端口), 解析键不可证, 属未建模物品链 -> 本处
    //     不猜键、不解析物品, 直接以证据齐备的 leaf 路由收口到 fail-closed.
    // ------------------------------------------------------------------------------
    public partial class TPlayObject
    {
        // ---- 建字段: 神佑袋/生肖掩码 (offsets [self+0x60C]/[self+0x610]) ----
        // RestoreNativeHeroZodiacState/PersistNativeHeroZodiacState now map these live
        // session fields to record+0x580/+0x57C exactly. The inlay commit chain itself
        // remains fail-closed until its recompute and SM 0xFC1 broadcast are complete.

        /// <summary>
        /// 生肖位掩码 <c>dword[self+0x60C]</c>. Bit (生肖-1) 置 1 表示该生肖已镶嵌。
        /// 镶嵌 worker 0x74697D <c>or [self+0x60C], 1&lt;&lt;(Shape-1)</c> 写, SM 0xCFD /
        /// SM 3324/3325 / 重算 0x747CF4 读。见类注释 (3)。
        /// </summary>
        internal uint HeroZodiacBlessMask { get; set; }

        /// <summary>
        /// 神佑袋门控 <c>dword[self+0x610]</c>. 0x747CF4 <c>cmp dword[+0x610],0 / jg</c>
        /// 决定派生属性是否生效; SM 0xCFD 取其低 word 作 Param。持久化源为
        /// record+0x57C，登录时按 0x6B060A 将非正值归一为 1，保存时原值写回。
        /// </summary>
        internal int HeroZodiacBlessGate { get; set; }

        // ---- 逻辑 1:1 复刻: 生肖掩码位运算 (worker 0x74697B/0x74697D, sub_789CFC) ----

        /// <summary>
        /// 生肖 shape (1..) 对应的掩码位: worker 0x74697B <c>mov eax,1; mov cl,Shape-1;
        /// shl eax,cl</c>, 与 sub_789CFC 同式。shape&lt;=0 或越界返回 0 (native cl 只取低 5 位;
        /// 这里以 0 收口, 越界即无位)。
        /// </summary>
        internal static uint HeroZodiacShapeBit(int shape)
            => shape <= 0 || shape > 32 ? 0u : 1u << (shape - 1);

        /// <summary>
        /// 该生肖是否已镶嵌: sub_789CFC <c>mov eax,[self+0x60C]; shr eax,(Shape-1); and eax,1</c>。
        /// </summary>
        internal bool HeroZodiacShapeInlaid(int shape)
            => shape > 0 && (HeroZodiacBlessMask & HeroZodiacShapeBit(shape)) != 0;

        /// <summary>
        /// 神佑袋派生属性是否生效: 0x747CF4 首句 <c>cmp dword[self+0x610],0 / jg</c>。
        /// </summary>
        internal bool HeroZodiacBonusActive => HeroZodiacBlessGate > 0;

        /// <summary>
        /// 忠实复现镶嵌置位 worker 0x74697D <c>or [self+0x60C], 1&lt;&lt;(Shape-1)</c>。
        /// 处理器【不】调用它: 环绕提交 (重算 0x747CF4 / 门控 [0x610] / 广播 SM 0xFC1 /
        /// 落盘 / SM 0xCFD 的 Param) 均未建模, 半提交会 desync 而非复现 (见类注释 (3))。
        /// 保留为掩码变更的逐位复刻, 待神佑袋集群建模后由完整链调用。
        /// </summary>
        internal void ApplyHeroZodiacInlayBit(int shape)
            => HeroZodiacBlessMask |= HeroZodiacShapeBit(shape);

        // ------------------------------------------------------------------
        // 挂钩 (integrator): 本方法接管 CM 1291/1316, 必须插在 TryHandleNativeCmQ2
        // 【之前】。本基线 TPlayObject.Message.cs::Operate 的 default: 已是短路 && 链,
        // 在 Q2 前加一项即可 (Message.cs 属他人/主体文件, 不在本轮改动, 仅此说明) ——
        //     default:
        //         if (!TryHandleNativeSocialProtocol(ProcessMsg)
        //             && !TryHandleNativeCmTailProtocol(ProcessMsg)
        //             && !TryHandleNativeCmQ1(ProcessMsg)
        //             && !TryHandleHeroSpiritBeadCm(ProcessMsg)   // ← 新增(插 Q2 前)
        //             && !TryHandleNativeCmQ2(ProcessMsg)
        //             && !TryHandleNativeCmQ3(ProcessMsg))
        //         {
        //             result = base.Operate(ProcessMsg);
        //         }
        //         break;
        // 只对它拥有的两个 ident 返回 true; cm-2 的 Q2 里同名两个 case 因此被短路屏蔽,
        // 那份文件保持不改。未接线时 CM 1291/1316 仍回落到 cm-2 的 Q2 (同为 fail-closed),
        // 无回归。
        // ------------------------------------------------------------------
        private bool TryHandleHeroSpiritBeadCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_1291:
                    ClientNativeHeroSpiritBead();
                    return true;
                case Grobal2.CM_1316:
                    ClientNativeHeroZodiacInlay(processMessage.wParam);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 1291, leaf 0x6DA3CA (<c>8B 45 FC / 8B 80 B0 0B 00 00 / 85 C0 / 0F 84.. je</c>),
        /// worker 0x69059C — 英雄灵珠 (THeroHighExpItem)。
        /// Leaf gate: 无英雄 (<c>[self+0xBB0]==0</c>) -> 原生静默 (0x6DA3D5 je 0x6DBC2C)。
        /// worker 首门 0x69079C 依赖未建模的 <c>word[owner+0x1880]</c> 上限, 无法完整求值;
        /// 其后荣耀账户 <c>[owner+0x1824]</c>、英雄加经验 0x687714、SM 0xA/0x278B body 均未
        /// 建模 -> 终末 fail-closed (见类注释 (2))。
        /// </summary>
        private void ClientNativeHeroSpiritBead()
        {
            if (m_HeroObject == null)
            {
                return; // 0x6DA3D5 je 0x6DBC2C — 无英雄, 原生静默
            }

            // 首门 0x69079C 起即触未建模子系统, 无从复现任一终末腿。
            NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1291, m_sCharName);
        }

        /// <summary>
        /// CM 1316, leaf 0x6DAACF (<c>mov ax,[msg+0xA] / cmp ax,1</c> 双分支), worker 0x746908
        /// — 英雄生肖/神佑袋镶嵌 (TAnimalMascot)。
        /// Leaf 路由 1:1: Series==1 时有英雄走 worker(Hero)、无英雄静默; Series==0 走
        /// worker(self); Series&gt;=2 静默。两条 worker 腿的终末动作 (置位 [self+0x60C]、
        /// 重算、广播 SM 0xFC1、SM 0xCFD) 依赖尚未完整接线的神佑袋集群
        /// -> fail-closed (见类注释 (3))。
        /// </summary>
        /// <param name="nSeries">Series = word[msg+0xA] = TProcessMessage.wParam。</param>
        private void ClientNativeHeroZodiacInlay(int nSeries)
        {
            if (nSeries == 1)
            {
                if (m_HeroObject == null)
                {
                    return; // 0x6DAAE6 je 0x6DBC2C — Series==1 但无英雄, 原生静默
                }

                // 0x6DAB05 worker(EAX=self.Hero, ...): 英雄生肖镶嵌。神佑袋集群未建模。
                NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1316, m_sCharName);
                return;
            }

            if (nSeries != 0)
            {
                return; // 0x6DAB12 jne 0x6DBC2C — Series>=2, 原生静默
            }

            // 0x6DAB2F worker(EAX=self, ...): 玩家自身生肖镶嵌 (cm-2 曾漏此腿而误静默)。
            // 同一 worker 0x746908、同一神佑袋集群未建模。
            NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1316, m_sCharName);
        }
    }
}
