using SystemModule;
using System.Buffers.Binary;

namespace GameSvr
{
    /// <summary>
    /// 战神 <c>sub_7842F8</c> — the item ACQUISITION STAMPER that runs inside the outer
    /// AddItemToBag wrapper <c>sub_6B7378</c> (TPlayer VMT slot <c>+0x248</c>, 68 native
    /// call sites).  C# previously implemented only the inner plain add
    /// (<c>sub_73D078</c>), so <c>word[item+0x34]</c> was never written and every
    /// downstream gate that reads it saw 0 ("free item") forever.
    ///
    /// Byte-exact body (@0x7842F8, verified by capstone over the reunpacked image):
    /// <code>
    /// 784302  mov ebx,eax                     ; eax = item object
    /// 784300  mov edi,edx                     ; dx  = word[player+0x278]  (= Level, RTTI 'LevelOrder' sibling; see below)
    /// 7842FE  mov esi,ecx                     ; ecx = daysOnline (sub_6D43C4)
    /// 784304  mov eax,[ebx+0x1C]              ; -> TStdItem
    /// 784307  test byte [eax+3],2             ; Reserved02 &amp; 0x0200 = bind-on-acquire class
    /// 78430B  je  0x784326
    /// 78430D  mov al,[ebp+8]                  ; acquisitionReason (stack arg)
    /// 784310  dec eax
    /// 784311  sub al,2 / jb  0x784319         ; reason 1 or 2 -> take branch
    /// 784315  sub al,1 / jne 0x784326         ; reason 4      -> take branch (3 falls through = skip)
    /// 784319  mov edx,esi / sub dx,2          ; edx = daysOnline - 2
    /// 78431F  mov eax,ebx / call sub_784718   ; word[item+0x34] = daysOnline - 2
    /// 784326  mov eax,[ebx+0x1C]
    /// 784329  test byte [eax+3],4             ; Reserved02 &amp; 0x0400 = second bind class
    /// 78432D  je  0x78433E
    /// 78432F  cmp di,0x1E / jae 0x78433E      ; the player word must be &lt; 30
    /// 784335  mov edx,esi / mov eax,ebx
    /// 784339  call sub_784718                 ; word[item+0x34] = daysOnline
    /// 784342  ret 4
    /// </code>
    ///
    /// <c>sub_784718</c> = <c>mov word [eax+0x34],dx</c> (@0x784718, 5 bytes) and
    /// <c>sub_784710</c> = <c>mov ax,word [eax+0x34]</c> (@0x784710) — so
    /// <c>item+0x34</c> is a single 16-bit field.  This repo already models it as
    /// <c>TUserItem.btValue[10..11]</c> (see <c>TPlayObject.PileItems.cs:221</c>,
    /// <c>HeroObject.cs:2325</c>, <c>NativeStallWriteTransaction.cs:77</c>).
    /// </summary>
    internal static class NativeItemAcquisitionStamp
    {
        /// <summary>
        /// <c>sub_7842F8</c> @0x784307 — <c>test byte [[item+0x1C]+3],2</c>.
        /// <c>[std+2]</c>/<c>[std+3]</c> are the low/high bytes of Reserved02
        /// (cross-verified on <c>+0x14</c>/<c>+0x15</c>/<c>+0x1A</c> against
        /// <c>NativeType2StdItemRuntimeAppend</c>), so bit 2 of <c>[std+3]</c> is 0x0200.
        /// </summary>
        internal const ushort BindOnAcquireClass = 0x0200;

        /// <summary><c>sub_7842F8</c> @0x784329 — <c>test byte [[item+0x1C]+3],4</c>.</summary>
        internal const ushort SecondBindClass = 0x0400;

        /// <summary><c>sub_7842F8</c> @0x78432F — <c>cmp di,0x1E; jae</c> (strictly below 30).</summary>
        internal const int SecondBindLevelLimit = 0x1E;

        /// <summary>
        /// <c>sub_6B7378</c> @0x6B73A3 — <c>cmp byte [edi+0x675],3; ja</c>.  RTTI
        /// (<c>staging/ida_strparam_rtti_exact_20260719.txt</c> idx=0014) pins
        /// <c>TPlayer.GMLevel</c> to the direct field <c>Self+0x675</c>, written at
        /// <c>0x6B1E80</c> from <c>sub_65583C</c> (the admin-list name lookup that C#
        /// mirrors as <c>UserEngine.GetHumPermission</c> -&gt; <c>m_btPermission</c>).
        /// GM-acquired items are deliberately NOT stamped.
        /// </summary>
        internal const int MaxStampedGmLevel = 3;

        /// <summary>
        /// The acquisition reasons observed at the native <c>+0x248</c> call sites
        /// (the pushed stack argument).  Only 1, 2 and 4 select the first stamp
        /// branch; 3 is explicitly skipped by the <c>sub al,1; jne</c> ladder.
        /// </summary>
        internal static class Reason
        {
            /// <summary>Native <c>sub_6C4580</c> @0x6C47A1 / @0x6C48C9 — DEAL completion (<c>push 1</c>).</summary>
            internal const byte Deal = 1;

            /// <summary>Native <c>sub_6D3C7C</c> @0x6D3DC2 and <c>sub_6F1358</c> @0x6F13B9 (<c>push 2</c>).</summary>
            internal const byte Reason2 = 2;

            /// <summary>Native <c>sub_6B74D8</c> @0x6B7713 — ground PICKUP (<c>push 4</c>).</summary>
            internal const byte PickUp = 4;

            /// <summary>Every other native site pushes 0 (no stamp branch selected).</summary>
            internal const byte None = 0;
        }

        /// <summary><c>sub_784710</c> @0x784710 — <c>mov ax,word [item+0x34]</c>.</summary>
        internal static ushort ReadBindWord(TUserItem item)
        {
            if (item?.btValue == null || item.btValue.Length < 12) return 0;
            return BinaryPrimitives.ReadUInt16LittleEndian(item.btValue.AsSpan(10, 2));
        }

        /// <summary><c>sub_784718</c> @0x784718 — <c>mov word [item+0x34],dx</c>.</summary>
        internal static void WriteBindWord(TUserItem item, ushort value)
        {
            if (item?.btValue == null || item.btValue.Length < 12) return;
            BinaryPrimitives.WriteUInt16LittleEndian(item.btValue.AsSpan(10, 2), value);
        }

        /// <summary>
        /// <c>sub_7842F8</c> verbatim.  <paramref name="daysOnline"/> is native
        /// <c>ecx</c> (<c>sub_6D43C4</c>), <paramref name="playerLevelWord"/> is native
        /// <c>dx</c> = <c>word[player+0x278]</c>, <paramref name="reason"/> is the
        /// pushed stack argument.  Returns true when the stamp word was written
        /// (used by the audits; native returns void).
        /// </summary>
        internal static bool Apply(TUserItem item, GoodItem stdItem, byte reason,
            int daysOnline, int playerLevelWord)
        {
            if (item == null || stdItem == null) return false;
            var written = false;

            // 0x784307: bind-on-acquire class -> stamp (daysOnline - 2) for reasons {1,2,4}.
            // 0x78430D-0x784317 decodes as: al = reason; al--; al -= 2; if (borrow) take;
            // else al -= 1; if (al != 0) skip.  Borrow after (reason-1)-2 means reason < 3,
            // i.e. reason in {1,2}; the residual equality path is reason == 4.
            if ((stdItem.NativeReserved02 & BindOnAcquireClass) != 0
                && (reason == Reason.Deal || reason == Reason.Reason2
                    || reason == Reason.PickUp))
            {
                // 0x78431B `sub dx,2` is a 16-bit wrap, matching word[item+0x34].
                WriteBindWord(item, (ushort)((daysOnline - 2) & 0xFFFF));
                written = true;
            }

            // 0x784329: second bind class -> stamp daysOnline, but only while the
            // player word (+0x278 = Level) is strictly below 30 (0x78432F cmp di,0x1E; jae).
            if ((stdItem.NativeReserved02 & SecondBindClass) != 0
                && (playerLevelWord & 0xFFFF) < SecondBindLevelLimit)
            {
                WriteBindWord(item, (ushort)(daysOnline & 0xFFFF));
                written = true;
            }

            return written;
        }
    }
}
