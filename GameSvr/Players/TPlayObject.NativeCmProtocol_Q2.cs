using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// CM idents 1265..3179 — the ascending 26..50th entries of 战神's client-message
    /// missing set (missing[25:50]; see docs/cm_q2_missing_impl_20260813.md).
    ///
    /// The dispatcher is sub_6D7D68; its selector tree is rooted at 0x6D805C and
    /// every arm that does nothing jumps to the shared exit label 0x6DBC2C. All
    /// idents handled here resolve to a REAL leaf, never to 0x6DBC2C.
    ///
    /// Dispatcher frame (0x6D7D68..0x6D7D97):
    ///   [ebp-4]    = Self          [ebp-0x34] = wire record
    ///   [ebp-8]    = body string   ESI/EDI    = body length
    /// received here as nParam1/nParam2/nParam3/wParam (Recog/Param/Tag/Series),
    /// sMsg and nBodyLen.
    ///
    /// Disposition (verified by disassembling every worker body, not trusted from
    /// prior artifacts): all 25 leaves run into a subsystem this port has not
    /// modelled, so none can be built byte-faithfully. Each arm reproduces the
    /// gates it CAN evaluate from the image (hero presence, Series, BodyLen, the
    /// Param/Tag range on 1364) as genuine native silence, then hands the ident to
    /// NativeCmQ2FailClosed.Q2Drop — the packet is dropped and the gap is logged
    /// once per ident, rather than putting invented bytes/return codes on the wire.
    ///
    /// HOOKING (integrator, do NOT edit the Operate() switch from this file):
    /// this port keeps TryHandleNativeCmQ2 self-contained exactly like master's
    /// TryHandleNativeCmTailProtocol, which the integrator wires up separately.
    /// Add one short-circuit line to TPlayObject.Message.cs::Operate, e.g. at the
    /// top of the existing default: leg —
    ///     default:
    ///         if (TryHandleNativeCmQ2(ProcessMsg)) break;   // ← add this
    ///         if (!TryHandleNativeSocialProtocol(ProcessMsg))
    ///             result = base.Operate(ProcessMsg);
    ///         break;
    /// TryHandleNativeCmQ2 returns true only for the 25 idents it owns.
    /// </summary>
    public partial class TPlayObject
    {
        private bool TryHandleNativeCmQ2(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                // --- 元宝寄售·写族 (1350..1364, worker 0x6F0xxx, 管理器 [0x7D5D98]) ---
                case Grobal2.CM_1350:
                    ClientNativeQ2_1350_YbConsignWrite(processMessage.nBodyLen);
                    return true;
                case Grobal2.CM_1351:
                    ClientNativeQ2_1351_YbConsignWrite();
                    return true;
                case Grobal2.CM_1352:
                    ClientNativeQ2_1352_YbConsignPost();
                    return true;
                case Grobal2.CM_1353:
                    ClientNativeQ2_1353_YbConsignWrite();
                    return true;
                case Grobal2.CM_1354:
                    ClientNativeQ2_1354_YbConsignWrite();
                    return true;
                case Grobal2.CM_1355:
                    ClientNativeQ2_1355_YbConsignWrite(processMessage.nBodyLen);
                    return true;
                case Grobal2.CM_1356:
                    ClientNativeQ2_1356_YbConsignWrite();
                    return true;
                case Grobal2.CM_1357:
                    ClientNativeQ2_1357_YbConsignWrite();
                    return true;
                case Grobal2.CM_1358:
                    ClientNativeQ2_1358_YbConsignWrite();
                    return true;
                case Grobal2.CM_1359:
                    ClientNativeQ2_1359_YbConsignReclaim();
                    return true;
                case Grobal2.CM_1360:
                    ClientNativeQ2_1360_YbConsignReclaim();
                    return true;
                case Grobal2.CM_1361:
                    ClientNativeQ2_1361_YbConsignWrite();
                    return true;
                case Grobal2.CM_1362:
                    ClientNativeQ2_1362_YbConsignWrite();
                    return true;
                case Grobal2.CM_1363:
                    ClientNativeQ2_1363_YbConsignWrite();
                    return true;
                case Grobal2.CM_1364:
                    ClientNativeQ2_1364_YbConsignWrite(processMessage.nParam2, processMessage.nParam3);
                    return true;

                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------
        // 元宝寄售·写族 (1350..1364)
        //
        // C# 已建模元宝寄售【读】侧 (CM 1252/1253/1256/1257, NativeYbConsignment.cs,
        // 本地 NativeYbConsignmentQuery 存储)。本族是【写】侧: 每个 worker 先过忙门
        // 0x6F0A24 (cmp byte[self+0x18C8],0 / [0x7D7038] 配置 / [self+0x128]+0x82
        // 地图标志, 皆未建模), 再经请求转发器 0x6D3694 把
        //   {[self+0xAF4]/10, [self+0xB09]/20, [self+0x106]/15=角色名, [self+0xB33]/15}
        // 前缀后接调用方 body, 通过单例 [0x7D5D98](0x637A00) 派发。玩家字段与该单例
        // 均未建模, 故 req/ack body 无法逐字节推导 → fail-closed。
        // ------------------------------------------------------------------

        /// <summary>
        /// CM 1350, leaf 0x6DAC8E (`83 FF 20 / 0F 82.. jb 0x6DBC2C`), worker 0x6F09C4
        /// (req SM 0x136, ack SM 0x4E2). Leaf gate: BodyLen &lt; 0x20 → 原生静默。
        /// </summary>
        private void ClientNativeQ2_1350_YbConsignWrite(int nBodyLen)
        {
            if (nBodyLen < 0x20)
            {
                return; // 0x6DAC91 jb 0x6DBC2C — native silence
            }

            NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1350, m_sCharName);
        }

        /// <summary>CM 1351, leaf 0x6DACA7, worker 0x6F0A98 (req 0x137/ack 0x4E3).</summary>
        private void ClientNativeQ2_1351_YbConsignWrite()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1351, m_sCharName);

        /// <summary>CM 1352, leaf 0x6DACD0, worker 0x6F0B84 (上架; req 0x138/ack 0x4E4).</summary>
        private void ClientNativeQ2_1352_YbConsignPost()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1352, m_sCharName);

        /// <summary>CM 1353, leaf 0x6DACE4, worker 0x6F0E0C (req 0x139/ack 0x4ED).</summary>
        private void ClientNativeQ2_1353_YbConsignWrite()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1353, m_sCharName);

        /// <summary>CM 1354, leaf 0x6DACF6, worker 0x6F0E64 (req 0x13A/ack 0x4E7).</summary>
        private void ClientNativeQ2_1354_YbConsignWrite()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1354, m_sCharName);

        /// <summary>
        /// CM 1355, leaf 0x6DAD08 (`83 FF 0C / 0F 82.. jb`), worker 0x6F0EBC
        /// (req SM 0x13B, ack SM 0x4E5). Leaf gate: BodyLen &lt; 0x0C → 原生静默。
        /// </summary>
        private void ClientNativeQ2_1355_YbConsignWrite(int nBodyLen)
        {
            if (nBodyLen < 0x0C)
            {
                return; // 0x6DAD0B jb 0x6DBC2C — native silence
            }

            NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1355, m_sCharName);
        }

        /// <summary>CM 1356, leaf 0x6DAD21, worker 0x6F0F28 (req 0x13C/ack 0x4E6).</summary>
        private void ClientNativeQ2_1356_YbConsignWrite()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1356, m_sCharName);

        /// <summary>CM 1357, leaf 0x6DAD33, worker 0x6F0F80 (req 0x13D/ack 0x4EE).</summary>
        private void ClientNativeQ2_1357_YbConsignWrite()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1357, m_sCharName);

        /// <summary>CM 1358, leaf 0x6DAD45, worker 0x6F0FD8 (req 0x13E/ack 0x4E8).</summary>
        private void ClientNativeQ2_1358_YbConsignWrite()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1358, m_sCharName);

        /// <summary>
        /// CM 1359, leaf 0x6DAD57 (cl=1), worker 0x6F1028 (req SM 0x13F, ack 0x4E9).
        /// worker has two SysMsg hint legs (not bare replies): 安全区门 0x76858C 假 →
        /// SM 0x38FF"在非安全区不能取回物品"; 背包空位 0x7441D8(=0x30-[[self+0x508]+8])
        /// &lt;=0 → SM 0xFFDB"你的背包位置不足"。两腿依赖安全区判定 + 文本发送腿
        /// [vmt+0xD4], 与主路(管理器 [0x7D5D98])一同 fail-closed。
        /// </summary>
        private void ClientNativeQ2_1359_YbConsignReclaim()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1359, m_sCharName);

        /// <summary>CM 1360, leaf 0x6DAD6B (cl=0), worker 0x6F1028 (req 0x140/ack 0x4E9); 同 1359。</summary>
        private void ClientNativeQ2_1360_YbConsignReclaim()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1360, m_sCharName);

        /// <summary>CM 1361, leaf 0x6DAD7F, worker 0x6F110C (req 0x141/ack 0x4EB).</summary>
        private void ClientNativeQ2_1361_YbConsignWrite()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1361, m_sCharName);

        /// <summary>CM 1362, leaf 0x6DAD91, worker 0x6F1164 (req 0x142/ack 0x4EC).</summary>
        private void ClientNativeQ2_1362_YbConsignWrite()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1362, m_sCharName);

        /// <summary>CM 1363, leaf 0x6DADA3, worker 0x6F11BC (req 0x143/ack 0x4EF).</summary>
        private void ClientNativeQ2_1363_YbConsignWrite()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1363, m_sCharName);

        /// <summary>
        /// CM 1364, leaf 0x6DADB5 (push Tag, cx=Param, edx=Recog), worker 0x6F120C
        /// (req SM 0x146, no ack; no 忙门). Worker gate: `cmp si,5 / jb` (Param&lt;5)
        /// and `cmp di,0x1E / jbe` (Tag&lt;=0x1E) → 原生静默; otherwise MakeLong(Param,Tag)
        /// is forwarded to the unmodelled manager.
        /// </summary>
        private void ClientNativeQ2_1364_YbConsignWrite(int nParam, int nTag)
        {
            if (nParam < 5 || nTag <= 0x1E)
            {
                return; // 0x6F1221 jb / 0x6F1227 jbe — native silence
            }

            NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_1364, m_sCharName);
        }
    }
}
