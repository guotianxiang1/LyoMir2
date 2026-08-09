namespace GameSvr
{
    // Dormant, evidence-backed model of the native chat-channel WRITE ops 4447-4452
    // (create/enter/exit/change-mode/change-mute/kick). Hex-Rays verified against M2Server
    // (image base 0x00400000). This is the reversed ground-truth ladder to verify the live
    // NativeChannelManager re-implementation against; it performs no writes and is not wired.
    //
    // Dispatch: each handler resolves a channel-role STRATEGY via sub_6F74EC(name,id) @0x006F74EC,
    // which returns one of three runtime singletons by the actor's channel role, then calls the op's
    // vtable slot; the reply is SendDefMessage(ident, wParam=code) via player.[vtbl+0x250].
    //   sub_6F74EC: not-in-channel -> *off_7D7210 (class off_6A72A4); in-channel non-owner ->
    //     *off_7D5BD4 (class off_6A7318); channel owner -> *off_7D5D18 (class off_6A7388).
    //   slots: +0x00 create, +0x04 enter, +0x08 exit, +0x0C mode, +0x10 kick, +0x14 mute.
    //
    // Strategy method -> core (verified):
    //   create +0x00 : not-in-channel sub_6A946C -> core sub_6A77C8; member sub_6A93E8 / owner
    //                  sub_6A940C -> -4 (already in a channel).
    //   enter  +0x04 : all roles sub_6A947C -> core sub_6A7C90.
    //   exit   +0x08 : not-in-channel sub_6A949C -> -13; member/owner sub_6A93F0 -> core sub_6A7D4C.
    //   mode   +0x0C : non-owner sub_6A94B4 -> -14; owner sub_6A9434 -> core sub_6A7DD8.
    //   kick   +0x10 : non-owner sub_6A94A8 -> -17; owner sub_6A9414 -> core sub_6A7E60.
    //   mute   +0x14 : non-owner sub_6A94C0 -> -22; owner sub_6A944C -> core sub_6A7F18.
    //
    // Core result ladders (exact):
    //   create sub_6A77C8 @0x006A77C8 : name exists -1 / >=50 channels -2 / 0.
    //   enter  sub_6A7C90 @0x006A7C90 : no channel -7 / closed -30 / actor offline -99 /
    //                                    bad password -8 / full -10 / 0.
    //   exit   sub_6A7D4C @0x006A7D4C : actor gone -99 / not in a channel -13 / channel gone -11 /
    //                                    not a member -12 / 0.
    //   mode   sub_6A7DD8 @0x006A7DD8 : actor gone -99 / channel mismatch -15 / channel gone -16 /
    //                                    not owner -14 / 0.
    //   kick   sub_6A7E60 @0x006A7E60 : actor gone -99 / channel mismatch -18 / channel gone -20 /
    //                                    not owner -17 / target not member -19 / target is owner -21 /
    //                                    else exit-core(target) (0 on success).
    //   mute   sub_6A7F18 @0x006A7F18 : actor gone -99 / channel mismatch -24 / channel gone -23 /
    //                                    not owner -22 / target not member -25 / target is owner -26 / 0.
    //
    // Handler-level pre-checks (before the strategy):
    //   4447 create sub_6F6D10 : payload <25 bytes -99 / password required-but-missing -5 /
    //                             bad type byte -6 / actor level <35 -3 / then strategy(create). On
    //                             create==0 it also enters and sends SM 4448 (= Enter type 0).
    //   4448 enter  sub_6F6EB4 : type 0/1 -> enter core; type 2/3/4 (guild/group scoped) ->
    //                             membership unresolved -9, else find-or-create scoped then enter core;
    //                             type >=5 -> -99.
    //   4451 mute   sub_6F7334 / 4452 kick sub_6F73A4 : target name unresolved (sub_652784) -> -27.

    public enum NativeChannelRole
    {
        NotInChannel = 0, // *off_7D7210 (class off_6A72A4)
        Member = 1,       // *off_7D5BD4 (class off_6A7318)
        Owner = 2,        // *off_7D5D18 (class off_6A7388)
    }

    public enum NativeChannelWriteOp
    {
        Create = 4447,
        Enter = 4448,
        Exit = 4449,
        ChangeMode = 4450,
        ChangeMute = 4451,
        KickOut = 4452,
    }

    public sealed class NativeChannelWriteContext
    {
        /// <summary>Actor's current channel role (sub_6F74EC dispatch).</summary>
        public NativeChannelRole Role { get; init; }

        // ---- 4447 create ----
        public bool CreatePayloadValid { get; init; } = true;   // request payload >= 25 bytes
        public bool CreatePasswordRequired { get; init; }        // password flag byte set
        public bool CreatePasswordProvided { get; init; }        // a real password value parsed (!= -1)
        public bool CreateTypeValid { get; init; } = true;       // *(req+24)-2 within [0,0xC7)
        public bool ActorLevelAtLeast35 { get; init; } = true;   // player level >= 0x23
        public bool ChannelNameExists { get; init; }             // create core: duplicate name
        public bool ChannelsAtMax { get; init; }                 // create core: >= 50 channels

        // ---- 4448 enter ----
        public byte EnterType { get; init; }                     // 0/1 direct, 2/3/4 scoped, >=5 invalid
        public bool ScopedMembershipResolved { get; init; }      // types 2/3/4 guild/group membership
        public bool EnterChannelFound { get; init; }             // enter core: sub_6A7B84
        public bool EnterChannelClosed { get; init; }            // enter core: channel closed flag
        public bool EnterActorOnline { get; init; } = true;      // enter core: sub_656C14
        public bool EnterPasswordOk { get; init; } = true;       // enter core: password matches
        public bool EnterChannelFull { get; init; }              // enter core: members >= capacity

        // ---- 4449 exit core ----
        public bool ExitActorOnline { get; init; } = true;
        public bool ExitInAChannel { get; init; }                // *(player+2784) > 0
        public bool ExitChannelExists { get; init; }             // sub_6A7B84
        public bool ExitIsMember { get; init; }                  // sub_6A8BB0

        // ---- 4450 change-mode core ----
        public bool ModeActorOnline { get; init; } = true;
        public bool ModeChannelMatch { get; init; }              // requested channel id == current
        public bool ModeChannelExists { get; init; }
        public bool ModeIsOwner { get; init; }

        // ---- 4451 mute / 4452 kick (shared shape) ----
        public bool TargetResolved { get; init; }                // sub_652784 target lookup
        public bool OpActorOnline { get; init; } = true;         // core sub_656C14 on the actor
        public bool OpChannelMatch { get; init; }                // requested channel id == current
        public bool OpChannelExists { get; init; }               // sub_6A7B84
        public bool OpIsOwner { get; init; }                     // actor is the channel owner
        public bool TargetIsMember { get; init; }                // sub_6A8BB0 on the target
        public bool TargetIsOwner { get; init; }                 // sub_6A8B84 on the target
    }

    public static class NativeChannelWriteTransaction
    {
        public const int VtblCreate = 0x00;
        public const int VtblEnter = 0x04;
        public const int VtblExit = 0x08;
        public const int VtblChangeMode = 0x0C;
        public const int VtblKickOut = 0x10;
        public const int VtblChangeMute = 0x14;
        public const int VtblSendDefMessage = 0x250;

        public const int Success = 0;

        /// <summary>Raw result code sent in the op's SM_CHANNEL_* reply (wParam).</summary>
        public static int Evaluate(NativeChannelWriteOp op, NativeChannelWriteContext c)
        {
            switch (op)
            {
                case NativeChannelWriteOp.Create:     return Create(c);
                case NativeChannelWriteOp.Enter:      return Enter(c);
                case NativeChannelWriteOp.Exit:       return Exit(c);
                case NativeChannelWriteOp.ChangeMode: return ChangeMode(c);
                case NativeChannelWriteOp.ChangeMute: return ChangeMute(c);
                case NativeChannelWriteOp.KickOut:    return KickOut(c);
                default: return -99;
            }
        }

        // 4447 create sub_6F6D10 -> strategy create -> core sub_6A77C8.
        private static int Create(NativeChannelWriteContext c)
        {
            if (!c.CreatePayloadValid) return -99;
            if (c.CreatePasswordRequired && !c.CreatePasswordProvided) return -5;
            if (!c.CreateTypeValid) return -6;
            if (!c.ActorLevelAtLeast35) return -3;
            if (c.Role != NativeChannelRole.NotInChannel) return -4; // member/owner strategy stub
            // create core sub_6A77C8
            if (c.ChannelNameExists) return -1;
            if (c.ChannelsAtMax) return -2;
            return Success;
        }

        // 4448 enter sub_6F6EB4 -> strategy enter (all roles) -> core sub_6A7C90.
        private static int Enter(NativeChannelWriteContext c)
        {
            if (c.EnterType >= 5) return -99;
            if (c.EnterType >= 2 && c.EnterType <= 4 && !c.ScopedMembershipResolved) return -9;
            return EnterCore(c);
        }

        private static int EnterCore(NativeChannelWriteContext c)
        {
            if (!c.EnterChannelFound) return -7;
            if (c.EnterChannelClosed) return -30;
            if (!c.EnterActorOnline) return -99;
            if (!c.EnterPasswordOk) return -8;
            if (c.EnterChannelFull) return -10;
            return Success;
        }

        // 4449 exit sub_6F72AC -> strategy exit -> core sub_6A7D4C.
        private static int Exit(NativeChannelWriteContext c)
        {
            if (c.Role == NativeChannelRole.NotInChannel) return -13; // strategy stub sub_6A949C
            if (!c.ExitActorOnline) return -99;
            if (!c.ExitInAChannel) return -13;
            if (!c.ExitChannelExists) return -11;
            if (!c.ExitIsMember) return -12;
            return Success;
        }

        // 4450 change-mode sub_6F72EC -> strategy mode -> core sub_6A7DD8.
        private static int ChangeMode(NativeChannelWriteContext c)
        {
            if (c.Role != NativeChannelRole.Owner) return -14; // strategy stub sub_6A94B4
            if (!c.ModeActorOnline) return -99;
            if (!c.ModeChannelMatch) return -15;
            if (!c.ModeChannelExists) return -16;
            if (!c.ModeIsOwner) return -14;
            return Success;
        }

        // 4451 change-mute sub_6F7334 -> target gate -27, then core sub_6A7F18 (owner authoritative).
        private static int ChangeMute(NativeChannelWriteContext c)
        {
            if (!c.TargetResolved) return -27;
            if (!c.OpActorOnline) return -99;
            if (!c.OpChannelMatch) return -24;
            if (!c.OpChannelExists) return -23;
            if (!c.OpIsOwner) return -22;
            if (!c.TargetIsMember) return -25;
            if (c.TargetIsOwner) return -26;
            return Success;
        }

        // 4452 kick sub_6F73A4 -> target gate -27, then core sub_6A7E60; success delegates to
        // exit-core(target), which returns 0 for the already-verified member target.
        private static int KickOut(NativeChannelWriteContext c)
        {
            if (!c.TargetResolved) return -27;
            if (!c.OpActorOnline) return -99;
            if (!c.OpChannelMatch) return -18;
            if (!c.OpChannelExists) return -20;
            if (!c.OpIsOwner) return -17;
            if (!c.TargetIsMember) return -19;
            if (c.TargetIsOwner) return -21;
            return Success;
        }
    }
}
