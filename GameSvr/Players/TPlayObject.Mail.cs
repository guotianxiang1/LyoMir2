using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private long _nativeMailRecipientId;

        private bool TryGetNativeMailRecipientId(out long recipientId)
        {
            recipientId = _nativeMailRecipientId;
            if (recipientId != 0) return true;
            if (!NativeMailStore.TryResolveRecipientId(
                    m_sCharName, out recipientId, out _))
            {
                recipientId = 0;
                return false;
            }
            _nativeMailRecipientId = recipientId;
            return true;
        }

        private static List<NativeMailCacheEntry> DecodeNativeMailEntries(
            IEnumerable<NativeMailRecord> records)
        {
            var entries = new List<NativeMailCacheEntry>();
            foreach (var record in records ?? Enumerable.Empty<NativeMailRecord>())
            {
                var attachments = new List<TUserItem>(record.RawAttachments.Count);
                foreach (var rawAttachment in record.RawAttachments)
                {
                    if (NativeMailAttachmentCodec.TryDecode(
                            rawAttachment, out var attachment, out _))
                        attachments.Add(attachment);
                }
                record.RawAttachments.Clear();
                entries.Add(new NativeMailCacheEntry(record, attachments));
            }
            return entries;
        }

        private void ClientFetchNativeMailList(int tag)
        {
            if (!TryEnsureNativeMailCategory(tag, out var records)
                || !TryBuildNativeMailListBody(tag, records, out var body, out var count))
            {
                SendDefMessage(Grobal2.SM_FETCH_MAIL_LIST, -1, 0, tag, 0, string.Empty);
                if (tag == 1) TriggerNativeMailQuest();
                return;
            }

            var header = Grobal2.MakeDefaultMsg(
                Grobal2.SM_FETCH_MAIL_LIST, 1, count, tag, 0);
            SendSocket(header, body);
            if (tag == 1) TriggerNativeMailQuest();
        }

        private void ClientFetchNativeMailInfo(int mailId, int tag)
        {
            if (!TryGetNativeMailRecipientId(out var recipientId)
                || !NativeMailCacheService.TryFind(recipientId, tag, mailId, out var entry))
            {
                SendDefMessage(Grobal2.SM_FETCH_MAIL_INFO, -1, 0, 0, 0, string.Empty);
                return;
            }

            MarkNativeMailRead(recipientId, entry.Record);
            var body = BuildNativeMailInfoBody(entry);
            var header = Grobal2.MakeDefaultMsg(
                Grobal2.SM_FETCH_MAIL_INFO, 1, 0, 0, 0);
            SendSocket(header, body);
        }

        private void ClientFetchNativeMailAttachments(int mailId, int tag)
        {
            if (!InSafeZone())
            {
                SendDefMessage(Grobal2.SM_FETCH_ATTACH, -5, 0, 0, 0, string.Empty);
                return;
            }

            if (!TryGetNativeMailRecipientId(out var recipientId)
                || !NativeMailCacheService.TryFind(recipientId, tag, mailId, out var entry))
                return;

            var result = FetchNativeMailAttachments(entry);
            if (result != 0)
                SendDefMessage(Grobal2.SM_FETCH_ATTACH, result, 0, 0, 0, string.Empty);
        }

        private void ClientFetchNativeMailAttachmentsOffline(int mailId)
        {
            var result = 0;
            if (TryGetNativeMailRecipientId(out var recipientId)
                && NativeMailCacheService.TryFind(
                    recipientId, 5, mailId, out var entry))
                result = FetchNativeMailAttachments(entry);

            if (result != 0)
                SendDefMessage(
                    Grobal2.SM_FETCH_ATTACH_OFFTM, result, 0, 0, 0, string.Empty);
        }

        private void ClientClearAllNativeMail(int tag)
        {
            if (!TryGetNativeMailRecipientId(out var recipientId)
                || !NativeMailCacheService.ContainsMailbox(recipientId))
                return;

            var result = -1;
            if (NativeMailCacheService.TryGetCachedCategory(
                    recipientId, tag, out var entries))
            {
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    var record = entries[i].Record;
                    if (record.MailStatus != 2 || record.AttachStatus is not 2 and not 3)
                        continue;
                    NativeMailStore.ArchiveAndDeleteBestEffort(record.Id);
                    if (!NativeMailCacheService.TryRemove(
                            recipientId, tag, record.Id, out _))
                    {
                        result = -1;
                        break;
                    }
                    result = 1;
                }
            }
            SendDefMessage(Grobal2.SM_CLEAR_ALLMAIL, result, 0, 0, 0, string.Empty);
        }

        private int FetchNativeMailAttachments(NativeMailCacheEntry entry)
        {
            var record = entry.Record;
            if (record.AttachStatus == 2) return -2;
            if (entry.Attachments.Count > BagCapacity.Of(this) - m_ItemList.Count) return -1;

            var orderId = record.MoneyCount > 0
                ? NativeMailStore.CreateMoneyOrderBestEffort(record, m_sCharName)
                : -1;

            if (record.MoneyType == 1)
            {
                if (record.MoneyCount <= 0) return -1;
                var recipientId = _nativeMailRecipientId;
                var request = new NativeYuanbaoRequest(recipientId, m_sUserID,
                    m_sCharName, record.MoneyCount,
                    NativeYuanbaoManager.AddOperation, orderId,
                    result => CompleteNativeMailYuanbaoClaim(recipientId, entry,
                        orderId, result));
                if (NativeYuanbaoManager.Enqueue(request)) return 0;

                NativeMailStore.SetMoneyOrderStatusBestEffort(orderId, 2);
                return -4;
            }

            if (record.MoneyType != 0) return -1;
            if (record.MoneyCount > 0)
            {
                // 0x70B7C0 e8 83 c1 fc ff  call 0x6D7948  overflow test
                // 0x70B7C5 84 c0 / 74     test al,al / je
                // 0x70B7C9 be fd ff ff ff  mov esi,-3     ; overflow -> return -3, gold/items untouched
                // The native add is 32-bit and can wrap; C# widens to 64-bit so a wrap
                // cannot sneak past as a grant. Fail-closed: refuse rather than credit.
                if ((long)m_nGold + record.MoneyCount > m_nGoldMax) return -3;
                // 0x70B7DB ff 91 8c 02 00 00  call [vmt+0x28C]  = IncGold (0x6D791C)
                // 0x70B7E1 84 c0 / 74 59     test al,al / je 0x70B83E
                // IncGold false does NOT become -3: native skips the success log and
                // still walks the MoneyType==0 deliver arm. GoldChanged lives inside
                // IncGold (0x6D793C call 0x6C19B4); do not credit through m_nGold +=.
                IncGold(record.MoneyCount);
            }

            NativeMailStore.SetMoneyOrderStatusBestEffort(orderId, 1);
            return DeliverNativeMailAttachments(entry);
        }

        private void CompleteNativeMailYuanbaoClaim(long recipientId,
            NativeMailCacheEntry entry, int orderId, NativeYuanbaoResult result)
        {
            var online = ResolveNativeMailClaimPlayer();
            if (result.ErrorCode != 0)
            {
                online?.SysMsg(
                    $"增加元宝失败 玩家:{m_sCharName} 错误信息：" +
                    NativeYuanbaoManager.GetErrorText(result.ErrorCode),
                    MsgColor.Red, MsgType.Hint);
                NativeMailStore.SetMoneyOrderStatusBestEffort(orderId, 2);
                online?.SendDefMessage(
                    Grobal2.SM_FETCH_ATTACH, -4, 0, 0, 0, string.Empty);
                return;
            }

            if (online != null)
            {
                online.m_nGameGold = result.Balance;
                online.GameGoldChanged();
                online.SysMsg($"邮件领取{entry.Record.MoneyCount}个元宝！",
                    MsgColor.Green, MsgType.Hint);
            }

            NativeMailStore.SetMoneyOrderStatusBestEffort(orderId, 1);
            var claimResult = -1;
            if (entry.Record.MailType == 4)
            {
                if (entry.Record.AttachStatus != 2)
                    SetNativeMailAttachStatus(recipientId, entry.Record, 2);
                claimResult = 1;
            }
            else if (online != null)
            {
                claimResult = online.DeliverNativeMailAttachments(entry);
            }

            online?.SendDefMessage(
                Grobal2.SM_FETCH_ATTACH, claimResult, 0, 0, 0, string.Empty);
        }

        private TPlayObject ResolveNativeMailClaimPlayer()
        {
            var online = M2Share.UserEngine?.GetPlayObject(m_sCharName);
            if (online == null || online.m_boGhost
                || !string.Equals(online.m_sUserID, m_sUserID,
                    StringComparison.OrdinalIgnoreCase))
                return null;
            return online;
        }

        /// <summary>
        /// 战神 <c>sub_70B458</c> — the mail attachment delivery loop.  Byte-exact shape:
        /// <code>
        /// 70B47A  or  esi,0xFFFFFFFF                       ; result := -1
        /// 70B47D  push [mail+0x24] / push [mail+0x20]
        /// 70B48A  call sub_656C14                          ; re-resolve the recipient PLAYER
        /// 70B48F  mov [ebp-0x10],eax
        /// 70B492  cmp [ebp-0x10],0 / je 0x70B5F7           ; gone -> ABORT, result stays -1,
        ///                                                 ;   AttachStatus is NEVER written
        /// 70B4A1  call [attachList+0x14]                   ; Count; 70B4A6 dec / jl -> 0x70B5E3
        /// 70B4B7  (loop head)  call [attachList+0x18]      ; item i
        /// 70B4DC  call sub_74DAE4                          ; build the item object
        /// 70B4E4  push 0 / xor ecx,ecx                     ; reason 0, stamper DISABLED
        /// 70B4F0  call [player vmt+0x248]                  ; the OUTER AddItemToBag
        /// 70B4F6  test al,al / je 0x70B5D9                 ; failure -> loop increment only
        /// 70B5DD  dec esi / jne 0x70B4B7                   ; (loop back-edge)
        /// 70B5E3  cmp byte [mail+0x4D],2 / je 0x70B5F2
        /// 70B5E9  mov dl,2 / call sub_70CB24               ; AttachStatus := 2
        /// 70B5F2  mov esi,1                                ; result := 1
        /// </code>
        /// TWO divergences from the discovery note, both re-checked against the bytes:
        /// (1) the recipient re-resolve is HOISTED OUT of the loop (one call at
        /// <c>0x70B48A</c> before the <c>0x70B4B7</c> loop head), not per-iteration;
        /// (2) native reaches the <c>AttachStatus := 2</c> write at <c>0x70B5E3</c>
        /// unconditionally after the loop, i.e. native ALSO marks claimed when an
        /// individual add failed.  The failed attachment is destroyed; native trades a
        /// LOSS window for the guarantee that a mail is claimable at most once.
        ///
        /// A <c>deliveredAll</c> guard used to sit here, justified by "marking claimed
        /// lets ClientClearAllNativeMail hard-delete the mail, an amplification native
        /// does not have".  That justification is false on the bytes: native's clear-all
        /// <c>sub_70D2D0</c> accepts exactly <c>MailStatus==2 &amp;&amp; AttachStatus in {2,3}</c>
        /// (<c>0x70D318 cmp byte[mail+0x4C],2</c>; <c>0x70D31E-0x70D327 add dl,0xFE / sub dl,2 / jae</c>)
        /// and calls <c>sub_70D350</c>, which runs <c>sub_70B0F0</c> =
        /// <c>sub_70AC7C</c> (<c>INSERT INTO %s.mailitem_b(...) SELECT ... FROM %s.mailitem
        /// WHERE idx = %d</c>) then <c>sub_70B00C</c>, and finally frees the object at
        /// <c>0x70D3C6</c>.  Native hard-deletes claimed mail exactly as this rewrite does.
        ///
        /// The guard's actual effect was an ITEM DUPLICATION window native does not have:
        /// leaving <c>AttachStatus == 1</c> after a partial delivery lets the whole
        /// attachment list — including the copies that already landed — be granted again on
        /// the next claim.  It is reachable through <see cref="CompleteNativeMailYuanbaoClaim"/>,
        /// which delivers on a re-resolved player without the pre-flight bag gate, exactly
        /// like native's own async arm at <c>0x70B294 call sub_70B458</c>; re-claiming there
        /// also re-credits the 元宝.
        /// </summary>
        private int DeliverNativeMailAttachments(NativeMailCacheEntry entry)
        {
            // 0x70B48A `call sub_656C14([mail+0x20],[mail+0x24])` = a lookup of the
            // recipient PLAYER OBJECT by the mail's stored recipient id
            // (sub_656C14 is a thunk: `mov eax,[engine+0x44]; call sub_49F98C`), then
            // 0x70B492 `cmp [ebp-0x10],0; je 0x70B5F7` -> return -1 with AttachStatus
            // NEVER written.  In this rewrite the claim is an INSTANCE method on that same
            // player, so `this` is already the resolved object; the faithful analogue of
            // "the object is gone" is the teardown flag, NOT a re-lookup by character name
            // (a different resolution key, which would also reject a legitimately
            // engine-unregistered player).
            if (m_boGhost) return -1;

            if (entry.Record.MailType != 4)
            {
                foreach (var attachment in entry.Attachments)
                {
                    var item = new TUserItem(attachment);
                    // 0x70B4E4-0x70B4F0: `push 0; xor ecx,ecx; call [vmt+0x248]` — the
                    // outer AddItemToBag with reason 0 and the stamper disabled.
                    if (!AddItemToBag(item,
                            NativeItemAcquisitionStamp.Reason.None, false))
                        continue;   // 0x70B4F8 je 0x70B5D9 — straight to the loop increment
                    SendAddItem(item);
                }
            }

            // 0x70B5E3-0x70B5ED: `cmp byte[mail+0x4D],2; jne` then sub_70CB24(dl=2).
            // Reached unconditionally after the loop; a partial delivery still closes the
            // mail, which is what keeps the attachment list one-shot.
            SetNativeMailAttachStatus(entry.Record, 2);
            return 1;   // 0x70B5F2 mov esi,1
        }

        private void SetNativeMailAttachStatus(NativeMailRecord record, byte status)
        {
            if (!TryGetNativeMailRecipientId(out var recipientId)) return;
            SetNativeMailAttachStatus(recipientId, record, status);
        }

        private static void SetNativeMailAttachStatus(long recipientId,
            NativeMailRecord record, byte status)
        {
            NativeMailCacheService.SetAttachStatus(
                recipientId, record.MailType, record.Id, status);
            NativeMailStore.SetAttachStatusBestEffort(record.Id, status);
        }

        private void ClientDeleteNativeMail(int mailId, int tag)
        {
            var result = -1;
            if (TryGetNativeMailRecipientId(out var recipientId)
                && NativeMailCacheService.TryFind(
                    recipientId, tag, mailId, out _))
            {
                NativeMailStore.ArchiveAndDeleteBestEffort(mailId);
                NativeMailCacheService.TryRemove(recipientId, tag, mailId, out _);
                result = 1;
            }

            SendDefMessage(Grobal2.SM_DEL_MAIL, result, 0,
                HUtil32.HiWord(mailId), HUtil32.LoWord(mailId), string.Empty);
        }

        private bool TryEnsureNativeMailCategory(int tag,
            out List<NativeMailCacheEntry> records)
        {
            records = null;
            if (!TryGetNativeMailRecipientId(out var recipientId)) return false;
            if (NativeMailCacheService.TryGetCategory(recipientId, tag, out records))
                return true;

            for (byte mailStatus = 1; mailStatus <= 2; mailStatus++)
            {
                if (NativeMailCacheService.IsStatusLoaded(
                        recipientId, tag, mailStatus))
                    continue;
                if (!NativeMailStore.TryLoadCategoryStatus(
                        recipientId, m_sCharName, tag, mailStatus,
                        out var loadedRecords, out _))
                    return false;
                NativeMailCacheService.MergeLoadedStatus(
                    recipientId, m_sCharName, tag, mailStatus,
                    DecodeNativeMailEntries(loadedRecords), DateTime.UtcNow);
            }
            return NativeMailCacheService.TryGetCategory(recipientId, tag, out records);
        }

        private bool TryBuildNativeMailListBody(int tag,
            List<NativeMailCacheEntry> cachedRecords, out byte[] body, out int count)
        {
            body = Array.Empty<byte>();
            count = 0;
            if (!NativeMailStore.IsSupportedTag(tag)) return false;

            var limit = tag == 1 ? 21 : tag == 5 ? 1 : 20;
            var records = cachedRecords.Take(limit).ToList();
            count = records.Count;
            using var stream = new MemoryStream();

            if (tag is 1 or 4)
            {
                foreach (var entry in records)
                {
                    var record = entry.Record;
                    stream.Write(NativeMailWireCodec.Encode(new NativeMailListInfo(
                        record.Id, record.Title, record.Sender, record.MailStatus,
                        record.AttachStatus, record.CreateDate.ToOADate())));
                }
            }

            if (tag == 4)
            {
                foreach (var entry in records)
                {
                    var attachment = entry.Attachments.FirstOrDefault();
                    if (attachment != null)
                        stream.Write(EncodeOwnedClientItemRecord(attachment));
                }
            }
            else if (tag == 5 && records.Count != 0)
            {
                if (TryGetNativeMailRecipientId(out var recipientId))
                    MarkNativeMailRead(recipientId, records[0].Record);
                stream.Write(BuildNativeMailInfoBody(records[0]));
            }
            else if (tag == 6)
            {
                var unreadIds = new List<int>();
                foreach (var entry in records)
                {
                    if (entry.Record.MailStatus != 2)
                    {
                        if (TryGetNativeMailRecipientId(out var recipientId)
                            && NativeMailCacheService.MarkRead(
                                recipientId, tag, entry.Record.Id))
                            unreadIds.Add(entry.Record.Id);
                    }
                }
                NativeMailStore.MarkReadBestEffort(unreadIds);

                foreach (var entry in records)
                {
                    var record = entry.Record;
                    stream.Write(NativeMailWireCodec.Encode(new NativeMailMessage(
                        record.Title, record.CreateDate.ToOADate(), record.Context)));
                }
            }

            body = stream.ToArray();
            return true;
        }

        private byte[] BuildNativeMailInfoBody(NativeMailCacheEntry entry)
        {
            var record = entry.Record;
            var attachments = record.MailType != 4 && record.AttachStatus == 1
                ? entry.Attachments
                : new List<TUserItem>();
            var gold = record.MoneyType == 1 ? 0 : record.MoneyCount;
            var yuanBao = record.MoneyType == 0 ? 0 : record.MoneyCount;
            using var stream = new MemoryStream();
            stream.Write(NativeMailWireCodec.Encode(new NativeMailInfo(
                record.Id, record.Sender, record.Title, record.Context,
                record.MailStatus, record.AttachStatus, record.MailType,
                record.CreateDate.ToOADate(), gold, yuanBao, attachments.Count, 0)));
            foreach (var attachment in attachments)
                stream.Write(EncodeOwnedClientItemRecord(attachment));
            return stream.ToArray();
        }

        private static void MarkNativeMailRead(long recipientId, NativeMailRecord record)
        {
            if (record.MailStatus == 2) return;
            if (NativeMailCacheService.MarkRead(
                    recipientId, record.MailType, record.Id))
                NativeMailStore.MarkReadBestEffort(new[] { record.Id });
        }
    }
}
