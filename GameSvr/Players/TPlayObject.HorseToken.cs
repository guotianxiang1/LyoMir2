using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 坐骑马牌 (TMaPai) — CM 1376 mount-token appearance select.
    ///
    /// Native path (flat_image.bin, ImageBase 0x400000):
    ///   dispatch leaf 0x6DAFF3 reads Recog=dword[msg+0x00] (0x6DAFFD) and
    ///   Param=word[msg+0x06] (0x6DAFF6) and tail-calls worker 0x6F2E44
    ///   (self=eax, edx=Recog, ecx=Param).
    ///
    /// worker sub_6F2E44:
    ///   0x6F2E50  item = sub_73CF08(self, Recog)   ; bag scan obj+0x508 by [item+0x18]
    ///   0x6F2E59  je   0x6F2E9C                     ; item == nil -> SysMsg
    ///   0x6F2E5D  edx = [0x75DC48]                  ; the TMaPai class ref (VMT 0x75DC94)
    ///   0x6F2E63  call 0x404828                     ; Delphi `is` (-> 0x4048C8 InheritsFrom)
    ///   0x6F2E6A  je   0x6F2E9C                     ; not `is TMaPai` -> SysMsg
    ///   0x6F2E70  al = sub_7632E4(item, Param)      ; validate Param against the token kind
    ///   0x6F2E77  je   0x6F2EAF                     ; invalid -> SILENT return
    ///   0x6F2E7D  sub_7632E0(item, Param)           ; byte[item+0x33] = Param
    ///   0x6F2E82  push Param / push 0 / push 0 / push 0
    ///   0x6F2E89  ecx = [item+0x18]                 ; ClientItemID
    ///   0x6F2E8C  dx  = 0x50A
    ///   0x6F2E94  call [self+0x250]                 ; SendDefMessage(0x50A, Recog=[item+0x18],
    ///                                               ;   wParam=Param, Tag=0, Series=0, sMsg="")
    ///   0x6F2E9C  cx = 0x38FF / edx = str@0x6F2EBC ("请放入马牌") / call [self+0xD4]  ; SysMsg
    ///
    /// Data structure (三件套 — offset -> C# mapping, all evidence-backed):
    ///   [item+0x18]  ClientItemID   -> TUserItem.ClientItemID. sub_73CF08 (0x73CF3D
    ///                `mov edx,[eax+0x18]` / 0x73CF40 `cmp edx,key`) matches this,
    ///                NOT MakeIndex (which is item+0x20, LegacyUserItem208Codec:
    ///                record[0]=item+0x20=MakeIndex). The wire id the client echoes
    ///                is EncodeClientItemRecord's `ClientItemID!=0 ? ClientItemID :
    ///                MakeIndex`, so the scan selects the same value. The SM reply
    ///                Recog is [item+0x18] == the matched Recog by construction.
    ///   [item+0x1C]  StdItem template ptr -> M2Share.UserEngine.GetStdItem(wIndex)
    ///                (GoodItem). Used for `is TMaPai` (StdMode==34 via
    ///                NativeItemFactory; TMaPai is childless so name-equality == `is`)
    ///                and for the token kind = word[StdItem+0x4C]. The 00CA std-item
    ///                wire body (== in-memory TStdItem: StdMode@0x14, Shape@0x15)
    ///                decodes 0x4C as IntParam1 (NativeType2StdItemDefinition
    ///                `IntParam1 => ReadInt32(0x4C)`), so kind == (ushort)IntParam1.
    ///   [item+0x33]  mount-appearance value byte (sub_7632E0 write; default 1 from
    ///                TMaPai.Create 0x763200 @0x76322F; summon reader sub_7632C8
    ///                @0x7632CA reads the SAME byte) -> record 0x13 (record base
    ///                item+0x20) == TUserItem.btValue[9]. btValue is what the 208-byte
    ///                codec rebuilds record[0x0A..0x17] from, so btValue[9] is the
    ///                authoritative representation; NativeRecord[0x13] is kept in sync.
    ///
    /// NOTE (cross-subsystem, sibling file left untouched):
    /// TPlayObject.NativeRun3Horse.cs::ResolveNativeMountType reads
    /// NativeRecord[0x33], which is item+0x53 (record base is item+0x20), i.e. a
    /// pre-existing off-by-0x20 read of the mount-type byte. The faithful offset is
    /// record 0x13 (== btValue[9] == item+0x33), verified against both the writer
    /// sub_7632E0 and the summon reader sub_7632C8. This port writes the correct
    /// byte; the integrator may want to correct that reader separately.
    ///
    /// HOOKING (integrator; do NOT edit TPlayObject.NativeCmProtocol_Q2.cs):
    /// TryHandleHorseTokenCm owns CM 1376 and must run BEFORE TryHandleNativeCmQ2 so
    /// it shadows that file's fail-closed CM_1376 stub. In
    /// TPlayObject.Message.cs::Operate default arm, add the marked line:
    ///     default:
    ///         if (!TryHandleInlayCm(ProcessMsg)
    ///             &amp;&amp; !TryHandleQiankunCm(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeSocialProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
    ///             &amp;&amp; !TryHandleHorseTokenCm(ProcessMsg)   // &lt;-- add, BEFORE Q2
    ///             &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
    ///         { result = base.Operate(ProcessMsg); }
    /// TryHandleHorseTokenCm returns true only for CM_1376.
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>item+0x33 == record 0x13 (record base item+0x20) == btValue[9].</summary>
        private const int NativeHorseTokenValueBtValueIndex = 9;
        private const int NativeHorseTokenValueRecordOffset = 0x13;

        /// <summary>
        /// Q2-segment hook for the 坐骑马牌 opcode. Insert ahead of
        /// TryHandleNativeCmQ2 in the Operate default arm (see file header).
        /// </summary>
        private bool TryHandleHorseTokenCm(TProcessMessage processMessage)
        {
            if (processMessage.wIdent != Grobal2.CM_1376)
            {
                return false;
            }

            // Default CM decode maps Recog->nParam1, Param(word[+6])->nParam2
            // (same as CM_USEITEMS / CM_SETFIXEDCOORD).
            ClientHorseTokenSetMount(processMessage.nParam1, processMessage.nParam2);
            return true;
        }

        /// <summary>
        /// Port of worker sub_6F2E44. <paramref name="recog"/> is the wire Recog
        /// (dword[msg+0]); <paramref name="param"/> is the wire Param (word[msg+6]).
        /// </summary>
        private void ClientHorseTokenSetMount(int recog, int param)
        {
            var item = FindHorseTokenBagItem(recog, out var stdItem);

            // 0x6F2E59 (item==nil) and 0x6F2E6A (`is TMaPai` false) both jump to the
            // same SysMsg leg 0x6F2E9C. NativeItemFactory is the C# port of the Delphi
            // class factory; case 34 -> "TMaPai" for every Shape, and TMaPai has no
            // subclass, so name-equality reproduces the native `is` at 0x404828.
            if (item == null || stdItem == null ||
                NativeItemFactory.GetClassName(stdItem) != "TMaPai")
            {
                SendHorseTokenNeedTokenMessage();
                return;
            }

            // 0x763318: kind = word[StdItem+0x4C] = low word of IntParam1.
            var kind = unchecked((ushort)stdItem.IntParam1);

            // 0x7632E4: kind==1 accepts Param==1; kind==2 accepts Param in {1,2};
            // any other kind rejects. Reject is the SILENT leg 0x6F2EAF (no packet).
            if (!IsHorseTokenParamAccepted(kind, param))
            {
                return;
            }

            // 0x7632E0: byte[item+0x33] = Param.
            SetHorseTokenMountValue(item, unchecked((byte)param));

            // 0x6F2E82..0x6F2E94: SendDefMessage(0x50A, Recog=[item+0x18], wParam=Param,
            // Tag=0, Series=0, ""). [item+0x18] == recog by the scan, so echo recog.
            SendDefMessage(Grobal2.SM_HORSETOKEN_SETMOUNT, recog, param, 0, 0,
                string.Empty);
        }

        /// <summary>
        /// Port of the bag scan sub_73CF08: linear walk of the player's own bag TList
        /// (obj+0x508 == m_ItemList), first item whose client-facing id equals
        /// <paramref name="recog"/> wins (0x73CF45 store + break). Equipment and the
        /// hero bag are NOT scanned — native only touches obj+0x508.
        /// </summary>
        private TUserItem FindHorseTokenBagItem(int recog, out GoodItem stdItem)
        {
            stdItem = null;
            if (m_ItemList == null)
            {
                return null;
            }

            for (var i = 0; i < m_ItemList.Count; i++)
            {
                var candidate = m_ItemList[i];
                if (candidate == null) // 0x73CF3B: skip nil slot
                {
                    continue;
                }

                // [item+0x18] == the id the client was told. EncodeClientItemRecord
                // writes ClientItemID when assigned, else MakeIndex; match the same.
                var wireId = candidate.ClientItemID != 0
                    ? candidate.ClientItemID
                    : candidate.MakeIndex;
                if (wireId != recog) // 0x73CF43: jne -> next
                {
                    continue;
                }

                stdItem = M2Share.UserEngine?.GetStdItem(candidate.wIndex);
                return candidate;
            }

            return null;
        }

        /// <summary>
        /// Port of sub_7632E4. Native decrements the kind and branches:
        ///   0x7632F2 `dec ax; je` -> kind==1 leg (Param==1 via `dec/sub si,1;setb`),
        ///   0x7632F7 `dec ax; je` -> kind==2 leg (Param in {1,2} via `sub si,2;setb`),
        ///   else result stays 0 (0x7632FC).
        /// </summary>
        private static bool IsHorseTokenParamAccepted(ushort kind, int param)
        {
            return kind switch
            {
                1 => param == 1,
                2 => param == 1 || param == 2,
                _ => false
            };
        }

        /// <summary>
        /// Port of sub_7632E0 `mov byte [item+0x33], dl`. item+0x33 is record 0x13
        /// (record base item+0x20) == btValue[9]. Write btValue[9] (authoritative for
        /// the 208-byte codec, which rebuilds record[0x0A..0x17] from btValue) and
        /// keep the raw NativeRecord image in sync.
        /// </summary>
        private static void SetHorseTokenMountValue(TUserItem item, byte value)
        {
            if (item.btValue != null &&
                item.btValue.Length > NativeHorseTokenValueBtValueIndex)
            {
                item.btValue[NativeHorseTokenValueBtValueIndex] = value;
            }

            if (item.NativeRecord != null &&
                item.NativeRecord.Length > NativeHorseTokenValueRecordOffset)
            {
                item.NativeRecord[NativeHorseTokenValueRecordOffset] = value;
            }
        }

        /// <summary>
        /// 0x6F2E9C: SysMsg with cx=0x38FF and str@0x6F2EBC ("请放入马牌", GBK
        /// C7EBB7C5C8EBC2EDC5C6) through [self+0xD4]. That wrapper (0x73C8F4) splits the
        /// colour word FColor=0xFF / BColor=0x38, i.e. the raw RM_SYSMESSAGE shape used
        /// by the sibling 0x38FF sites (cf. TPlayObject.NativeFixedCoordStone.cs).
        /// </summary>
        private void SendHorseTokenNeedTokenMessage()
        {
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0, "请放入马牌");
        }
    }
}
