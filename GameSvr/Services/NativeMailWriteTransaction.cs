using SystemModule;

namespace GameSvr.Services
{
    // Dormant, side-effect-free reference model of the native M2Server mail WRITE
    // result-code ladders. Every rung below is transcribed from the disassembly of
    //
    //     M2Server.exe  file version 1.0.1.135  size 7,774,208
    //     SHA-256 CC505716AEB2FDB09C96B805D06C1DDDCD70DB0F331EF42AE1338C71766B452F
    //
    // This type performs NO database access, NO packet send, and NO player/cache
    // mutation. It is a pure classifier: given the observable inputs of a native
    // mail write handler it returns the exact result code / reply disposition the
    // binary would produce. It exists so the live NativeMail* runtime and the
    // AuditTools/NativeMailWriteCompatCheck harness can be pinned to the reversed
    // ladders. It is intentionally not wired into the dispatcher.
    //
    // Native record field map (object at EAX in the claim/delete cores):
    //   +0x04 Id          +0x08 MailType(byte)     +0x48 attachment list
    //   +0x4C MailStatus  +0x4D AttachStatus       +0x50 MoneyType   +0x54 MoneyCount
    // Native player field map:
    //   +0x15C Gold  +0x68C GoldMax  +0x588/+0x58C 64-bit UserId
    public enum NativeMailWriteOp
    {
        ClaimAttach,          // CM/SM 4462  sub_6E7810 -> sub_7098D4 -> sub_70D498 -> sub_70B664
        ClaimAttachOffline,   // CM/SM 4468  sub_6E77AC -> sub_7098D4(tag=5) -> sub_70D498 -> sub_70B664
        DeleteMail,           // CM/SM 4463  sub_6E7888 -> sub_709830 -> sub_70D3D8 -> sub_70D350
        ClearAllMail,         // CM/SM 4495  sub_6E76A4 -> sub_7097F8 -> sub_70D2D0 -> sub_70D350
        MarkRead,             // read side effect  sub_70E240 -> sub_70C980
        YuanbaoClaimComplete, // async 4462/4468 completion  sub_70B144 (callback of sub_7114E4)
        SendMail              // NewFullMailEx  sub_7092D0 -> sub_70C570 / sub_70BBFC
    }

    // Outcome of a client-facing mail write op: the SM header the native
    // dispatcher wrapper emits, or SendsReply=false when the wrapper returns
    // without touching the socket (the "silent" branch).
    public readonly struct NativeMailWriteOutcome
    {
        public bool SendsReply { get; }
        public int ReplyIdent { get; }
        public int Recog { get; }
        public int Param { get; }
        public int Tag { get; }
        public int Series { get; }

        private NativeMailWriteOutcome(bool sendsReply, int replyIdent,
            int recog, int param, int tag, int series)
        {
            SendsReply = sendsReply;
            ReplyIdent = replyIdent;
            Recog = recog;
            Param = param;
            Tag = tag;
            Series = series;
        }

        internal static NativeMailWriteOutcome Silent() =>
            new(false, 0, 0, 0, 0, 0);

        internal static NativeMailWriteOutcome Reply(int ident, int recog,
            int param = 0, int tag = 0, int series = 0) =>
            new(true, ident, recog, param, tag, series);
    }

    // Result of the asynchronous yuanbao completion callback sub_70B144. The
    // callback mutates three things and conditionally sends SM 4462: the money
    // order status, the mail attachment status, and (when the recipient is still
    // online) the attachment claim reply.
    public readonly struct NativeMailYuanbaoCompletion
    {
        public byte MoneyOrderStatus { get; }   // Money_order.moneyStatus: 1 ok / 2 failed
        public bool SetsAttachClaimed { get; }   // mailitem.attachstatus <- 2
        public NativeMailWriteOutcome Reply { get; }

        internal NativeMailYuanbaoCompletion(byte moneyOrderStatus,
            bool setsAttachClaimed, NativeMailWriteOutcome reply)
        {
            MoneyOrderStatus = moneyOrderStatus;
            SetsAttachClaimed = setsAttachClaimed;
            Reply = reply;
        }
    }

    // sub_7092D0 has three terminal dispositions for a server-initiated send.
    // Send is not a client request/response, so it carries no SM code.
    public enum NativeMailSendDisposition
    {
        RejectedTooManyItems, // item groups > 6  -> sub_79DF74 fallback, no mail row
        InsertFailed,         // sub_70C570 SaveMailItem returned -1, no usable mail
        Created               // mailitem row written; attachments are best-effort
    }

    public static class NativeMailWriteTransaction
    {
        public const string Baseline = "M2Server.exe 1.0.1.135";
        public const string BaselineSha256 =
            "CC505716AEB2FDB09C96B805D06C1DDDCD70DB0F331EF42AE1338C71766B452F";

        // ---- Claim-core (sub_70B664) result ladder ----
        public const int Delivered = 1;         // items/gold delivered, attachstatus<-2
        public const int Failed = -1;           // bag full, or moneyType not 0/1, or offline deliver
        public const int AlreadyClaimed = -2;   // attachstatus already 2
        public const int GoldOverflow = -3;     // Gold + moneyCount would exceed GoldMax
        public const int YuanbaoClaimFailed = -4; // async yuanbao op failed (sub_70B144)
        public const int NotInSafeZone = -5;    // 4462 wrapper safe-zone gate (sub_6E7810)
        // 0 has two meanings on the wire and both are silent: the async yuanbao
        // request was enqueued and its reply is owed by the callback, OR the
        // mailbox/mail was not found. The dispatcher wrapper sends nothing for 0.
        public const int Pending = 0;

        // ---- Native yuanbao account-op codes (sub_710C60 family) ----
        // The mail claim path always submits operation 0 (add) with a positive
        // amount, so of these only YbSuccess / YbInvalidUserId / YbSqlFailure are
        // reachable via mail. InsufficientBalance is subtract-only and
        // NegativeAmount cannot occur because mail moneyCount is > 0.
        public const int YbSuccess = 0;
        public const int YbInvalidUserId = -1500001;
        public const int YbInsufficientBalance = -1500002;
        public const int YbSqlFailure = -1500003;
        public const int YbNegativeAmount = -1500004;

        // Categories the native mailbox recognises. sub_70DBCC gates on bit N of
        // dword_7D3DE8 for N<=7; dword_7D3DE8 reads 7E 8D 40 00 and has exactly one
        // reference image-wide (the read inside the bt at 0x70DBD7), so the set is
        // 1..6 — 1 系统 / 2 任务奖励 / 3 离线补偿 / 4 物品售卖 / 5 过期返还 / 6 摊位留言,
        // named at 0x7D3DEC[1..6]. An unsupported tag makes the clear-all core fall
        // straight through to its -1 default.
        public static bool IsSupportedTag(int tag) => tag is >= 1 and <= 6;

        // =====================================================================
        // Claim core: sub_70B664 (shared by 4462 and 4468 through sub_70D498).
        // Returns the raw ladder value; 0 means "no synchronous reply" (async
        // yuanbao in flight). sub_70D498 substitutes 0 when the mail is absent,
        // which the wrapper treats identically to this 0.
        // =====================================================================
        public static int ClassifyClaimCore(
            byte attachStatus, int attachmentCount, int freeBagSlots,
            int moneyType, int moneyCount, byte mailType,
            bool goldWouldOverflow, bool recipientOnline)
        {
            // sub_70B664: cmp byte[a1+0x4D],2
            if (attachStatus == 2) return AlreadyClaimed;

            // if ( attachmentCount <= sub_7481F4() )  ... else falls to -1 default
            if (attachmentCount > freeBagSlots) return Failed;

            // if ( moneyType==1 && moneyCount>0 ) -> enqueue sub_7114E4, v3 = 0
            if (moneyType == 1 && moneyCount > 0) return Pending;

            // The native else-chain only continues for moneyType==0. moneyType==1
            // with moneyCount<=0, and any moneyType>=2, leave v3 at its -1 default.
            if (moneyType != 0) return Failed;

            // moneyType==0, moneyCount>0: sub_6D7948 overflow guard -> v3=-3
            if (moneyCount > 0 && goldWouldOverflow) return GoldOverflow;

            // sub_70C484(order,1); mail type 4 sets attachstatus=2 without delivery
            if (mailType == 4) return Delivered;

            // otherwise sub_70B458: 1 if the recipient is online, else -1
            return recipientOnline ? Delivered : Failed;
        }

        // =====================================================================
        // 4462 online claim wrapper: sub_6E7810.
        // Safe-zone gate first (sub_7684DC); on failure it replies -5. Otherwise
        // it runs the claim core and replies only when the result is nonzero.
        // =====================================================================
        public static NativeMailWriteOutcome ClaimAttachOnline(
            bool inSafeZone, int claimCoreResult)
        {
            if (!inSafeZone)
                return NativeMailWriteOutcome.Reply(Grobal2.SM_FETCH_ATTACH, NotInSafeZone);
            if (claimCoreResult == Pending)
                return NativeMailWriteOutcome.Silent();
            return NativeMailWriteOutcome.Reply(Grobal2.SM_FETCH_ATTACH, claimCoreResult);
        }

        // =====================================================================
        // 4468 offline claim wrapper: sub_6E77AC -> sub_7098D4(tag=5).
        // No safe-zone gate; tag is hard-coded 5; input mail id is the request
        // Recog. Replies only when the core result is nonzero.
        // =====================================================================
        public static NativeMailWriteOutcome ClaimAttachOffline(int claimCoreResult)
        {
            if (claimCoreResult == Pending)
                return NativeMailWriteOutcome.Silent();
            return NativeMailWriteOutcome.Reply(
                Grobal2.SM_FETCH_ATTACH_OFFTM, claimCoreResult);
        }

        // =====================================================================
        // 4463 delete wrapper: sub_6E7888 -> sub_709830 -> sub_70D3D8.
        //   mailbox missing  -> sub_709830 returns 0    -> silent
        //   mail not found   -> sub_70D3D8 returns -1    -> reply -1
        //   mail removed     -> sub_70D350 returns 1     -> reply 1
        // The reply echoes the 32-bit mail id split into Tag=hi, Series=lo.
        // =====================================================================
        public static NativeMailWriteOutcome DeleteMail(
            bool mailboxExists, bool mailFound, int mailId)
        {
            if (!mailboxExists) return NativeMailWriteOutcome.Silent();
            var result = mailFound ? Delivered : Failed;
            return NativeMailWriteOutcome.Reply(Grobal2.SM_DEL_MAIL, result, 0,
                (mailId >> 16) & 0xFFFF, mailId & 0xFFFF);
        }

        // =====================================================================
        // 4495 clear-all wrapper: sub_6E76A4 -> sub_7097F8 -> sub_70D2D0.
        //   mailbox missing              -> sub_7097F8 returns 0 -> silent
        //   unsupported tag / none ready -> sub_70D2D0 -1 default -> reply -1
        //   at least one removed         -> reply 1
        // =====================================================================
        public static NativeMailWriteOutcome ClearAllMail(
            bool mailboxExists, int tag, int eligibleRemovedCount)
        {
            if (!mailboxExists) return NativeMailWriteOutcome.Silent();
            if (!IsSupportedTag(tag))
                return NativeMailWriteOutcome.Reply(Grobal2.SM_CLEAR_ALLMAIL, Failed);
            var result = eligibleRemovedCount > 0 ? Delivered : Failed;
            return NativeMailWriteOutcome.Reply(Grobal2.SM_CLEAR_ALLMAIL, result);
        }

        // Per-mail eligibility for the 4495 scan (sub_70D0CC / sub_70D2D0 inline):
        // read AND (claimed OR no-attachment).
        public static bool IsClearAllEligible(byte mailStatus, byte attachStatus) =>
            mailStatus == 2 && (attachStatus == 2 || attachStatus == 3);

        // =====================================================================
        // Mark-read side effect: sub_70E240 -> sub_70C980.
        // The status write (and unread-counter decrement) happens only when the
        // requested status differs from the current one; marking read is a no-op
        // when the mail is already read. There is no distinct SM code.
        // =====================================================================
        public static bool MarkReadWriteOccurs(byte currentMailStatus) =>
            currentMailStatus != 2;

        // =====================================================================
        // Async yuanbao completion: sub_70B144. Contract: ECX = account-op result
        // (0 == success). The mail claim always uses operation 0 (add).
        //   success: money order -> 1; type-4 sets attachstatus=2, other types
        //            deliver when online; reply SM 4462 with the delivery result
        //            (1 online, -1 offline) but only while the player is online.
        //   failure: money order -> 2; no delivery; reply SM 4462 = -4 while online.
        //   offline at completion: no reply is sent in either case.
        // =====================================================================
        public static NativeMailYuanbaoCompletion YuanbaoClaimComplete(
            int yuanbaoResult, byte mailType, bool recipientOnline)
        {
            if (yuanbaoResult == YbSuccess)
            {
                var delivered = mailType == 4
                    ? Delivered
                    : recipientOnline ? Delivered : Failed;
                var reply = recipientOnline
                    ? NativeMailWriteOutcome.Reply(Grobal2.SM_FETCH_ATTACH, delivered)
                    : NativeMailWriteOutcome.Silent();
                // type-4 always ends claimed; other types only when delivered online.
                var setsClaimed = mailType == 4 || (recipientOnline && delivered == Delivered);
                return new NativeMailYuanbaoCompletion(1, setsClaimed, reply);
            }

            var failReply = recipientOnline
                ? NativeMailWriteOutcome.Reply(Grobal2.SM_FETCH_ATTACH, YuanbaoClaimFailed)
                : NativeMailWriteOutcome.Silent();
            return new NativeMailYuanbaoCompletion(2, false, failReply);
        }

        // =====================================================================
        // Server-initiated send: sub_7092D0 (via TPlayer/Global NewFullMailEx).
        //   item groups > 6           -> sub_79DF74 fallback, no mail written
        //   sub_70C570 SaveMailItem -1 -> no usable mail row
        //   otherwise                 -> mail created; attachments are best-effort
        // =====================================================================
        public static NativeMailSendDisposition ClassifySend(
            int itemGroupCount, bool mailRowInserted)
        {
            if (itemGroupCount > 6) return NativeMailSendDisposition.RejectedTooManyItems;
            return mailRowInserted
                ? NativeMailSendDisposition.Created
                : NativeMailSendDisposition.InsertFailed;
        }

        // Native address map, surfaced so the audit harness can print the exact
        // provenance of each modelled ladder.
        public static IReadOnlyList<(NativeMailWriteOp Op, string Address, string Note)>
            CoverageMap { get; } = new[]
        {
            (NativeMailWriteOp.ClaimAttach,
                "0x006E7810/0x0070B664", "safe-zone -5 gate; core -2/-1/-3/0/1"),
            (NativeMailWriteOp.ClaimAttachOffline,
                "0x006E77AC/0x007098D4", "tag=5 hard-coded; no safe-zone gate"),
            (NativeMailWriteOp.DeleteMail,
                "0x006E7888/0x0070D3D8", "silent/-1/1; echoes mail id in Tag/Series"),
            (NativeMailWriteOp.ClearAllMail,
                "0x006E76A4/0x0070D2D0", "silent/-1/1; read AND claimed-or-empty scan"),
            (NativeMailWriteOp.MarkRead,
                "0x0070E240/0x0070C980", "status write only when not already read"),
            (NativeMailWriteOp.YuanbaoClaimComplete,
                "0x0070B144", "order 1/2; deliver online; SM 4462 1/-1/-4"),
            (NativeMailWriteOp.SendMail,
                "0x007092D0/0x0070C570", "reject >6 items; SaveMailItem -1 fails")
        };
    }
}
