using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 战神 "按名/按 id 查询" CM cluster — CM 3179, 3282, 3295, 3306.
    ///
    /// These four leaves were previously fail-closed by the generic cm-2/cm-3
    /// registries (NativeCmQ2FailClosed / NativeCmQ3FailClosed). This partial
    /// consolidates them into one dedicated, byte-level-audited handler so the
    /// query data flow is documented in one place and every gate this port CAN
    /// evaluate is reproduced 1:1. The findings below come from disassembling each
    /// leaf and worker in flat_image.bin (ImageBase 0x400000, capstone 5.0.7); no
    /// terminal action is invented.
    ///
    /// Dispatcher frame (sub_6D7D68, selector root 0x6D805C reading word[record+4]):
    ///   [ebp-4]    = Self          [ebp-0x34] = 12-byte wire record
    ///   [ebp-8]    = body string   ESI/EDI    = body length
    /// record = { int Recog@0, word Ident@4, word Param@6, word Tag@8, word Series@0xA },
    /// surfaced here as nParam1=Recog, nParam2=Param, nParam3=Tag, wParam=Series,
    /// sMsg=body, nBodyLen=body length (SystemModule/Data/TProcessMessage.cs).
    ///
    /// ── Shared finder sub_73CF08(Self=EAX, id=EDX) ──────────────────────────────
    /// All four workers resolve their target through sub_73CF08, which scans the
    /// player's MAIN ITEM TList at [Self+0x508] and returns the item whose
    /// [item+0x18] == id (TList.Count @ +8, TList.Get = sub_424D4C). This finder
    /// AND [Self+0x508] ARE modelled in this port — see NativeItemMerge
    /// (SelfItemListOffset = 0x508), NativeUpdateClothesTransaction and
    /// NativeStrengthenEquipQuest, which all port the same "find item by id in
    /// [player+0x508]" primitive. So the *lookup* is not the blocker; the blocker
    /// is the per-worker terminal state read AFTER the lookup, detailed per ident.
    /// (Established item field map: +0x18 id/Recog, +0x1C StdItem-def ptr,
    ///  +0x26 Dura, +0x28 DuraMax, +0x34 merge-gate word — NativeItemMerge.cs.)
    ///
    /// NOTE on the "按名 (by-name)" label: despite the cluster nickname, none of
    /// these workers calls a by-NAME player finder. They are all by-ID (by-Recog)
    /// item queries via sub_73CF08. UserEngine.GetPlayObject(name) was checked for
    /// reuse and is NOT applicable to any of the four. 3295/3306 merely copy a text
    /// / dword payload out of the body; the object they act on is still by-Recog.
    ///
    /// ── Disposition (§铁律 有据不臆造 / fail-closed) ────────────────────────────
    /// Every evaluable pre-gate is reproduced 1:1 as genuine native silence; the
    /// terminal action of each worker reads state this port does not model, so it
    /// is withheld via the existing per-ident registry rather than emitting invented
    /// bytes or an invented return code. In particular CM 3179's out-of-bounds -1
    /// is NOT synthesised (§铁律 尤其 3179 越界返回 -1 不可臆造): the byte it would
    /// report comes from an item-extension sub-object chain that has no C# model.
    ///
    /// ── HOOKING (integrator; this port never edits Operate()/Q2/Q3 files) ───────
    /// Wire TryHandleNameQueryCm into TPlayObject.Message.cs::Operate ahead of the
    /// quarter dispatchers, so it claims these four idents before the generic
    /// Q2/Q3 fail-closed arms do (3179 belongs to the Q2 quarter → ahead of
    /// TryHandleNativeCmQ2; 3282/3295/3306 belong to the Q3 quarter → ahead of
    /// TryHandleNativeCmQ3). A single insertion before TryHandleNativeCmQ2 (which
    /// itself precedes Q3) satisfies both:
    ///     default:
    ///         if (!TryHandleNativeSocialProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
    ///             &amp;&amp; !TryHandleNameQueryCm(ProcessMsg)   // ← add: 3179 前置于 Q2, 3282/3295/3306 前置于 Q3
    ///             &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
    ///         {
    ///             result = base.Operate(ProcessMsg);
    ///         }
    ///         break;
    /// TryHandleNameQueryCm returns true only for the four idents it owns; when it
    /// is not wired, the legacy Q2/Q3 stubs still fail-close them identically, so
    /// wiring it is a no-regression precedence change.
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>
        /// Dispatch for the 按名/按 id query cluster. Returns true (consumed) only
        /// for CM 3179/3282/3295/3306. Insert ahead of TryHandleNativeCmQ2 — see
        /// the HOOKING note on this partial.
        /// </summary>
        private bool TryHandleNameQueryCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_3179:
                    NameQuery_Cm3179_MerchantItemByteQuery();
                    return true;
                case Grobal2.CM_3282:
                    NameQuery_Cm3282_RosterByIds(processMessage.nBodyLen);
                    return true;
                case Grobal2.CM_3295:
                    NameQuery_Cm3295_MicroWhelk(processMessage);
                    return true;
                case Grobal2.CM_3306:
                    NameQuery_Cm3306_ItemDualValueByRecog(processMessage.nBodyLen);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 3179 — leaf 0x6DA3F3 → worker 0x6E320C(Self=EAX, Recog=record[0]=EDX).
        ///
        /// Data flow (three-part: find → chain → reply):
        ///   find  : item = sub_73CF08(Self, Recog)   → [Self+0x508] item whose +0x18==Recog
        ///   chain : def   = [item+0x1C]              (StdItem-def ptr; 0 → Recog=-1)
        ///           sub   = [def+0x44]               (ext sub-object; 0 → Recog=-1)
        ///           arr   = [sub+0x14]               (Delphi dynamic byte array; Length via 0x406A88)
        ///           idx   = byte[item+0x37]
        ///           byte  = (idx &lt; Length(arr)) ? arr[idx] : -1   (0x6E3253 jge → -1)
        ///   reply : SM 0x6BE (SM_1726) via vmt+0x250, Recog = byte-or-(-1), unconditionally.
        ///
        /// C# mapping: the find leg IS modelled ([Self+0x508] item TList, +0x18 id,
        /// +0x1C StdItem def — NativeItemMerge). The chain leg is NOT: TStdItem
        /// (SystemModule/Packet/TStdItem.cs) is a flat record (Name…Price) with no
        /// +0x44 sub-object and no +0x14 byte array — that array is a native in-memory
        /// StdItem extension with no serialised form and no C# model.
        ///
        /// Fail-closed: the worker ALWAYS answers SM 0x6BE, and the answer's Recog is
        /// either the real ext byte (found + in-bounds) or -1 (not-found / null def /
        /// out-of-bounds). This port can evaluate the not-found -1 leg but not the
        /// found byte, and emitting -1 unconditionally would fabricate the very
        /// out-of-bounds return code §铁律 forbids while breaking the worker's
        /// always-reply invariant — so the packet is dropped rather than answered.
        /// No leaf/worker length gate exists to reproduce (leaf calls the worker
        /// straight; the worker's only branches read the unmodelled chain).
        /// </summary>
        private void NameQuery_Cm3179_MerchantItemByteQuery()
            => NativeCmQ2FailClosed.Q2Drop(Grobal2.CM_3179, m_sCharName);

        /// <summary>
        /// CM 3282 — leaf 0x6DA600 → worker 0x6E64BC(Self=EAX, body=EDX, len=ECX).
        ///
        /// Evaluable gate (reproduced): worker 0x6E64E1 `cmp ecx,0x14 / jl 0x6E661C`
        /// — a body shorter than 0x14 bytes (5×4) tears down and returns with no
        /// send. Reproduced here as genuine silence.
        ///
        /// Data flow for len&gt;=0x14: parse 5 int Recogs from the body and resolve
        /// each via sub_73CF08 (the modelled by-id item finder) into a 5-slot array,
        /// then match that array against the manager list [[0x7D5D6C]]+0x3C0 via
        /// sub_75339C. On a hit it emits SM 0xA (per item, formatted from [obj+0x20]
        /// through sub_784568/sub_768BE0), SM 0xCD3 (Recog=1, count via sub_791F3C)
        /// through vmt+0x254, and SM 9; on a miss it emits SM 0xCD3 (Recog=0) with
        /// word[[[0x7D5D6C]]+0x3C0 +8] through vmt+0x250.
        ///
        /// C# mapping: the 5 by-id resolutions are modelled, but the manager list
        /// [[0x7D5D6C]]+0x3C0, the matcher sub_75339C and the SM 0xA/0xCD3/9 bodies
        /// (built from [obj+0x20], vmt+0x34, sub_791F3C) are not. Both the hit and
        /// the miss reply reflect that unmodelled list, so the terminal action is
        /// withheld.
        /// </summary>
        private void NameQuery_Cm3282_RosterByIds(int nBodyLen)
        {
            if (nBodyLen < 0x14)
            {
                return; // 0x6E64E1 cmp ecx,0x14 / jl 0x6E661C — native silence
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3282, m_sCharName);
        }

        /// <summary>
        /// CM 3295 — the main-bag TMicroWhelk operation restored from leaf
        /// 0x6DAA99 and worker 0x6EB8E4. The shared implementation owns the exact
        /// Dura 1000 boundary, SM 641/202 ordering, SM 106 header, raw GBK body,
        /// mute routing and direct/broadcast NUL counts.
        /// </summary>
        private void NameQuery_Cm3295_MicroWhelk(TProcessMessage processMessage)
            => HandleNativeCm3295(processMessage);

        /// <summary>
        /// CM 3306 — leaf 0x6DAB39 → worker 0x6EFD54(Self=EAX, Recog=record[0]=EDX,
        /// ECX=[body+0], Tag=word[+8], Param=word[+6]).
        ///
        /// Evaluable gate (reproduced): leaf 0x6DAB39 `cmp si,4 / jb 0x6DBC2C` — a
        /// body shorter than 4 bytes is dropped silently. Reproduced as silence.
        ///
        /// Data flow for len&gt;=4: obj = sub_73CF08(Self, Recog) (modelled by-id item
        /// finder), then a chain of guards on the first body dword and globals
        /// ([0x78072C], [0x6AC87C], manager [[0x7D5D6C]] via sub_74DE54, sub_76C9D4,
        /// sub_6C87B4) sets a success flag. On success it reads word[obj+0x12C] /
        /// word[obj+0x130], notifies via vmt+0xD4/0x24C and sub_769258/sub_76920C,
        /// and answers SM 0x275 via vmt+0x250; on failure it answers SM 0x276.
        ///
        /// C# mapping: every guard input (the three globals/managers, the helpers,
        /// word[obj+0x12C]/[0x130], the vmt notify slots) is unmodelled, so the
        /// SM 0x275-vs-0x276 decision cannot be evaluated. Withheld — no invented
        /// return code.
        /// </summary>
        private void NameQuery_Cm3306_ItemDualValueByRecog(int nBodyLen)
        {
            if (nBodyLen < 4)
            {
                return; // 0x6DAB39 cmp si,4 / jb 0x6DBC2C — native silence
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3306, m_sCharName);
        }
    }
}
