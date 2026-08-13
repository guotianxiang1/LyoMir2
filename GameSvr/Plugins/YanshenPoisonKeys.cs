using System;
using SystemModule;
using GameSvr;

namespace GameSvr.Plugins
{
    /// <summary>
    /// P1-A3/A4/A5 —— 眼神「带毒五键 / 野蛮麻痹 / 噬魂沼泽绿毒」的<b>配置门控插桩点</b>。
    ///
    /// <para>
    /// 本文件<b>不改变任何运行时行为</b>：它只把 <see cref="YanshenApi"/> 里已经声明、
    /// 但战斗代码从不消费的六个开关（武器绿毒 / 物功带毒 / 法师群毒 / 雷电带毒 /
    /// 半月带毒 / 噬魂沼泽绿毒修复）收敛成一组可被主代理直接调用的谓词，并逐条钉死
    /// 每个键的原版挂载点、真实语义与 C# 端已有承载点。主代理在其拥有的热点文件里把
    /// 这些谓词 <c>&amp;&amp;</c> 进已有的施加条件即可完成接线（见每个方法上的「插桩点」注释）。
    /// </para>
    ///
    /// <para><b>底本核验结论（flat_image.bin base 0x400000，capstone 5.0.7 逐条反汇编）：</b></para>
    /// <list type="bullet">
    /// <item>「带毒」的真实载体是<b>自定义 state 26（0x1A）</b>，经每个对象 VMT+0xC8
    /// （TCreature 槽 = <c>sub_76B3C8</c>：免疫判 → <c>imul ecx,时长,1000</c> →
    /// <c>call [VMT+0x1EC]</c> AddTimedState）施加；概率函数 <c>sub_772598</c> =
    /// <c>Random(有效档) &gt;= 阈值</c>，命中在返回 0 时（即 <c>Random&lt;1</c>）。
    /// 这两者与 C# 的 <c>TryApplyNativeState26*</c> / <c>GetNativeState26ContestRange</c>
    /// 逐字节吻合。</item>
    /// <item>审计设想的「<c>[+0x18CC]&amp;2/&amp;4</c> + <c>Random(1)/Random(5)</c> +
    /// <c>call 0x766060</c> + <c>ident 0x283C</c>」绿/红毒机制，在本转储中
    /// <b>位移 0x18CC 命中 0 次、ident 0x283C 命中 0 次</b>（`cc180000`/`3c280000`
    /// 全库零命中）。结合审计 C1（255 个函数被 Themida 逐函数虚拟化、落 0x10400000
    /// 全零区）与 C6（明确把「带毒 0x766060 的 ident 0x283C 出网形态」列为未解），
    /// 该机制在本底本<b>无字节佐证 → fail-closed，不得凭空实现</b>。</item>
    /// </list>
    /// </summary>
    internal static class YanshenPoisonKeys
    {
        // ── 原版挂载点（逐条反汇编钉死；供主代理定位热点） ─────────────────────────
        /// <summary>直接载体 sub_76E268（武器/半月共用）：绿支 0x76E2BC（flag [atk+0x1B4]，
        /// contest 基数 5）、红支 0x76E2FD（flag [atk+0x1B5]，基数 15）。0x76E2FB
        /// <c>jmp 0x76E33C</c> 使绿命中后跳过红 —— <b>互斥一套毒源</b>。</summary>
        internal const uint SiteDirectCarrier76E268 = 0x76E268;
        internal const uint SiteWeaponGreen76E2BC = 0x76E2BC;
        internal const uint SiteWeaponRed76E2FD = 0x76E2FD;

        /// <summary>物理近战后 sub_76A3xx：绿支 0x76A3D0（flag [atk+0x1B4]，
        /// <c>Random([tgt+0x26C]+5)==0</c>，时长 5）、红支 0x76A400（flag [atk+0x1B5]，
        /// <c>Random([tgt+0x26C]+15)==0</c>，<b>时长 3</b>）。0x76A3FE <c>jmp 0x76A42E</c>
        /// 绿命中跳过红 —— 互斥。已由 <c>TryApplyNativeState26AfterPhysicalDamage</c>
        /// 逐字节承载，接线于 <c>TBaseObject.Attack.cs</c>（StruckDamage 之后）。</summary>
        internal const uint SitePhysicalGreen76A3D0 = 0x76A3D0;
        internal const uint SitePhysicalRed76A400 = 0x76A400;

        /// <summary>区域载体 sub_76E0B4（法师群体）：绿支 0x76E1A9（flag [atk+0x1B6]，
        /// contest 基数 7）、红支 0x76E1F0（flag [atk+0x1B7]，基数 21）。承载于
        /// <c>ApplyNativeAreaMagicEffect</c> → <c>TryApplyNativeState26Single(7,21)</c>。</summary>
        internal const uint SiteMageArea76E1A9 = 0x76E1A9;

        /// <summary>半月宿主 sub_771E9C，其逐目标 <c>call sub_76E268</c> 在 0x772105，
        /// 前一条 0x7720F9 <c>push 0</c> = <b>arg0=0</b>，故本底本半月<b>不</b>触发 state 26。</summary>
        internal const uint SiteHalfMoonCall76E268 = 0x7720FB;

        /// <summary>雷电宿主 sub_76EA3C：伤害走 0x76EB18 <c>call sub_76FE44</c>（category 2
        /// line，line 载体不施 state 26）；0x76EB1D 处的 <c>call [VMT+0x3C]</c> 是
        /// <b>练技能</b>（<c>ecx=Random(3)+1</c>，非毒）。本底本雷电<b>不</b>触发 state 26。</summary>
        internal const uint SiteLightningTrain76EB1D = 0x76EB1D;

        /// <summary>噬魂沼泽 = 英雄技能 sub_691BF0（派发器 sub_694020 读 [self+0x68C]=英雄、
        /// 按 [hero+0x72] 选 4 技能之一）。逐格 0x691E34 <c>call sub_76FE44</c>
        /// （QueueNativeMagicEffect，<b>arg0=1</b>）→ 走已端口的 state-26 管线。审计所称
        /// 「0x691E2E 无条件 call 0x766060」实为 <c>call sub_76FE44</c>（0x766060 = SendDelayMsg
        /// 内核，本函数并不直接调用它）。</summary>
        internal const uint SiteSwampQueue76FE44 = 0x691E2E;

        // ── 服务器级开关求值（Enabled 只用 _pluginManager，属服务器全局；插件未运行=关） ──
        private static bool ServerToggle(Func<YanshenApi, bool> pick)
        {
            var pm = M2Share.PluginManager;
            if (pm == null)
                return false;
            return pick(new YanshenApi(null, null, pm));
        }

        // ── A3 带毒五键：配置门控谓词（与装备毒标志 m_boNativeState26* 正交） ──────────

        /// <summary>武器绿毒（skills.weaponGreenPoison）。
        /// <para><b>插桩点</b>：<c>TBaseObject.NativeState26Effects.cs</c> 的
        /// <c>TryApplyNativeState26ByContest</c> / <c>TryApplyNativeState26AfterPhysicalDamage</c>
        /// 的<b>强支（strong / DirectStrong = flag [atk+0x1B4]）</b>条件，改为
        /// <c>m_boNativeState26DirectStrong &amp;&amp; YanshenPoisonKeys.WeaponGreenPoisonEnabled(this)</c>。
        /// 强支已 <c>return</c>，天然跳过弱支 → 与「物功带毒」<b>互斥一套毒源</b>不变。</para></summary>
        internal static bool WeaponGreenPoisonEnabled(TBaseObject caster)
            => ServerToggle(a => a.IsWeaponGreenPoison());

        /// <summary>物功带毒（skills.physicalPoison）。与「武器绿毒」<b>同址</b>
        /// （0x76E2BC / 0x76A3D0），门控<b>弱支（weak / DirectWeak = flag [atk+0x1B5]）</b>。
        /// <para><b>插桩点</b>：同上，弱支条件改为
        /// <c>m_boNativeState26DirectWeak &amp;&amp; YanshenPoisonKeys.PhysicalPoisonEnabled(this)</c>。
        /// 切勿新起第二个施毒调用——原版是强支 <c>jmp</c> 跳过弱支的<b>单一互斥毒源</b>。</para></summary>
        internal static bool PhysicalPoisonEnabled(TBaseObject caster)
            => ServerToggle(a => a.IsPhysicalPoison());

        /// <summary>法师群毒（skills.mageGroupPoison）。门控区域载体 0x76E1A9 的 state-26。
        /// <para><b>插桩点</b>：<c>ApplyNativeAreaMagicEffect</c>（及 Single）里
        /// <c>TryApplyNativeState26Single(target)</c> 前置
        /// <c>if (YanshenPoisonKeys.MageGroupPoisonEnabled(this))</c>。</para></summary>
        internal static bool MageGroupPoisonEnabled(TBaseObject caster)
            => ServerToggle(a => a.IsMageGroupPoison());

        /// <summary>半月带毒（skills.halfMoonPoison）。<b>fail-closed</b>：本底本半月
        /// （0x7720FB，arg0=0）不施 state 26，「带毒」补丁在 Themida 虚拟化区，无字节佐证。
        /// 谓词保留供主代理在拿到带 VM 段的转储、确证补丁把 arg0 置 1 后再接线。</summary>
        internal static bool HalfMoonPoisonEnabled(TBaseObject caster)
            => ServerToggle(a => a.IsHalfMoonPoison());

        /// <summary>雷电带毒（skills.lightningPoison）。<b>fail-closed</b>：0x76EB1D 是练技能、
        /// 雷电本体为 line（不施 state 26）；「带毒」补丁在 Themida 虚拟化区，无字节佐证。</summary>
        internal static bool LightningPoisonEnabled(TBaseObject caster)
            => ServerToggle(a => a.IsLightningPoison());

        // ── A5 噬魂沼泽绿毒修复 ─────────────────────────────────────────────────────

        /// <summary>噬魂沼泽绿毒修复（skills.zhaoZeFix，<see cref="YanshenApi.IsZhaoZeFix"/>）。
        /// <para>噬魂沼泽是英雄技能 sub_691BF0，逐格 <c>call sub_76FE44</c>
        /// （QueueNativeMagicEffect，arg0=1）复用已端口的 state-26 / RM_NATIVE_MAGIC_EFFECT
        /// 管线。「修复」开关本身语义（native 补丁点）在 Themida 虚拟化区，出网 ident 0x283C
        /// 形态审计 C6 列为未解 → <b>fail-closed</b>；谓词保留供主代理在英雄技能派发器
        /// （sub_694020 / [hero+0x72]）接线时门控。</para></summary>
        internal static bool SwampGreenPoisonFixEnabled(TBaseObject caster)
            => ServerToggle(a => a.IsZhaoZeFix());

        // ── A4 野蛮麻痹 ─────────────────────────────────────────────────────────────
        // 审计所称「0x6BC9E2 call [VMT+0xC8] edx=0x1A ecx=3」经复核为
        // 「call [VMT+0x3C]=练技能，ecx=Random(3)+1」（冲撞 sub_73F200 成功后练级），
        // 与麻痹无关。野蛮麻痹在眼神层已由脚本 API 实现并受「野蛮麻痹」开关门控：
        //   YanshenApi.PushEnemy / PullEnemy2（击退/拉回）、RootTarget（STATE_LOCKRUN 定身）。
        // 冲撞 sub_73F200 本体做击退（VMT+0xA4 = CharPushed），已由 CharPushed 承载。
        // 故 A4 无新增 native 施毒/定身缺口 —— fail-closed 于审计的 vmt+0xC8/ecx=3 设想。
    }
}
