using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 镶嵌子系统 — 神珠 (spirit-bead) 与 宝玉 (jade) 的 CM 4408/4409/4410/4411.
    ///
    /// 分发 (sub_6D7D68 的尾段, 见 TPlayObject.NativeCmTailProtocol.cs 头注):
    ///   CM 4408 leaf 0x6DB08A -> 0x6F37EC(DL=0 本人)   神珠镶嵌
    ///   CM 4410 leaf 0x6DB0D0 -> 0x6F37EC(DL=1 英雄)   神珠镶嵌 (共用 worker)
    ///   CM 4409 leaf 0x6DB0B2 -> 0x6F38A8(DL=0 本人)   宝玉合成
    ///   CM 4411 leaf 0x6DB0F8 -> 0x6F38A8(DL=1 英雄)   宝玉合成 (共用 worker)
    /// leaf 传参 (Delphi register call, EAX=Self, DL=selector):
    ///   神珠: Recog=[rec+0]->ECX, MakeLong(Param=word[rec+6], Tag=word[rec+8]) 压栈 (0x408D40)
    ///   宝玉: Param=word[rec+6]->ECX, body length ESI 压栈, body 串 [ebp-8] 压栈
    /// C# 侧对应 nParam1=Recog / nParam2=Param / nParam3=Tag / sMsg=body / nBodyLen=len.
    ///
    /// ===== worker 外壳 (完全可证, 1:1 复刻) =====
    /// 两个 worker 结构一致:
    ///   result = -99 (0xFFFFFF9D)                            0x6F37FD / 0x6F38B9
    ///   [神珠额外] if m_boDealing([self+0x461]) 或 !sub_6C7D88(self): result=-98  0x6F3802/0x6F381A
    ///   target = selector==0 ? Self : 有效英雄               0x6F381F / 0x6F38BE
    ///     英雄 = [Self+0xBB0] (m_HeroObject) 且 !IsDead(sub_772DA8=[hero+0x74]=m_boDeath)
    ///            且 !ghost([hero+0x73]=m_boGhost)             0x6F3825.. / 0x6F38C4..
    ///   if target!=null: result = 镶嵌链(神珠 0x7487A8 / 宝玉 0x748A18)   0x6F385E / 0x6F3901
    ///   SendDefMessage(SM=CM, Recog=result, 0,0,0,"")        [vmt+0x250]
    ///     神珠 SM 0x1138(本人)/0x113A(英雄); 宝玉 SM 0x1139(本人)/0x113B(英雄)
    ///
    /// ===== 镶嵌链结果码 (反汇编所得) =====
    /// 神珠 0x7487A8(target, EDX=Recog, ECX=MakeLong):
    ///   -1 物品(装备)未找到/类型不符  -2 神珠未找到/类型不符  -3 sub_78BD28 不兼容
    ///   -4 sub_78BF44 取神珠码失败    -5 该神珠码不允许镶入此装备(位掩码)
    ///   -6 装备已镶满 ([item+0xBE]>=3) -7/-8/-9 sub_78BD70 挂载子结果   0 成功
    /// 宝玉 0x748A18(target, EDX=Param, ECX=body, len):
    ///   -1 Param<5 (0x748A43)  -2 len<0x14 (0x748A4C)  -3 非 5 颗同属性神珠 (0x748AFC)
    ///   -4 入包失败 (0x748C0C)  0 成功 (合成: 消耗 5 神珠 -> 生成 1 宝玉)
    ///
    /// ===== 不可证边界 (fail-closed, 绝不臆造) =====
    /// 神珠挂载 sub_78BD70 -> sub_78C5EC 写运行期镶嵌属性 item+0x100..0x102, 取自
    /// 运行期镶嵌属性表 [0x7DCBDC] (stride 0x75); 该表在静态镜像里全 0, 由启动时从
    /// 本移植看不到的数据填充 -> 神珠成功/挂载路径全部不可证.
    /// 宝玉合成从 body 20 字节取 5 个神珠 ClientItemID, 读元素 (sub_78C73C=btValue[12]) 与
    /// 品级 (sub_78C72C=std+0x15==3), 由模板表 [0x7D3F34] + 物品工厂 [[0x7D5D6C]]->0x74DE54
    /// 造出宝玉并回发 SM 0x27A4(10148); 模板/工厂/运行期属性均未建模 -> 合成路径不可证.
    ///
    /// ===== 挂钩 =====
    /// 本文件只提供 <see cref="TryHandleInlayCm"/> 挂钩, 不改共享分发文件. 集成时由父级
    /// 把 CM 4408-4411 路由到这里 (替换 TPlayObject.NativeCmTailProtocol.cs 里那 4 个
    /// fail-closed 桩), 例如在 TPlayObject.Message.cs 的 Operate default 臂:
    ///     if (!TryHandleNativeSocialProtocol(ProcessMsg)
    ///         &amp;&amp; !TryHandleInlayCm(ProcessMsg)
    ///         &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg))
    ///     { result = base.Operate(ProcessMsg); }
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>worker 默认结果码 0xFFFFFF9D (神珠 0x6F37FD / 宝玉 0x6F38B9), 无有效目标时回发.</summary>
        private const int InlayResultNoTarget = -99;

        /// <summary>宝玉 Param&lt;5: 0x748A43 `cmp edx,5; jl 0x748C36`.</summary>
        private const int JadeResultParamTooSmall = -1;

        /// <summary>宝玉 body 长度&lt;0x14: 0x748A4C `cmp [ebp+8],0x14; jl 0x748C2D`.</summary>
        private const int JadeResultBodyTooShort = -2;

        /// <summary>宝玉最少神珠数 (Param 门): 0x748A43 `cmp edx,5`.</summary>
        private const int JadeMinBeadParam = 5;

        /// <summary>宝玉最少 body 字节数 (5 个 dword ClientItemID): 0x748A4C `cmp [ebp+8],0x14`.</summary>
        private const int JadeMinBodyLen = 0x14;

        /// <summary>神珠/宝玉 worker 的 self/hero 选择子 (leaf 里的 DL).</summary>
        private const byte InlaySelectorSelf = 0;
        private const byte InlaySelectorHero = 1;

        /// <summary>
        /// 镶嵌 CM 挂钩. 父级分发命中 4408-4411 时应调用本方法 (见类型注释的集成说明).
        /// </summary>
        private bool TryHandleInlayCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_4408:
                    NativeBeadInlayWorker(InlaySelectorSelf);
                    return true;
                case Grobal2.CM_4409:
                    NativeJadeInlayWorker(InlaySelectorSelf, processMessage);
                    return true;
                case Grobal2.CM_4410:
                    NativeBeadInlayWorker(InlaySelectorHero);
                    return true;
                case Grobal2.CM_4411:
                    NativeJadeInlayWorker(InlaySelectorHero, processMessage);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// worker 的 self/hero 目标解析, 两个 worker 共用 (神珠 0x6F381F、宝玉 0x6F38BE).
        /// 1:1 复刻本人/英雄分派与幽灵门:
        ///   selector==0 -> 本人 (Self)
        ///   selector==1 -> 英雄 [Self+0xBB0]=m_HeroObject, 需 非空 且 !IsDead
        ///                  (sub_772DA8 = 读 [hero+0x74] = m_boDeath) 且 !ghost
        ///                  ([hero+0x73] = m_boGhost); 任一不满足 -> 无目标 (null).
        /// </summary>
        private TBaseObject ResolveInlayTarget(byte selector)
        {
            if (selector == InlaySelectorSelf)
            {
                return this; // 0x6F3852 / 0x6F38F1  mov eax,edi (Self)
            }

            var hero = m_HeroObject; // 0x6F3825 / 0x6F38C4  mov ebx,[edi+0xBB0]
            if (hero == null)
            {
                return null; // 0x6F382D / 0x6F38CC  test ebx,ebx; je -> 无目标
            }
            if (hero.m_boDeath)
            {
                return null; // 0x6F3831 / 0x6F38D0  call sub_772DA8([hero+0x74]); jne -> 无目标
            }
            if (hero.m_boGhost)
            {
                return null; // 0x6F3840 / 0x6F38DF  cmp [hero+0x73],0; jne -> 无目标
            }
            return hero; // 0x6F3846 / 0x6F38E5  mov eax,[edi+0xBB0]
        }

        /// <summary>
        /// 神珠镶嵌 worker 0x6F37EC (CM 4408 selector=0 / CM 4410 selector=1).
        ///
        /// 目标解析按 <see cref="ResolveInlayTarget"/> 1:1 复刻 (本人/英雄/幽灵门). 但无论
        /// 目标如何, 回发的结果码都不可证, 故终态一律 fail-closed:
        ///   * 有效目标 -> 镶嵌链 0x7487A8 经 sub_78BD70->sub_78C5EC 挂载, 写运行期镶嵌
        ///     属性 item+0x100..0x102, 取自运行期属性表 [0x7DCBDC] (stride 0x75, 静态镜像全 0);
        ///   * 无有效目标 (英雄变体, 英雄缺失/死亡/幽灵) -> 回码 -98/-99 由自身门决定:
        ///     m_boDealing([self+0x461]) -> -98, 否则 sub_6C7D88(self); 其 [self+0x711]
        ///     (ConfirmPending) 分支带未建模的计时/发消息副作用, -98 与 -99 无法忠实区分.
        /// 两种终态均不可推导, 依铁律登记后丢弃 (不回发臆造 body).
        /// </summary>
        private void NativeBeadInlayWorker(byte selector)
        {
            int cm = selector == InlaySelectorSelf ? Grobal2.CM_4408 : Grobal2.CM_4410;
            var target = ResolveInlayTarget(selector);
            if (target == null)
            {
                // 英雄缺失: -98/-99 取决于 sub_6C7D88 的未建模副作用分支 -> fail-closed.
                NativeCmTailFailClosed.Drop(cm, m_sCharName);
                return;
            }

            // 有效目标: 0x7487A8 挂载依赖运行期镶嵌属性表 [0x7DCBDC] (静态镜像全 0) -> fail-closed.
            NativeCmTailFailClosed.Drop(cm, m_sCharName);
        }

        /// <summary>
        /// 宝玉合成 worker 0x6F38A8 (CM 4409 selector=0 / CM 4411 selector=1).
        ///
        /// 与神珠 worker 不同, 宝玉 worker 无 -98 自身门 (0x6F38B9 直接置 -99). 可证部分
        /// 回发真实 SM, 仅把不可证的合成终态 fail-closed:
        ///   * 无有效目标 (英雄变体, 英雄缺失/死亡/幽灵) -> ESI 保持 -99, 回发 SM Recog=-99.
        ///   * 有效目标进入链 0x748A18: Param&lt;5 -> -1 (0x748A43); body 长度&lt;0x14 -> -2 (0x748A4C).
        ///   * 参数合法后需从 body 取 5 个神珠、查模板表 [0x7D3F34]、经物品工厂
        ///     [[0x7D5D6C]]->0x74DE54 造宝玉, 均未建模 -> fail-closed.
        /// </summary>
        private void NativeJadeInlayWorker(byte selector, TProcessMessage processMessage)
        {
            int cm = selector == InlaySelectorSelf ? Grobal2.CM_4409 : Grobal2.CM_4411;
            short sm = (short)(selector == InlaySelectorSelf ? Grobal2.SM_4409 : Grobal2.SM_4411);

            var target = ResolveInlayTarget(selector);
            if (target == null)
            {
                // 0x6F38F3 test eax,eax; je 0x6F3908 -> ESI 仍为 -99 (0x6F38B9). 回发即可, 完全可证.
                SendDefMessage(sm, InlayResultNoTarget, 0, 0, 0, string.Empty);
                return;
            }

            // 进入 0x748A18. 以下两道门只看消息参数, 不触及物品模型, 完全可证.
            if (processMessage.nParam2 < JadeMinBeadParam)
            {
                // 0x748A43 cmp edx,5; jl 0x748C36 -> result=-1
                SendDefMessage(sm, JadeResultParamTooSmall, 0, 0, 0, string.Empty);
                return;
            }
            if (processMessage.nBodyLen < JadeMinBodyLen)
            {
                // 0x748A4C cmp [ebp+8],0x14; jl 0x748C2D -> result=-2
                SendDefMessage(sm, JadeResultBodyTooShort, 0, 0, 0, string.Empty);
                return;
            }

            // 参数合法: 合成链需模板表 [0x7D3F34] + 物品工厂 0x74DE54 + 运行期属性, 均未建模 -> fail-closed.
            NativeCmTailFailClosed.Drop(cm, m_sCharName);
        }
    }
}
