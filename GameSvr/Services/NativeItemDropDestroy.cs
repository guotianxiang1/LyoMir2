using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 战神 item-transfer permission and the auth/gift DESTROY branch shared by the three
    /// drop paths (manual drop <c>sub_73CC98</c>, death bag-drop <c>sub_740078</c>,
    /// death equip-drop <c>sub_73FC70</c>).
    ///
    /// All three run the same two-part ladder before placing anything on the ground:
    /// an authentication + gift test that DESTROYS the item with a chat notice, and the
    /// transfer-permission classifier <c>sub_78389C</c> in mode 5 that skips the item.
    /// C# previously had neither, so an unauthenticated character (or a 赠品 gift item)
    /// scattered its inventory onto the floor where any other character could pick it up
    /// — the laundering route "mule drops / mule dies, alt picks up".
    /// </summary>
    internal static class NativeItemDropDestroy
    {
        // ── The GBK notices, read byte-for-byte out of the image (Delphi long strings,
        //    length dword at VA-4).  Do not retype these: they are the literals the
        //    original sends through sub_768BE0(dx=0x5E).
        //
        //   0x73CE74 len=21 ceb4d1e9d6a42c... = "未验证,物品消失(丢弃)"
        //   0x73CE94 len=19 d4f9c6b72c...     = "赠品,物品消失(丢弃)"
        //   0x7402CC len=21 ceb4d1e9d6a42c... = "未验证,物品消失(死亡)"
        //   0x7402EC len=19 d4f9c6b72c...     = "赠品,物品消失(死亡)"
        //   0x740030 len=20 ceb4d1e9d6a4ce... = "未验证物品消失(死亡)"   (no comma — equip path)
        //   0x740050 len=18 d4f9cbcdceef...   = "赠送物品消失(死亡)"     (赠送, not 赠品)

        /// <summary><c>sub_73CC98</c> @0x73CD8F — <c>mov edx,0x73CE74</c>.</summary>
        internal const string DropUnverifiedNotice = "未验证,物品消失(丢弃)";

        /// <summary><c>sub_73CC98</c> @0x73CDA5 — <c>mov edx,0x73CE94</c>.</summary>
        internal const string DropGiftNotice = "赠品,物品消失(丢弃)";

        /// <summary><c>sub_740078</c> @0x7401BC — <c>mov edx,0x7402CC</c>.</summary>
        internal const string DeathBagUnverifiedNotice = "未验证,物品消失(死亡)";

        /// <summary><c>sub_740078</c> @0x7401D2 — <c>mov edx,0x7402EC</c>.</summary>
        internal const string DeathBagGiftNotice = "赠品,物品消失(死亡)";

        /// <summary><c>sub_73FC70</c> @0x73FE68 — <c>mov edx,0x740030</c>.</summary>
        internal const string DeathEquipUnverifiedNotice = "未验证物品消失(死亡)";

        /// <summary><c>sub_73FC70</c> @0x73FE7E — <c>mov edx,0x740050</c>.</summary>
        internal const string DeathEquipGiftNotice = "赠送物品消失(死亡)";

        /// <summary>
        /// The <c>sub_768BE0</c> message kind used by all three destroy branches
        /// (<c>mov dx,0x5E</c> at 0x73CDDE / 0x740211 / 0x73FEB7).
        /// </summary>
        internal const int DestroyNoticeKind = 0x5E;

        /// <summary>
        /// The authentication order the destroy branch tests:
        /// <c>mov cl,4; mov edx,player; call sub_617A38</c> against the singleton at
        /// <c>[[0x7D6534]]</c> (0x73CD37, 0x740154, 0x73FDC3).  <c>sub_617A38</c> is
        /// <c>cmp byte [mgr+8],0; je -&gt; TRUE</c> (feature off ⇒ everyone passes) else a
        /// 2-round <c>bt dword [player+esi+0x193C],order</c> bit test — exactly the
        /// two-status form <c>CheckNativeAuthentication</c> already models.
        /// </summary>
        internal const int AuthenOrder = 4;

        /// <summary><c>sub_78389C</c> mode 2 used by trade (<c>mov edx,2</c>).</summary>
        internal const int TransferModeTrade = 2;

        /// <summary><c>sub_78389C</c> mode used by all three drop paths (<c>mov edx,5</c>).</summary>
        internal const int TransferModeDrop = 5;

        /// <summary>
        /// 战神 <c>sub_78389C</c> @0x78389C — the item transfer-permission classifier.
        /// Returns 0 when the move is permitted; any non-zero value is the rejecting
        /// rung.  Byte-exact:
        /// <code>
        /// 7838AA  xor esi,esi                                     ; esi = 0
        /// 7838AE  call sub_784710 / test ax,ax / jne 0x7838CC     ; bind word != 0 -> REJECT
        /// 7838B8  test byte [[item+0x1C]+3],8 / jne 0x7838CC      ; Reserved02 &amp; 0x0800 -> REJECT
        /// 7838C3  call sub_784720 / test al,al / je 0x7838D1      ; Reserved02 &amp; 0x4000 -> REJECT
        /// 7838CC  mov esi,1                                       ; any hit -> esi = 1
        /// 7838D1  test esi,esi / jne 0x783979                     ; marked -> 0x783979 `mov eax,esi` = return 1 (REJECT)
        /// 7838D9  cmp edi,5 / ja 0x783979                         ; mode &gt; 5 -> return 0
        /// 7838E2  jmp [edi*4+0x7838E9]                            ; per-mode jumptable
        ///   mode 2 -> 0x783911: reject (esi=3) if [item+0xFC]!=0 OR Reserved02 &amp; 0x0200
        ///   mode 5 -> 0x783940: reject (esi=5) unless [item+0xFC]!=0
        ///                       OR Reserved02 &amp; 0x0200 OR &amp; 0x0400 OR &amp; 0x0080
        /// </code>
        /// Note the polarity: the pre-ladder REJECTS (returns 1) — <c>0x783979</c> is
        /// <c>mov eax,esi</c> and <c>esi</c> is 1 on every pre-ladder hit.  Both callers
        /// treat non-zero as reject: trade escrow <c>0x6C4238 cmp [ebp-0x10],0 / jg</c>,
        /// drop <c>0x73CD63 test eax,eax / jne</c>.  The per-mode rungs are therefore only
        /// reached by an UNSTAMPED item that is not 0x0800 / 0x4000.
        /// </summary>
        internal static int CheckTransferPermission(TUserItem item, GoodItem stdItem,
            int mode)
        {
            if (item == null || stdItem == null) return 0;

            // 0x7838AA `xor esi,esi` then 0x7838AE-0x7838CA: three ways to reach
            // 0x7838CC `mov esi,1`, which 0x7838D3 `jne 0x783979` carries straight to
            // 0x783979 `mov eax,esi` — the pre-ladder REJECTS with 1, it is not a whitelist.
            if (NativeItemAcquisitionStamp.ReadBindWord(item) != 0
                || (stdItem.NativeReserved02 & 0x0800) != 0
                || (stdItem.NativeReserved02 & 0x4000) != 0)
            {
                return 1;
            }

            // 0x7838D9: only modes 0..5 have a jumptable entry.
            if ((uint)mode > 5) return 0;

            // 0x783911 (mode 2): reject when byte[std+3] & 0x02 (no-trade flag).
            // Native bytes: 80 BB FC 00 00 00 00 (cmp byte [ebx+0xFC],0) / 75 09 (jne 0x783923)
            //               8B 43 1C (mov eax,[ebx+0x1C]) / F6 40 03 02 (test byte [eax+3],2)
            //               74 56 (je 0x783979 = allow) / BE 03 00 00 00 (mov esi,3) / EB 4F
            // NativeReserved02 is the ushort at std+2, so byte[std+3] is its high byte:
            // bit 1 of std+3 == 0x0200, the same mask the mode-5 rung below already uses.
            // [item+0xFC] has no C# counterpart yet, so that disjunct is omitted.
            if (mode == TransferModeTrade)
            {
                if ((stdItem.NativeReserved02 & 0x0200) != 0)
                {
                    return 3;
                }
            }

            // 0x783940 (mode 5): reject when the item carries any of the bind / timed
            // classes.  [item+0xFC] (the always-drop class) has no C# counterpart yet, so
            // that disjunct is omitted — recorded, not invented.
            if (mode == TransferModeDrop)
            {
                if ((stdItem.NativeReserved02 & 0x0200) != 0
                    || (stdItem.NativeReserved02 & 0x0400) != 0
                    || (stdItem.NativeReserved02 & 0x0080) != 0)
                {
                    return 5;
                }
            }

            return 0;
        }

        /// <summary>
        /// 战神 <c>item+0xD8</c> — the "gift" (赠品) byte tested by all three destroy
        /// branches (<c>cmp byte [item+0xD8],0; je</c> at 0x73CD44 / 0x740161 / 0x73FDD0).
        /// <c>+0xD8</c> has no field in this rewrite's <c>TUserItem</c>; the gift class is
        /// carried instead by the StdItem descriptor bit this repo already decodes.  This
        /// helper keeps the read in one place so the field can be swapped in later without
        /// touching the three call sites.
        /// </summary>
        internal static bool IsGiftItem(TUserItem item)
        {
            return item != null && item.NativeGiftItem;
        }

        /// <summary>
        /// The shared destroy-branch decision: <c>true</c> when the item must be REMOVED
        /// AND FREED (never placed on the map).  Native shape, identical in all three
        /// paths:
        /// <code>
        /// cmp byte [player+0x178],0 / jne  -> normal drop   ; non-player race skips the gate
        /// call sub_617A38(cl=4) / test al,al / je -> DESTROY ; not authenticated
        /// cmp byte [item+0xD8],0 / je -> normal drop         ; authenticated + not a gift
        ///                                / else -> DESTROY   ; authenticated but a gift
        /// </code>
        /// </summary>
        internal static bool ShouldDestroy(bool isPlayerRace, bool authenticated,
            TUserItem item)
        {
            // 0x73CD23 / 0x740140 / 0x73FDAF: `cmp byte [player+0x178],0; jne` — only the
            // player race (RC_PLAYOBJECT == 0) reaches the auth ladder at all.
            if (!isPlayerRace) return false;
            if (!authenticated) return true;            // 0x73CD42 je -> destroy
            return IsGiftItem(item);                    // 0x73CD4B je -> normal drop
        }

        /// <summary>
        /// The notice text native builds: it appends the unverified line when
        /// <c>sub_617A38</c> failed and the gift line when <c>item+0xD8</c> is set, then
        /// formats it through <c>sub_784568</c> (item name) and sends via
        /// <c>sub_768BE0(dx=0x5E)</c>.  Both conditions can hold at once, and native
        /// simply overwrites the local string, so the gift text wins.
        /// </summary>
        internal static string BuildDestroyNotice(bool authenticated, TUserItem item,
            string unverifiedNotice, string giftNotice)
        {
            var text = authenticated ? null : unverifiedNotice;
            if (IsGiftItem(item)) text = giftNotice;     // native overwrites (0x73CDAA)
            return text;
        }
    }
}
