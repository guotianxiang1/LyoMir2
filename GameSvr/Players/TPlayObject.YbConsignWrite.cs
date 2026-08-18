using System.Collections.Generic;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    // =====================================================================
    // 元宝寄售 · 写侧 (yb-consignment WRITE) — CM 1350..1364
    //
    // The read side of this subsystem (CM 1252/1253/1256/1257) is modelled in
    // TPlayObject.NativeYbConsignment.cs / NativeYbConsignmentQuery.cs: four list
    // views backed by a local INativeYbConsignmentStore (EmptyStore by default,
    // i.e. no external server). This file is the WRITE side — the 15 client
    // opcodes that mutate the SellItems / ybDealHis tables (post / re-price /
    // reclaim / settle). cm-2 had these fail-closed via NativeCmQ2FailClosed.Q2Drop;
    // this file upgrades them to a faithful port of the native workers.
    //
    // ---------------------------------------------------------------------
    // NATIVE DATA FLOW (disassembled from flat_image.bin, base 0x400000)
    //
    // Dispatch: leaf 0x6DAC8E..0x6DADB5 (arm per ident) -> worker 0x6F09C4..0x6F120C.
    // Every worker (except reclaim 0x6F1028 and 0x6F120C) opens with the same gate:
    //
    //   busy gate sub_6F0A24(self) -> bool "refuse silently", returns TRUE when ANY of:
    //     * byte[self+0x18C8] != 0            ; a prior write still awaits its async ack
    //     * (dword[0x7D7038]+3 & 0x80) == 0   ; the 元宝寄售写 feature switch is OFF
    //     * byte[[self+0x128]+0x82] != 0      ; the current map blocks consignment writes
    //   -> when busy, the worker returns with NO reply at all (native silence).
    //
    // If the gate passes, the worker forwards the request through sub_6D3694:
    //   sub_6D3694(self, dx=reqIdent, ecx=arg, push len, push body, push arg3):
    //     builds a 0x40-byte identity prefix
    //       +0x00 ShortString(10)  = [self+0xAF4]
    //       +0x0B ShortString(20)  = [self+0xB09]
    //       +0x20 ShortString(15)  = [self+0x106]  (char name)
    //       +0x30 ShortString(15)  = [self+0xB33]
    //     then Move()s the caller body after it, and hands (len, buf, arg3) to the
    //     manager singleton [0x7D5D98]/sub_637A00.
    //   sub_637A00 frames the record { u32 0x33AABB77 magic ; u32 ecx ; u32 arg3 ;
    //     u16 reqIdent ; u16 len } + payload into a growable send-queue [self+0x180]
    //     and FLUSHES it to a SOCKET at [[self+0x1C]+0x38] (sub_4C93F8). i.e. the write
    //     is an RPC to an EXTERNAL 元宝寄售/DB server; the SM_xxxx ack (see below) comes
    //     back ASYNCHRONOUSLY through that socket, not from the worker.
    //   sub_637A00 returns 1 (queued) only when the link is up (byte[singleton+0x2C]!=0);
    //   a disconnected/disabled link returns 0.
    //
    //   worker: byte[self+0x18C8] = forward result; if != 0 the request is queued and
    //   the worker returns (ack is async). If == 0 (link down) the worker emits an
    //   IMMEDIATE reply — a fully image-derivable SM_12xx packet (the per-ident tables
    //   below), which is the "server unavailable" answer.
    //
    // ---------------------------------------------------------------------
    // WHAT THIS PORT CAN AND CANNOT DO
    //
    // The external server link (singleton [0x7D5D98], the 0x33AABB77 framing, the
    // async ack receive path) is NOT modelled in this C# port — exactly as the read
    // side ships with an EmptyStore and no socket to any consignment/DB server. So:
    //
    //   * The busy/config gate 0x6F0A24 is reproduced 1:1. Its feature switch
    //     (dword[0x7D7038]+3 & 0x80) has NO hardcoded default-on setter in the image
    //     (the always-on bits set at 0x6FC6E9 are +0/+1, never +3), so it is loaded
    //     from server config and is OFF on a server that does not configure it. This
    //     port has no such config -> the gate is CLOSED -> the whole busy-gated write
    //     family (1350..1358, 1361..1363) is NATIVE-SILENT by default. ("忙门/配置门
    //     若默认关，同样复刻".) NativeYbConsignmentWrite.WriteFeatureEnabled exposes the
    //     switch for tests/future wiring.
    //
    //   * The forward itself is a no-op that returns 0 (link down), because there is
    //     no external server. So IF the feature switch is enabled, each busy-gated
    //     worker takes its image-derivable "server unavailable" reply path. The true
    //     terminal action (enqueue + async success ack) is FAIL-CLOSED: this port
    //     never fabricates a queued state or a success packet.
    //
    //   * reclaim (CM 1359/1360, worker 0x6F1028) and CM 1364 have NO busy gate, so
    //     they are config-INDEPENDENT and always active. Reclaim is fully modelled
    //     here: the two pre-forward hint legs (safe-zone, bag-full) and the SM_1257
    //     reply are all image-derivable and reproduced byte-for-byte. Its forward is
    //     the same unmodelled RPC, so it always takes the "server unavailable"
    //     (SM_1257) path — the faithful behaviour for a port with no consignment link.
    //
    //   * Posting (1352, item template + bind masks), the vendor-slot setter (1351,
    //     street-stall state [self+0x18A0..]/sub_6C7D88/sub_6D64B8) and the two
    //     body-struct writers (1350/1355, whose pre-forward gate reads binary body
    //     fields this port does not surface) keep their leaf + busy gate but FAIL-CLOSE
    //     the body/item-dependent branch rather than invent item/vendor semantics.
    //
    // ---------------------------------------------------------------------
    // HOOKING (integrator — this file does NOT edit the Operate() switch)
    //
    // TryHandleYbConsignWriteCm owns CM 1350..1364 and must run BEFORE
    // TryHandleNativeCmQ2 (which still carries cm-2's Q2Drop arms for these idents).
    // In TPlayObject.Message.cs::Operate default: add the first line here —
    //     if (!TryHandleNativeSocialProtocol(ProcessMsg)
    //         && !TryHandleNativeCmTailProtocol(ProcessMsg)
    //         && !TryHandleNativeCmQ1(ProcessMsg)
    //         && !TryHandleYbConsignWriteCm(ProcessMsg)   // <-- add, BEFORE Q2
    //         && !TryHandleNativeCmQ2(ProcessMsg)
    //         && !TryHandleNativeCmQ3(ProcessMsg))
    //     { result = base.Operate(ProcessMsg); }
    // Once wired, the 1350..1364 arms inside TryHandleNativeCmQ2 are superseded
    // (unreachable, harmless). TryHandleYbConsignWriteCm returns true only for the
    // 15 idents it owns.
    //
    // TProcessMessage field binding (wire TDefaultMessage): nParam1=Recog[rec+0],
    // nParam2=Param[rec+6], nParam3=Tag[rec+8], wParam=Series[rec+0xA], nBodyLen=body.
    // =====================================================================
    public partial class TPlayObject
    {
        /// <summary>
        /// 0x006F1BE8 prologue `mov byte [eax+0x18C8],0` — external batch-cancel ack
        /// clears the write-pending flag before query/debug logging.
        /// </summary>
        internal void ClearNativeYbConsignWritePending() =>
            m_btYbConsignWritePending = 0;

        /// <summary>
        /// [self+0x18C8]. Set to sub_6D3694's return in every busy-gated worker:
        /// non-zero once a write has been queued to the (external) manager and is
        /// awaiting its async ack. This port never queues, so it stays 0 — kept so the
        /// busy gate keeps its exact native shape.
        /// </summary>
        private byte m_btYbConsignWritePending;

        /// <summary>
        /// sub_6F0A24 — the busy/config/map gate shared by workers 1350..1358, 1361..1363.
        /// Returns true = "refuse this write silently". CLOSED by default because the
        /// feature switch (dword[0x7D7038]+3 &amp; 0x80) is off on an unconfigured server
        /// and this port carries no such config. See file header.
        /// </summary>
        private bool YbConsignWriteGateClosed()
        {
            // 0x6F0A24: cmp byte[self+0x18C8],0 / jne busy
            if (m_btYbConsignWritePending != 0)
            {
                return true;
            }

            // 0x6F0A2D: mov edx,[0x7D7038] / test byte[edx+3],0x80 / je busy
            if (!NativeYbConsignmentWrite.WriteFeatureEnabled)
            {
                return true;
            }

            // 0x6F0A39: mov eax,[self+0x128] / cmp byte[eax+0x82],0 / jne busy.
            // The per-map block flag is not part of the C# Envirnoment model; the
            // feature switch above already closes the gate, so this stays permissive
            // (0 = allowed) and is documented rather than invented.
            return false;
        }

        /// <summary>
        /// Owns CM 1350..1364 (元宝寄售 write family). Returns true for exactly those
        /// idents. Must be wired BEFORE TryHandleNativeCmQ2 (see file header).
        /// </summary>
        private bool TryHandleYbConsignWriteCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_1350: // leaf 0x6DAC8E -> worker 0x6F09C4
                    ClientYbConsignWrite1350(processMessage.nBodyLen);
                    return true;
                case Grobal2.CM_1351: // leaf 0x6DACA7 -> worker 0x6F0A98
                    ClientYbConsignWrite1351();
                    return true;
                case Grobal2.CM_1352: // leaf 0x6DACD0 -> worker 0x6F0B84
                    ClientYbConsignWrite1352();
                    return true;
                case Grobal2.CM_1353: // leaf 0x6DACE4 -> worker 0x6F0E0C
                    ClientYbConsignWriteRecogGated(processMessage.nParam1,
                        0x139, Grobal2.SM_1261, -1);
                    return true;
                case Grobal2.CM_1354: // leaf 0x6DACF6 -> worker 0x6F0E64
                    ClientYbConsignWriteRecogGated(processMessage.nParam1,
                        0x13A, Grobal2.SM_1255, -1);
                    return true;
                case Grobal2.CM_1355: // leaf 0x6DAD08 -> worker 0x6F0EBC
                    ClientYbConsignWrite1355(processMessage.nBodyLen);
                    return true;
                case Grobal2.CM_1356: // leaf 0x6DAD21 -> worker 0x6F0F28
                    ClientYbConsignWriteRecogGated(processMessage.nParam1,
                        0x13C, Grobal2.SM_1254, -1);
                    return true;
                case Grobal2.CM_1357: // leaf 0x6DAD33 -> worker 0x6F0F80
                    ClientYbConsignWriteRecogGated(processMessage.nParam1,
                        0x13D, Grobal2.SM_1262, -1);
                    return true;
                case Grobal2.CM_1358: // leaf 0x6DAD45 -> worker 0x6F0FD8
                    ClientYbConsignWriteUngated(0x13E, Grobal2.SM_1256,
                        processMessage.nParam1, 0);
                    return true;
                case Grobal2.CM_1359: // leaf 0x6DAD57 (cl=1) -> worker 0x6F1028
                    ClientYbConsignReclaim(processMessage.nParam1, cl: true);
                    return true;
                case Grobal2.CM_1360: // leaf 0x6DAD6B (cl=0) -> worker 0x6F1028
                    ClientYbConsignReclaim(processMessage.nParam1, cl: false);
                    return true;
                case Grobal2.CM_1361: // leaf 0x6DAD7F -> worker 0x6F110C
                    ClientYbConsignWriteRecogGated(processMessage.nParam1,
                        0x141, Grobal2.SM_1259, -1);
                    return true;
                case Grobal2.CM_1362: // leaf 0x6DAD91 -> worker 0x6F1164
                    ClientYbConsignWriteRecogGated(processMessage.nParam1,
                        0x142, Grobal2.SM_1260, -1);
                    return true;
                case Grobal2.CM_1363: // leaf 0x6DADA3 -> worker 0x6F11BC
                    ClientYbConsignWriteUngated(0x143, Grobal2.SM_1263,
                        processMessage.nParam1, 5);
                    return true;
                case Grobal2.CM_1364: // leaf 0x6DADB5 -> worker 0x6F120C
                    ClientYbConsignWrite1364(processMessage.nParam1,
                        processMessage.nParam2, processMessage.nParam3);
                    return true;
                default:
                    return false;
            }
        }

        // -----------------------------------------------------------------
        // Tier B — busy-gated writes whose entire ladder is image-derivable:
        //   gate -> [Recog gate] -> forward(no-op, link down) -> immediate SM ack.
        // Silent by default (gate closed). The ack fires only if the feature switch
        // is enabled, and is the exact "server unavailable" packet from the image.
        // -----------------------------------------------------------------

        /// <summary>
        /// Workers 0x6F0E0C/0x6F0E64/0x6F0F28/0x6F0F80/0x6F110C/0x6F1164 — identical shape:
        /// busy gate; `test esi,esi / jle` on Recog; forward reqIdent; on link-down emit
        /// SM ackIdent with a fixed Recog (-1). All six are byte-identical bar the codes.
        /// </summary>
        private void ClientYbConsignWriteRecogGated(int nRecog, int reqIdent,
            int smAckIdent, int ackRecog)
        {
            if (YbConsignWriteGateClosed())
            {
                return; // 0x6F0A24 busy/config gate -> native silence
            }
            if (nRecog <= 0)
            {
                return; // test esi,esi / jle -> native silence
            }

            // sub_6D3694 -> singleton [0x7D5D98]/sub_637A00: no external link in this
            // port, so the forward is not queued (returns 0). byte[self+0x18C8] = 0.
            m_btYbConsignWritePending = NativeYbConsignmentWrite.ForwardWrite(this, reqIdent);
            if (m_btYbConsignWritePending != 0)
            {
                return; // queued -> ack is async
            }

            // link down -> the worker's immediate reply (fully image-derivable).
            SendDefMessage((short)smAckIdent, ackRecog, 0, 0, 0, "");
        }

        /// <summary>
        /// Workers 0x6F0FD8 (1358) / 0x6F11BC (1363) — busy gate, NO Recog gate, forward,
        /// then on link-down emit SM ackIdent carrying (Recog=echoed value, Param=fixed).
        /// 1358: Recog=arg, Param=0. 1363: Recog=arg, Param=5.
        /// </summary>
        private void ClientYbConsignWriteUngated(int reqIdent,
            int smAckIdent, int ackRecog, int ackParam)
        {
            if (YbConsignWriteGateClosed())
            {
                return; // 0x6F0A24
            }

            m_btYbConsignWritePending = NativeYbConsignmentWrite.ForwardWrite(this, reqIdent);
            if (m_btYbConsignWritePending != 0)
            {
                return; // queued -> ack is async
            }

            SendDefMessage((short)smAckIdent, ackRecog, ackParam, 0, 0, "");
        }

        // -----------------------------------------------------------------
        // Tier A — config-INDEPENDENT (no busy gate): reclaim + 1364.
        // -----------------------------------------------------------------

        /// <summary>
        /// CM 1359 (cl=1) / CM 1360 (cl=0) — worker 0x6F1028, 元宝寄售取回. NO busy gate,
        /// so this is active regardless of the write feature switch. Two image-derivable
        /// hint legs then the SM_1257 reply:
        ///   0x6F103D !sub_76858C(self)      -> SysMsg 0x38FF "在非安全区不能领回"
        ///   0x6F1048 sub_7441D8(self) &lt;= 0 -> SysMsg 0xFFDB "您的包裹位不足"
        ///                                        (sub_7441D8 = 0x30 - [[self+0x508]+8])
        ///   0x6F10B1 forward result == 0     -> SM_1257 (0x4E9), Recog=arg, empty body
        /// The forward (req 0x13F for cl=1 / 0x140 for cl=0) targets the unmodelled
        /// manager singleton, so it never queues (result 0) and every path reaches the
        /// SM_1257 reply — the faithful "consignment link unavailable" answer, which
        /// does NOT fabricate a completed reclaim (native sends SM_1257 on exactly the
        /// not-completed paths).
        /// </summary>
        private void ClientYbConsignReclaim(int nRecog, bool cl)
        {
            var queued = false; // 0x6F1039 xor ebx,ebx

            // 0x6F103D call sub_76858C(self) — boSAFE OR polygon OR start-point abs<=12.
            var inSafeZone = InNativeSafeZone12();
            if (!inSafeZone)
            {
                // 0x6F109E: mov cx,0x38FF / mov edx,gbk / call [vmt+0xD4]
                SysMsgByWord(0x38FF, "在非安全区不能领回");
            }
            else
            {
                // 0x6F1048: sub_7441D8 = 0x30 - [[self+0x508]+8] (free bag slots)
                var freeSlots = 0x30 - (m_ItemList?.Count ?? 0);
                if (freeSlots <= 0)
                {
                    // 0x6F1089: mov cx,0xFFDB / mov edx,gbk / call [vmt+0xD4]
                    SysMsgByWord(0xFFDB, "您的包裹位不足");
                }
                else
                {
                    // safe + space: forward the reclaim to the unmodelled manager
                    // singleton [0x7D5D98]/sub_637A00 (req 0x13F cl=1 / 0x140 cl=0).
                    // No external link -> not queued (returns 0).
                    queued = NativeYbConsignmentWrite.ForwardReclaim(this, nRecog, cl);
                }
            }

            // 0x6F10B1: test bl,bl / jne (skip the reply only when the forward queued).
            if (queued)
            {
                return; // queued -> ack is async
            }

            // 0x6F10BD..0x6F10C8: SendDefMessage(0x4E9, Recog, 0,0,0, "").
            SendDefMessage((short)Grobal2.SM_1257, nRecog, 0, 0, 0, "");
        }

        /// <summary>
        /// CM 1364 — worker 0x6F120C. NO busy gate. Gate: Param(nParam2) &gt;= 5 AND
        /// Tag(nParam3) &gt; 0x1E, else native silence. On pass it forwards
        /// MakeLong(Param, Tag) with Recog (req 0x146) to the unmodelled manager and
        /// returns with NO reply (fire-and-forget, no ack ident). With no external link
        /// the forward is a no-op, so the observable result is silence either way.
        /// </summary>
        private void ClientYbConsignWrite1364(int nRecog, int nParam, int nTag)
        {
            // 0x6F121D cmp si,5 / jb ; 0x6F1223 cmp di,0x1E / jbe
            if (nParam < 5 || nTag <= 0x1E)
            {
                return; // native silence
            }

            // 0x6F1229 MakeLong(Param, Tag) -> forward req 0x146 (Recog in ecx). No ack.
            NativeYbConsignmentWrite.ForwardWrite1364(this, nRecog,
                HUtil32.MakeLong(nParam, nTag));
        }

        // -----------------------------------------------------------------
        // Tier C — busy gate reproduced; body/item/vendor-dependent branch fail-closed.
        // Silent by default (gate closed). The withheld branch is recorded once per
        // ident only if the feature switch is enabled and the gate opens.
        // -----------------------------------------------------------------

        /// <summary>
        /// CM 1350 — leaf 0x6DAC8E (BodyLen &lt; 0x20 -&gt; silent), worker 0x6F09C4.
        /// Busy gate, then a pre-forward gate that reads BINARY body fields
        /// (dword[body+0x18] &gt; 0 AND byte[body+0x1C] in {1,2,3}) before forwarding a
        /// 0x20-byte record (req 0x136) and, on link-down, replying SM_1250 (0x4E2)
        /// Recog=-1. This port does not surface the raw request body, so the body gate
        /// and its forward are fail-closed rather than guessed. The leaf + busy gate are
        /// reproduced faithfully (both already yield silence by default).
        /// </summary>
        private void ClientYbConsignWrite1350(int nBodyLen)
        {
            if (nBodyLen < 0x20)
            {
                return; // 0x6DAC91 jb -> native silence
            }
            if (YbConsignWriteGateClosed())
            {
                return; // 0x6F0A24 -> native silence
            }

            // feature enabled but the binary body gate (dword[body+0x18]/byte[body+0x1C])
            // and the 0x20-byte forward (req 0x136 / ack SM_1250) are not derivable
            // without the raw body -> withhold, do not fabricate.
            NativeYbConsignmentWrite.RecordWithheld(Grobal2.CM_1350, m_sCharName,
                "req 0x136/ack SM_1250(0x4E2); 请求体二进制门 [body+0x18]/[body+0x1C] 未建模");
        }

        /// <summary>
        /// CM 1351 — leaf 0x6DACA7, worker 0x6F0A98, 摆摊/寄售格位设置. Busy gate, then a
        /// street-stall placement path (sub_6C7D88 cooldown, [self+0x18A0]/[+0x18A4]
        /// slot bounds vs 0x1F4, sub_6D64B8 setter) OR a forward (req 0x137) whose
        /// link-down reply is SM_1251 (0x4E3) Recog, Param=status(4/5). The stall state
        /// is not modelled -> fail-closed after the busy gate.
        /// </summary>
        private void ClientYbConsignWrite1351()
        {
            if (YbConsignWriteGateClosed())
            {
                return; // 0x6F0A24 -> native silence
            }

            NativeYbConsignmentWrite.RecordWithheld(Grobal2.CM_1351, m_sCharName,
                "req 0x137/ack SM_1251(0x4E3); 摆摊格 [self+0x18A0]/sub_6C7D88/sub_6D64B8 未建模");
        }

        /// <summary>
        /// CM 1352 — leaf 0x6DACD0, worker 0x6F0B84, 元宝寄售上架. Busy gate, then bag-item
        /// lookup (sub_73CF08 by Recog) + template/bind validation (StdItem [0x7D5D6C]
        /// via sub_7559D0, sub_404828 against [0x75D1CC], byte[item+0x33], sub_78389C
        /// bind check + config [0x7D7038]+2 &amp; 2 / [self+0x18D0]/[+0x18D2]) that assemble
        /// a 0x10A-byte record and forward it (req 0x138). Link-down reply SM_1252 (0x4E4)
        /// Recog, Param=status(1/2/3/5). The item/template/bind chain is not modelled
        /// here -> fail-closed after the busy gate.
        /// </summary>
        private void ClientYbConsignWrite1352()
        {
            if (YbConsignWriteGateClosed())
            {
                return; // 0x6F0A24 -> native silence
            }

            NativeYbConsignmentWrite.RecordWithheld(Grobal2.CM_1352, m_sCharName,
                "req 0x138/ack SM_1252(0x4E4); 上架物品模板/绑定链 (StdItem [0x7D5D6C]/sub_78389C) 未建模");
        }

        /// <summary>
        /// CM 1355 — leaf 0x6DAD08 (BodyLen &lt; 0x0C -&gt; silent), worker 0x6F0EBC.
        /// Busy gate, then a pre-forward gate on BINARY body fields (dword[body+4] &gt; 0
        /// AND dword[body+0] &gt; 0) before forwarding a 0x0C-byte record (req 0x13B). Its
        /// link-down reply SM_1253 (0x4E5) even ECHOES the body: Recog=-2,
        /// Tag=HiWord(dword[body+0]), Series=LoWord(dword[body+0]). The raw body is not
        /// surfaced here -> body gate + echo forward fail-closed; leaf + busy gate kept.
        /// </summary>
        private void ClientYbConsignWrite1355(int nBodyLen)
        {
            if (nBodyLen < 0x0C)
            {
                return; // 0x6DAD0B jb -> native silence
            }
            if (YbConsignWriteGateClosed())
            {
                return; // 0x6F0A24 -> native silence
            }

            NativeYbConsignmentWrite.RecordWithheld(Grobal2.CM_1355, m_sCharName,
                "req 0x13B/ack SM_1253(0x4E5); 请求体二进制门 [body+0]/[body+4] 与回包体回显未建模");
        }

        /// <summary>
        /// sub_6D3694 -&gt; singleton [0x7D5D98]/sub_637A00 SysMsg sibling: the reclaim
        /// worker's hint legs call [vmt+0xD4](self, edx=gbk, cx=wColorWord). The word
        /// splits into (fg=LoByte, bg=HiByte); the live send is the same one YbDbClient
        /// uses — SendMsg(this, RM_SYSMESSAGE, 0, fg, bg, 0, text).
        /// </summary>
        private void SysMsgByWord(int wColorWord, string text)
        {
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                wColorWord & 0xFF, (wColorWord >> 8) & 0xFF, 0, text);
        }
    }

    /// <summary>
    /// The 元宝寄售 write-side manager surface that this port does not model: the config
    /// feature switch (dword[0x7D7038]+3 &amp; 0x80) and the forward to the external
    /// manager singleton [0x7D5D98]/sub_637A00. Kept in this file (no separate service
    /// file) so the write subsystem is self-contained. See TPlayObject.YbConsignWrite
    /// file header for the full data flow.
    /// </summary>
    internal static class NativeYbConsignmentWrite
    {
        /// <summary>
        /// dword[0x7D7038]+3 &amp; 0x80 — the write-side feature switch tested by the busy
        /// gate 0x6F0A24. Loaded from server config in native (no default-on setter in
        /// the image); OFF on an unconfigured server. This port ships no such config, so
        /// it defaults false and the busy-gated write family is native-silent. Exposed
        /// for tests / future config wiring.
        /// </summary>
        internal static bool WriteFeatureEnabled;

        private static readonly HashSet<int> s_reported = new HashSet<int>();
        private static readonly object s_gate = new object();

        /// <summary>
        /// sub_6D3694 forward for the busy-gated workers. In native this frames the
        /// request behind a player-identity prefix and pushes it to the external
        /// consignment/DB server through singleton sub_637A00, returning 1 when queued.
        /// This port has no such link, so the request is never queued: returns 0
        /// (link down), which drives every busy-gated worker onto its image-derivable
        /// "server unavailable" reply. Never fabricates a queued state.
        /// </summary>
        internal static byte ForwardWrite(TPlayObject self, int reqIdent) => 0;

        /// <summary>1364's fire-and-forget forward (req 0x146). Same unmodelled link -&gt; no-op.</summary>
        internal static void ForwardWrite1364(TPlayObject self, int nRecog, int payload)
        {
            // no external consignment link -> nothing is sent; native has no ack here.
        }

        /// <summary>Reclaim forward (req 0x13F cl=1 / 0x140 cl=0). No external link -&gt; not queued.</summary>
        internal static bool ForwardReclaim(TPlayObject self, int nRecog, bool cl) => false;

        /// <summary>
        /// External batch-cancel ack (native 0x006F1EB8). Invoked when the consignment
        /// manager returns per-order results for a seller batch cancel.
        /// </summary>
        internal static void HandleBatchCancelCallback(TPlayObject player,
            int callbackKind, int orderId, int errorCode, int batchCount,
            string detail = null)
        {
            NativeYbConsignmentBatchCancel.HandleCallback(player, callbackKind,
                orderId, errorCode, batchCount, detail);
        }

        /// <summary>
        /// Record — once per ident per process — that a body/item/vendor-dependent write
        /// branch was withheld (fail-closed) because the external manager and the
        /// request-body semantics are not modelled. Only reachable when the feature
        /// switch has opened the busy gate; silent by default.
        /// </summary>
        internal static void RecordWithheld(int cmIdent, string charName, string detail)
        {
            lock (s_gate)
            {
                if (!s_reported.Add(cmIdent))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[元宝寄售·写] CM {cmIdent} 转发搁置(外部寄售管理器 [0x7D5D98]/0x637A00 未建模); " +
                $"角色={(string.IsNullOrEmpty(charName) ? "<unknown>" : charName)}; {detail}");
        }
    }
}
