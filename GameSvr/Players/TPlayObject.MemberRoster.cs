using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 「成员名册簇」CM 3180 / 3190 / 3191 / 3307 —— cm-3 曾把它们记为社交名册/成员集合并整体
    /// fail-closed。忠实反汇编 (flat_image.bin, ImageBase 0x400000, capstone) 后判定：这 <b>不是</b>
    /// 队伍 / 结拜 / 行会 一类社交成员，而是 <b>「极品装备升级 / 装备回收(荣耀点)」物品子系统</b>。
    /// 这里的「成员」是玩家 <b>背包物品</b>；「名册/成员集合」就是 <c>[self+0x508]</c> 背包物品列表。
    ///
    /// <para><b>成员数据模型（偏移 → 原生含义 → C# 映射）</b>：</para>
    /// <list type="bullet">
    /// <item><c>[self+0x508]</c> = Delphi <c>TList</c>（<c>FCount@+8</c>，取值 <c>sub_424D4C</c>）= 玩家背包
    /// 物品列表。<b>证据</b>：背包 worker 销毁支 <c>0x74019D call sub_424B30</c> 先从 <c>[self+0x508]</c>
    /// 摘除再 <c>0x74021E Free</c>（见 <c>TBaseObject.Base.cs</c> 内注）。→ C# <c>m_ItemList</c>。</item>
    /// <item><c>sub_73CF08(self, id)</c> 不是「按名查对象」（cm-3 台账误标），而是
    /// <c>FindBagItemById</c>：遍历 <c>[self+0x508]</c> 找 <c>[item+0x18]==id</c> 的项。</item>
    /// <item>物品项 <c>[+0x18]</c>=物品唯一 id（对应 <c>TUserItem.MakeIndex</c>，未做逐字节对齐——查项支
    /// 全部 fail-closed，不依赖它）；<c>[+0x1c]</c>=缓存的 <c>StdItem</c> 模板指针，名字在
    /// <c>[+0x1c]+4</c>（<c>sub_784568</c>）→ C# <c>UserEngine.GetStdItem(wIndex).Name</c>。</item>
    /// <item><c>[+0x1c]+0x44</c> = 5×6「升级/材料槽」网格；<c>[+0x1c]+0x2a/0x2c/0x2d/0x2e/0x32</c> =
    /// 升级 word/byte 统计；<c>[+0x20]</c>=值、<c>[+0x37]</c>=计数字节 —— 均为「极品装备升级」自定义
    /// 结构，<b>C# StdItem 未建模</b>。</item>
    /// <item><c>[[0x7D5D6C]]</c> = 全局 StdItem 管理器（g_StdItemList）；<c>[+0x1c]</c>=StdItem TList
    /// （<c>sub_74C248</c> 按序号取）→ C# <c>UserEngine.GetStdItem</c>；<c>[+0x3a8]</c>=按名的「每日
    /// 次数」表，每条 <c>[+0x14]</c>=上限 / <c>[+0x18]</c>=今日已用 / <c>[+0x1c]</c>=日序号
    /// （由 <c>[0x7D6A88]</c> 服务器时间 <c>Trunc</c> 得到）。此每日计数与已建模的
    /// <c>NativeGloryLogManager</c>（<c>GloryLog(costId, costDate, value)</c> 唯一键）同构，但
    /// <b>内存里的每条限额记录（+0x14/+0x18/+0x1c）未建模</b>。</item>
    /// <item><c>[0x7D6A88]</c> = 指向服务器 TDateTime(now) 的指针；<c>sub_40EB9C</c> 是 <c>DecodeTime</c>
    /// （除数 60000/60/1000，出 时/分/秒/毫秒），<b>不是</b> DecodeDate（cm-3 台账误记为「month/day 门」）。
    /// 3191 leaf 的门实为 <c>时==0 且 分&lt;5 → 丢弃</c>（午夜后 5 分钟拒绝窗口）。<c>[0x7D6A88]</c> 语义
    /// 未建模为可解码全局 → 时间门 fail-closed。</item>
    /// <item>奖励货币「荣耀点」本身 <b>已建模</b>（<c>m_CreditCard.GloryPointValue</c> +
    /// <c>NativeGloryLogManager</c> + <c>DecNativeGloryPoint</c>），回收/升级 <b>动作</b>由眼神插件+脚本
    /// （<c>YanshenRecycleDriver</c>/<c>RunQuest.pas AutoRecycle</c>）承担。但这四个 CM 是
    /// <b>查询/握手</b>包，只 <b>读取</b> 上面那些未建模的背包槽表/StdItem 限额记录来拼回包，无法从镜像
    /// 求得回包字节。</item>
    /// </list>
    ///
    /// <para><b>结论（§铁律 fail-closed，绝不臆造）</b>：唯一可从已建模状态求得的回包是 CM 3180 的
    /// 短包腿；其余全部依赖未建模结构 → 丢弃并记账。</para>
    ///
    /// <para><b>挂钩（本代理不改 Message.cs / 他人分部）</b>：把本方法接到
    /// <c>TPlayObject.Message.cs</c> Operate() default 臂里、<b>CM Q3 之前</b>一行即可认领这 4 个 ident：
    /// <code>
    /// if (!TryHandleNativeSocialProtocol(ProcessMsg)
    ///     &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg)
    ///     &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
    ///     &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)
    ///     &amp;&amp; !TryHandleMemberRosterCm(ProcessMsg)   // &lt;-- 插在 Q3 之前
    ///     &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
    /// {
    ///     result = base.Operate(ProcessMsg);
    /// }
    /// </code>
    /// 接入后 <c>NativeCmProtocol_Q3.cs</c> 里 3180/3190/3191/3307 的旧臂即变为不可达（本方法先认领）。
    /// 未接入时本方法为待挂钩代码，行为与 Q3 旧臂一致，故不改变现网。丢弃统一复用
    /// <c>NativeCmQ3FailClosed.Q3Drop</c>（每 ident 单进程节流，登记表已含这 4 个 ident）。
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>
        /// 认领「极品装备升级/回收」查询握手包 CM 3180/3190/3191/3307。命中返回 true。
        /// </summary>
        internal bool TryHandleMemberRosterCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_3180: MemberRosterCm3180(processMessage.nBodyLen); return true;
                case Grobal2.CM_3190: MemberRosterCm3190(); return true;
                case Grobal2.CM_3191: MemberRosterCm3191(); return true;
                case Grobal2.CM_3307: MemberRosterCm3307(); return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 3180, leaf 0x6DA405, worker 0x6E3280(Self, body=[ebp-8], len=ECX)。
        /// body 被当成 ≥6 个 4 字节物品 id 的数组（<c>[body+i*4]</c>）。
        ///
        /// worker 首门在 0x6E32A0 <c>cmp ecx,0x18 / jl 0x6E3428</c>：短包时 EDI 仍为 0x6E329E
        /// <c>xor edi,edi</c> 的 0，落到共享回包 0x6E3428 —— 经 vmt+0x250 发 SM 0x6BF(1727)，
        /// Recog=EDI=0、body 全零。<b>此短包腿不依赖任何未建模结构，如实复现。</b>
        ///
        /// nBodyLen&gt;=0x18 时：<c>FindBagItemById(Self, [body+0])</c>（0x73CF08 扫 <c>[self+0x508]</c>），
        /// 再读 StdItem 的 5×6 升级槽网格 <c>[item.StdItem+0x44]</c> 逐槽匹配，成功则发 SM 0x24 明细 +
        /// SM 0x6BF(Recog∈{-1 未找到, -2 无网格/计数越界, -3 匹配数不符, 1 成功})。升级槽网格 C# 未建模
        /// → fail-closed。
        /// </summary>
        private void MemberRosterCm3180(int nBodyLen)
        {
            // 0x6E32A0 cmp ecx,0x18 / 0x6E32A3 jl 0x6E3428（EDI=0）→ SM 0x6BF Recog=0 全零 body。
            if (nBodyLen < 0x18)
            {
                SendDefMessage(Grobal2.SM_1727, 0, 0, 0, 0, string.Empty);
                return;
            }

            // 查项 + 5×6 升级槽匹配 + SM 0x24/0x6BF 明细：升级槽网格未建模。
            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3180, m_sCharName);
        }

        /// <summary>
        /// CM 3190, leaf 0x6DA5AE, worker 0x6E590C(Self, Recog=[record])。worker 扫背包
        /// <c>[self+0x508]</c> 找 <c>[item+0x18]==Recog</c> 的项（0x6E5930 循环 / 0x424D4C 取值），
        /// 命中则经 StdItem 管理器 <c>[[0x7D5D6C]].0x752A20(item)</c> 做「名字→每日限额」查表并得计数，
        /// 发 SM 0xB40，Recog=该计数（未命中则 Recog=0）。
        ///
        /// 每日限额表（<c>[[0x7D5D6C]]+0x3a8</c> 的 +0x14/+0x18/+0x1c 内存记录）C# 未建模：命中且有限额
        /// 记录时的 Recog 无法求得；一律回 Recog=0 会在「有物品且有限额」时臆造回包 → 整体 fail-closed。
        /// </summary>
        private void MemberRosterCm3190() =>
            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3190, m_sCharName);

        /// <summary>
        /// CM 3191, leaf 0x6DA5C0, worker 0x6E5BA8(Self, Recog=[record])。leaf 先把服务器时间
        /// <c>[[0x7D6A88]]</c> 经 <c>sub_40EB9C = DecodeTime</c> 拆成 时/分/秒/毫秒，门为
        /// 0x6DA5DC <c>cmp word[ebp-0xC](时),0 / jne</c> + <c>cmp word[ebp-0xE](分),5 / jb 0x6DBC2C</c>
        /// —— 即「<b>时==0 且 分&lt;5</b> 丢弃」（午夜后 5 分钟拒绝窗口；<b>非</b> cm-3 台账所记的 month/day 日门）。
        /// 通过后 worker 扫 <c>[self+0x508]</c> 找 <c>[item+0x18]==Recog</c>，摘除该项（0x425020 TList.Delete）、
        /// 走每日限额结算并发 SM 0xB41 + 广播「%s在装备回收商人处回收了%s,获得了%d荣耀点」。
        ///
        /// 时间门全局 <c>[0x7D6A88]</c> 语义、每日限额记录、背包摘除后的回包均未建模 → fail-closed。
        /// </summary>
        private void MemberRosterCm3191() =>
            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3191, m_sCharName);

        /// <summary>
        /// CM 3307, leaf 0x6DABEA, worker 0x6CBD78(Self, Recog=[record])。<b>真 worker 只用 StdItem
        /// 管理器</b>：<c>[[0x7D5D6C]].0x74C2FC(Recog)</c> = 按序号取 StdItem（0x74C248：
        /// <c>[mgr+0x1c]</c> 是 StdItem TList，越界返回 nil），命中则 0x74C338 造一个派生对象、
        /// 经 0x791F3C 算出 body 长（<c>word[obj+0xA]*4 + 0x10</c>）后经 vmt+0x254 发 SM 0xD04，再 0x791F18 释放。
        /// <b>它不触碰 <c>[self+0x508]</c></b> —— cm-3 台账「[+0x508]/[+0x258]/[+0x248]」是把它与邻居
        /// sub_6CBDD4 混淆了（后者才走背包与 vmt+0x258/+0x248 及 [[0x7D5F20]]）。
        ///
        /// SM 0xD04 的 body 来自 StdItem 的「极品装备升级」派生结构（word[+0xA] 计数 + 表体），C# StdItem
        /// 未建模该结构 → fail-closed。
        /// </summary>
        private void MemberRosterCm3307() =>
            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3307, m_sCharName);
    }
}
