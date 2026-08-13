using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 消息板 / 自身对象回显子系统 (CM 2815 + CM 1280)。
    ///
    /// 这两个 ident 属于战神客户端消息缺失集第 2 片 (missing[25:50]，见
    /// docs/cm_q2_missing_impl_20260813.md)，此前由 cm-2 在
    /// TPlayObject.NativeCmProtocol_Q2.cs 里整体 fail-closed 丢弃。本分部文件把它们
    /// 升级为忠实实现：逐字节反汇编 worker/leaf/单例后，把【能从镜像求值的门】1:1
    /// 复刻成真正的原生静默，只在【原生真的会回包但 body 依赖未建模状态】的那一点
    /// 才 fail-closed（丢弃并按 ident 限流记一次），绝不把臆造字节送上线。
    ///
    /// 分发链 (dispatcher sub_6D7D68, 选择子树根 0x6D805C, 共享退出标签 0x6DBC2C)：
    ///   CM 2815  leaf 0x6D9B52 (`movzx ecx,si`=BodyLen) → worker 0x6D4E4C
    ///   CM 1280  leaf 0x6DA8F3 → 身份分类器 0x6E9208（+ path2 解析器 0x76C9D4）
    /// dispatcher 帧：[ebp-0x34]=wire record，[msg+0]=Recog→nParam1，
    /// [msg+6]=X、[msg+8]=Y，body→sMsg，(total-0xC)→nBodyLen（见 Grobal2 注释 2008-2010）。
    ///
    /// 挂钩（集成者：不要从本文件改 Operate()/TryHandleNativeCmQ2 的 switch）：
    /// 2815 与 1280 都在 Q2 缺失集内，cm-2 的 TryHandleNativeCmQ2 仍保留它们的
    /// fail-closed 腿。请把本文件的 TryHandleMessageBoardCm 挂在 TryHandleNativeCmQ2
    /// 【之前】，让忠实实现抢先接管这两个 ident，例如在 Operate 的 default: 顶部——
    ///     default:
    ///         if (TryHandleMessageBoardCm(ProcessMsg)) break;   // ← 插在 Q2 之前
    ///         if (TryHandleNativeCmQ2(ProcessMsg)) break;
    ///         if (!TryHandleNativeSocialProtocol(ProcessMsg))
    ///             result = base.Operate(ProcessMsg);
    ///         break;
    /// TryHandleMessageBoardCm 只对 2815/1280 返回 true（与原生一致：命中真实 leaf
    /// 即被消费、不落到 base.Operate；门未过的静默分支也返回 true）。
    /// </summary>
    public partial class TPlayObject
    {
        // === MessageBoard subsystem ===
        // 按 ident 限流的 fail-closed 记录（每进程每 ident 一次）。语义同
        // NativeCmQ2FailClosed，但反映升级后的结论：门已 1:1 复刻并通过，只有 body
        // 依赖未建模状态（消息板单例 / [obj+0x554] 字段块）时才丢弃，不臆造字节。
        private static readonly HashSet<int> s_messageBoardFailClosedReported = new HashSet<int>();
        private static readonly object s_messageBoardFailClosedGate = new object();

        private void MessageBoardFailClosed(int ident, string subsystem, uint handlerVa,
            uint calleeVa, string blocker)
        {
            lock (s_messageBoardFailClosedGate)
            {
                if (!s_messageBoardFailClosedReported.Add(ident))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[CM未移植] CM {ident} ({subsystem}) 门已过但 body 依赖未建模状态, 已丢弃; " +
                $"handler=0x{handlerVa:X6} callee=0x{calleeVa:X6}; " +
                $"角色={(string.IsNullOrEmpty(m_sCharName) ? "<unknown>" : m_sCharName)}; " +
                $"缺口={blocker}");
        }

        /// <summary>
        /// 消息板 / 自身回显 CM 分发。只拥有 CM 2815 与 CM 1280，其余返回 false。
        /// 集成者须把它挂在 TryHandleNativeCmQ2 之前（见文件头 HOOKING）。
        /// </summary>
        private bool TryHandleMessageBoardCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_2815:
                    ClientMessageBoardRelay_2815(processMessage.nBodyLen);
                    return true;
                case Grobal2.CM_1280:
                    ClientSelfEcho_1280(processMessage.nParam1);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 2815 消息板/relay，leaf 0x6D9B52 → worker 0x6D4E4C。
        ///
        /// worker 入口门 (0x6D4E6B `dec ecx / sub ecx,0x40 / jae 0x6D4EDD`)：无符号，
        /// 当 (BodyLen-1) &gt;= 0x40 时跳到退出 ⇒ 仅 BodyLen ∈ [1,0x40] 才继续
        /// （BodyLen=0 经 dec 回绕成 0xFFFFFFFF 亦被跳过；ecx 来自 leaf `movzx ecx,si`
        /// 的 BodyLen 低字）。门外 = 原生静默。
        ///
        /// 门内 worker 恒定执行（与单例是否接受该条无关）：
        ///   1. 取 word[self+0x9E4]/[self+0x9E6]（坐标）、串 [self+0xB33]/[self+0xB09]
        ///      (0x405774 ShortString→AnsiString) 与 body 串；
        ///   2. 0x6D4EBE `call [[0x7D60FC]].0x6A4144`（= 消息板 AddMessage）：内部再校验
        ///      body 长度==0x24、坐标&gt;0、列表 [singleton+0x14] 存在，把 7 元记录
        ///      {[self+0xB09],[self+0xB09],1,4,[self+0xB33],x,y,body} 追加进
        ///      [singleton+0x10]，并回填 out 串 [ebp-8]；
        ///   3. 0x6D4ED7 `call [vmt+0x250]`（SendDefMessage）无条件发 SM 0xAFF
        ///      (=SM_2815，all-0 frame，sMsg=该 out 串)。
        /// out 串来自单例状态，单例 (0x6A4144) 与 [self+0x9E4/0x9E6/0xB09/0xB33] 均
        /// 未建模 ⇒ SM_2815 body 无法逐字节求值。门内必回包，故此处 fail-closed。
        /// </summary>
        private void ClientMessageBoardRelay_2815(int nBodyLen)
        {
            // 0x6D4E6B dec ecx / sub ecx,0x40 / jae 0x6D4EDD — BodyLen∉[1,0x40] → 原生静默
            if ((uint)(nBodyLen - 1) >= 0x40)
            {
                return;
            }

            // 门内：原生无条件经消息板单例发 SM_2815 (0xAFF)。out 串 body 依赖未建模的
            // 单例 [0x7D60FC](0x6A4144) 与玩家坐标/串字段 → fail-closed（不臆造 body）。
            MessageBoardFailClosed(Grobal2.CM_2815, "消息板/relay", 0x006D9B52, 0x006D4E4C,
                "单例 [0x7D60FC](0x6A4144=AddMessage) 与玩家字段 [self+0x9E4]/[+0x9E6]/[+0xB09]/[+0xB33] 未建模; SM_2815(0xAFF) body=单例回填 out 串, 无法求值");
        }

        /// <summary>
        /// CM 1280 自身对象回显，leaf 0x6DA8F3 → 身份分类器 0x6E9208。
        ///
        /// leaf：edx=[wire+0]=Recog（战神里=客户端回传的服务端对象指针），eax=self。
        /// 分类器 0x6E9208：`cmp edx,eax`→1(=self)；`cmp edx,[self+0xBB0]`→2(=hero)；
        /// 否则 0。返回≠0（self 或 hero）时 (0x6DA904..)：0x6DA90F `add eax,0x554` /
        ///   push 0x1C / ecx=Recog / dx=0xCDB / 0x6DA925 `call [vmt+0x254]`(SendSocket)
        ///   发 SM 0xCDB (=SM_3291)，Buf=[命中对象+0x554]，Len=0x1C，Recog=Recog，
        ///   Param/Tag/Series=0。
        /// 返回 0 时 (je 0x6DA930，path2)：按坐标 word[wire+6]/[wire+8] 在地图格里用
        ///   0x76C9D4 找 id==Recog 的可见对象（格节点 [+4]==Recog 且 byte[Recog+0x73]==0），
        ///   过 byte[obj+0x178]∈{0,0x36} 与 vmt[0x27C]() 后同样发 SM 0xCDB，Buf=[obj+0x554]。
        ///
        /// 指针身份门在 C# 用 ObjectManager.Get(Recog) 复刻（Recog=对象 ObjectId）：
        ///   Get(Recog)==null → 分类器=0 且 path2 无格节点 [+4]==Recog → 原生两路皆静默
        ///                      → 返回（stale/garbage Recog，不记录）；
        ///   Get(Recog)==this → 分类器=1（self 回显）；
        ///   Get(Recog)==hero → 分类器=2（hero 回显）；
        ///   其它非空对象     → path2（近身对象回显）。
        /// 三种命中路径的 body 都是同一块 [对象+0x554] 0x1C 字节字段块（28B，未建模）
        /// ⇒ SM_3291(0xCDB) body 无法求值。命中处 fail-closed（不臆造 body）。
        /// </summary>
        private void ClientSelfEcho_1280(int nRecog)
        {
            var target = M2Share.ObjectManager?.Get(nRecog);

            // 0x6E9208 分类器=0 且 path2 (0x76C9D4) 无匹配格节点 → 原生静默
            if (target == null)
            {
                return;
            }

            if (ReferenceEquals(target, this))
            {
                // 0x6E9208 → 1（Recog==self）：SM 0xCDB body=[self+0x554] 0x1C，未建模
                MessageBoardFailClosed(Grobal2.CM_1280, "自身对象回显(self)", 0x006DA8F3, 0x006E9208,
                    "身份门命中 self; SM_3291(0xCDB)@0x6DA925 body=[self+0x554] 0x1C(28B) 字段块未建模, 无法求值");
                return;
            }

            if (ReferenceEquals(target, m_HeroObject))
            {
                // 0x6E9208 → 2（Recog==[self+0xBB0]=hero）：SM 0xCDB body=[hero+0x554]
                MessageBoardFailClosed(Grobal2.CM_1280, "自身对象回显(hero)", 0x006DA8F3, 0x006E9208,
                    "身份门命中 hero; SM_3291(0xCDB)@0x6DA925 body=[hero+0x554] 0x1C(28B) 字段块未建模, 无法求值");
                return;
            }

            // else：path2 (0x6DA930) 近身对象回显；body 同为 [obj+0x554] 未建模。
            // path2 额外门（地图格坐标 / byte[obj+0x178] / vmt[0x27C]）依赖未建模布局，
            // 无法逐门求值；存在可解析对象即可能回包 → fail-closed。
            MessageBoardFailClosed(Grobal2.CM_1280, "自身对象回显(path2近身)", 0x006DA930, 0x0076C9D4,
                "path2 经 0x76C9D4 近身对象回显; SM_3291(0xCDB)@0x6DA99A body=[obj+0x554] 0x1C(28B) 未建模 + 坐标/类型/vmt[0x27C] 门未建模");
        }
    }
}
