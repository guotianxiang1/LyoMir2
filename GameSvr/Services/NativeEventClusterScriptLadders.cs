namespace GameSvr
{
    // Dormant, fail-closed models of five previously un-modeled PAS-script
    // ("TPsNpc" procedure) handler ladders in the dynamic-room / 天关-关卡 /
    // event-cluster domain of the 战神 M2Server binary
    // (M2Server_unpacked_fixed.exe, image base 0x00400000,
    //  SHA256 5540f43b…c049670b14e).
    //
    // Each procedure is registered by the runtime table builder sub_731350
    // (name -> wrapper via sub_4F4180) and declared by the compiler-facing
    // signature table around 0x0072A9xx-0x0072BCxx. These five wrappers are
    // currently either absent from PasApiBridge or reduced to
    // RejectUnsupportedNativeApi()/loose approximations; none has an
    // evidence-backed model of the exact native decision ladder.
    //
    // These models capture only the PURE decision ladder of each wrapper.
    // Every polymorphic side-effect executor — the group range-mover
    // sub_727678, the couple-teleport executor sub_6CEF14, the SendMsg /
    // SendDelayMsg dispatch (sub_765E68 / sub_766060) and the SQL query/exec
    // manager (sub_724BE8 / sub_724E48) — is abstracted as an INPUT, never
    // synthesised here. Nothing in this file mutates game state; it is a
    // reference oracle for a later wiring pass and for AuditTools.

    // =====================================================================
    // 1. GroupFlyToDynRoomInRange  (wrapper sub_6E0734, declared at 0x0072D984)
    //    procedure GroupFlyToDynRoomInRange(roomName: string;
    //                                       roomIdx, x, y, iRange: Integer);
    //    Currently: PasApiBridge case "groupflytodynroominrange" =>
    //               RejectUnsupportedNativeApi().
    // =====================================================================

    /// <summary>Outcome of the sub_6E0734 decision ladder.</summary>
    public enum NativeGroupFlyToDynRoomInRangeOutcome
    {
        /// <summary>Caller has no group object ([self+0x0A80] == 0): silent no-op
        /// (native jz short loc_6E078D at 0x006E075E).</summary>
        NoGroup,

        /// <summary>Group exists but sub_5FCB78(mgr, roomName, roomIdx) returned
        /// no active environment (name has no definition, or no instance whose
        /// state byte +0xF0 == 2 has dynamic index +0xD4 == roomIdx): silent
        /// no-op (native test eax / jz at 0x006E0771-73).</summary>
        RoomNotActive,

        /// <summary>Both gates pass: dispatch the group range-mover sub_727678
        /// against the resolved active environment.</summary>
        DispatchRangeMove
    }

    /// <summary>
    /// Exact register/stack source mapping that sub_6E0734 feeds into the group
    /// range-mover sub_727678. sub_727678 computes, for every non-null group
    /// slot (index 0..10, [group+0x48+idx*4], skipping the leader at
    /// [group+0x3C]):
    ///   centerX = Ecx  - Radius + Random(2*Radius)   (member vtable[0x1C0] X)
    ///   centerY = Arg4 - Radius + Random(2*Radius)   (member vtable[0x1C0] Y)
    /// where Radius is sub_727678's first stack slot ([ebp+8]) and Arg4 is its
    /// second stack slot ([ebp+0x0C]).
    ///
    /// ABI note (resolved with peer pas-api; confirmed by the CreateDynRoomMon
    /// frame in createdynroommon_go_nogo_20260720.md, "+18=X … +08=MonNum"):
    /// the Delphi register convention passes Self/param1/param2 in EAX/EDX/ECX
    /// and pushes the REMAINING params LEFT-TO-RIGHT, so the leftmost extra sits
    /// at the HIGHEST [ebp] offset and IDA's arg_0 ([ebp+8]) is the RIGHTMOST
    /// (last) declared param. Hence for
    /// GroupFlyToDynRoomInRange(roomName, roomIdx, x, y, iRange): x = arg_8
    /// ([ebp+0x10]), y = arg_4 ([ebp+0x0C]), iRange = arg_0 ([ebp+8]). sub_6E0734
    /// wires ECX&lt;-arg_8=x, first-push&lt;-arg_4=y, last-push&lt;-arg_0=iRange,
    /// so the executor roles are the intuitive centerX=x, centerY=y,
    /// radius=iRange — identical to the sibling GroupFlyInRange (sub_6E07B4).
    /// </summary>
    public readonly struct NativeGroupRangeMoveDispatch
    {
        /// <summary>Declared param placed in ECX (sub_727678 centerX).</summary>
        public const string EcxSource = "x";           // param3, [self-wrapper arg_8]
        /// <summary>Declared param pushed FIRST (sub_727678 centerY, arg_4).</summary>
        public const string FirstPushedSource = "y";   // param4, [self-wrapper arg_4]
        /// <summary>Declared param pushed LAST (sub_727678 radius, arg_0).</summary>
        public const string LastPushedSource = "iRange"; // param5, [self-wrapper arg_0]

        /// <summary>The resolved active dynamic-room environment key (roomName).</summary>
        public string RoomName { get; init; }
        /// <summary>The resolved active dynamic-room index (roomIdx).</summary>
        public int RoomIdx { get; init; }
        /// <summary>Value routed to ECX / centerX (declared "x").</summary>
        public int EcxValue { get; init; }
        /// <summary>Value pushed first / centerY (declared "y").</summary>
        public int FirstPushedValue { get; init; }
        /// <summary>Value pushed last / radius (declared "iRange").</summary>
        public int LastPushedValue { get; init; }
    }

    /// <summary>Dormant model of GroupFlyToDynRoomInRange (sub_6E0734).</summary>
    public static class NativeGroupFlyToDynRoomInRangePlanner
    {
        public const int WrapperAddress = 0x006E0734;
        public const int RoomResolverAddress = 0x005FCB78;   // sub_5FCB78
        public const int RangeMoverAddress = 0x00727678;     // sub_727678

        /// <param name="hasGroup">[self+0x0A80] != 0 (group object present).</param>
        /// <param name="roomResolvesToActiveEnv">sub_5FCB78(mgr, roomName,
        /// roomIdx) returned a non-null active environment. This is exactly
        /// NativeDynamicRoomManager.TryGetActiveRoom(roomName, roomIdx): a
        /// definition lookup (sub_5FB800) followed by the active-instance
        /// lookup (sub_5FEA90, state +0xF0 == 2 &amp;&amp; index +0xD4 == roomIdx).</param>
        public static NativeGroupFlyToDynRoomInRangeOutcome Plan(
            bool hasGroup, bool roomResolvesToActiveEnv)
        {
            if (!hasGroup)
                return NativeGroupFlyToDynRoomInRangeOutcome.NoGroup;
            if (!roomResolvesToActiveEnv)
                return NativeGroupFlyToDynRoomInRangeOutcome.RoomNotActive;
            return NativeGroupFlyToDynRoomInRangeOutcome.DispatchRangeMove;
        }

        /// <summary>Builds the exact executor dispatch descriptor for the
        /// DispatchRangeMove case. Preconditions must already hold; this does
        /// NOT perform the move (the executor sub_727678 is an abstracted
        /// input).
        /// ABI (Delphi register convention, confirmed by CreateDynRoomMon frame):
        /// extra params pushed left-to-right so arg_0=[ebp+8] = LAST param.
        /// sub_6E0734: ECX &lt;- arg_8 = x, arg_4 = y, arg_0 = iRange (radius).
        /// </summary>
        public static NativeGroupRangeMoveDispatch BuildDispatch(
            string roomName, int roomIdx, int x, int y, int iRange)
        {
            return new NativeGroupRangeMoveDispatch
            {
                RoomName = roomName,
                RoomIdx = roomIdx,
                EcxValue = x,               // ECX  <- arg_8 = x (centerX)
                FirstPushedValue = y,       // arg_4 (first pushed)  = y (centerY)
                LastPushedValue = iRange    // arg_0 (last pushed)   = iRange (radius)
            };
        }
    }

    // =====================================================================
    // 2. AddGuanMoPoint  (wrapper sub_6EFB08, declared at 0x0072B9F9)
    //    procedure AddGuanMoPoint(gmPoint: Integer);
    //    Currently: absent from PasApiBridge (no model). This is a persisted
    //    event-point accumulator (the 天关/擂炮 observation-point ledger).
    // =====================================================================

    /// <summary>Which SQL statement sub_6EFB08 executes after its count probe.</summary>
    public enum NativeGuanMoPointStatement
    {
        /// <summary>Row-count probe returned &gt; 0: accumulate on the existing
        /// row (native test eax / jle at 0x006EFB62-64 falls through to the
        /// UPDATE branch).</summary>
        Update,

        /// <summary>Probe returned &lt;= 0 (no row, OR the probe itself failed —
        /// the native does not distinguish the two): create a new row. This is
        /// the native's own fail-open behaviour and is reproduced exactly.</summary>
        Insert
    }

    /// <summary>Resolved statement plus the literal SQL the native emits.</summary>
    public readonly struct NativeGuanMoPointPlan
    {
        public NativeGuanMoPointStatement Statement { get; init; }
        public string Sql { get; init; }
    }

    /// <summary>
    /// Dormant model of AddGuanMoPoint (sub_6EFB08). The DB manager
    /// (off_7D5C40 -> sub_724BE8 count probe, sub_724E48 exec) is abstracted as
    /// an input (the caller supplies the probe row count). There is no gate on
    /// gmPoint: a negative gmPoint subtracts via "ObPoint = ObPoint + gmPoint".
    /// CharName is [self+0x106]; PTID is the string field [self+0x0B09] and is
    /// upper-cased before being interpolated (0x006EFBBB call sub_40BC50) while
    /// CharName is not — see <see cref="UpperCaseNative"/>.
    /// <para>
    /// Note on the sibling storage: GetObPoint (sub_6F08AC) and DeleteObPoint
    /// (sub_6F0930) do NOT read this table. They operate on the TMirStringList at
    /// [0x7D5EC8], which no code path ever populates (all 11 image references are
    /// the two getters plus the shutdown saver sub_7931C8, which writes it to the
    /// text file 'ObPoint.txt' -- 0x793808, whose only reference 0x79345D is
    /// SaveToFile, there is no LoadFromFile). So natively the balance is
    /// write-only: GetObPoint always returns 0 and DeleteObPoint is always a
    /// no-op. That is reproduced verbatim in PasApiBridge; do not "repair" it.
    /// </para>
    /// </summary>
    public static class NativeGuanMoPointAccumulator
    {
        public const int WrapperAddress = 0x006EFB08;

        // Exact templates emitted by sub_6EFB08 (STRING_REFS 0x006EFC4C /
        // 0x006EFC90 / 0x006EFCE4). Argument order matches the native format
        // descriptor arrays byte-for-byte.
        public const string SelectTemplate =
            "Select * from gamedata.LiPaoObPoint where CharName=\"{0}\";";
        public const string UpdateTemplate =
            "Update gamedata.LiPaoObPoint Set ObPoint=ObPoint+{0} where Charname=\"{1}\";";
        public const string InsertTemplate =
            "insert into gamedata.LiPaoObPoint(PTID, CharName, ObPoint) values(\"{0}\",\"{1}\",{2});";

        /// <summary>
        /// The table DDL the native creates at startup (sub_725794, string
        /// 0x007257B4, executed via sub_724E48 with cl=1). Recorded because it
        /// proves the column widths/collation and the ABSENCE of a unique key on
        /// CharName, which is why the fail-open INSERT below can duplicate a row.
        /// </summary>
        public const string CreateTableSql =
            "Create Table if not Exists gamedata.LiPaoObPoint(Idx Int AUTO_INCREMENT " +
            "PRIMARY KEY, PTID char(32) binary not null, CharName char(15) binary " +
            "not null, ObPoint int default 0);";

        /// <summary>
        /// Delphi UpperCase (sub_40BC50) as applied to the PTID at 0x006EFBBB.
        /// The native loop (0x0040BC86..0x0040BCB3) only maps bytes in [0x61,0x7A]
        /// by subtracting 0x20 -- ASCII a-z only, no locale and no effect on GBK
        /// lead/trail bytes. <c>string.ToUpperInvariant</c> would also fold
        /// non-ASCII letters, so it must not be used here.
        /// </summary>
        public static string UpperCaseNative(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            var buffer = value.ToCharArray();
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] >= 'a' && buffer[i] <= 'z')
                    buffer[i] = (char)(buffer[i] - 0x20);
            }
            return new string(buffer);
        }

        /// <summary>The probe SQL the native issues first (0x006EFB49).</summary>
        public static string BuildProbeSql(string charName)
        {
            return string.Format(SelectTemplate, charName);
        }

        /// <param name="probeRowCount">Signed result of sub_724BE8. The native
        /// branches on <c>&gt; 0</c> (jle is signed).</param>
        public static NativeGuanMoPointPlan Plan(int probeRowCount, int gmPoint,
            string charName, string ptid)
        {
            if (probeRowCount > 0)
            {
                return new NativeGuanMoPointPlan
                {
                    Statement = NativeGuanMoPointStatement.Update,
                    Sql = string.Format(UpdateTemplate, gmPoint, charName)
                };
            }
            return new NativeGuanMoPointPlan
            {
                Statement = NativeGuanMoPointStatement.Insert,
                // 0x006EFBB0 shortstring->AnsiString, 0x006EFBBB UpperCase, and only
                // then the format descriptor slot (type 0x0B) at 0x006EFBC3.
                Sql = string.Format(InsertTemplate, UpperCaseNative(ptid), charName, gmPoint)
            };
        }
    }

    // =====================================================================
    // 3. RandomFlyTo  (wrapper sub_6DF7A8, declared at 0x0072B1BE)
    //    procedure RandomFlyTo(const MapName : string);
    //    Currently: PasApiBridge case "randomflyto" approximates with an
    //    immediate MapRandomMove; the native instead POSTS a self-message.
    // =====================================================================

    /// <summary>Outcome of the sub_6DF7A8 decision ladder.</summary>
    public enum NativeRandomFlyToOutcome
    {
        /// <summary>MapName is the empty Delphi string (nil pointer; native
        /// test esi / jz at 0x006DF7B1-B3): silent no-op.</summary>
        EmptyMapName,

        /// <summary>Non-empty MapName: post self-action message
        /// PostMessageId (0x2747) carrying MapName via sub_765E68.</summary>
        PostRandomFly
    }

    /// <summary>Dormant model of RandomFlyTo (sub_6DF7A8).</summary>
    public static class NativeRandomFlyToPlanner
    {
        public const int WrapperAddress = 0x006DF7A8;
        public const int SendMsgExecutorAddress = 0x00765E68;   // sub_765E68
        /// <summary>wIdent (CX) the native loads before sub_765E68 (0x2747).</summary>
        public const int PostMessageId = 0x2747;                // 10055

        public static NativeRandomFlyToOutcome Plan(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
                return NativeRandomFlyToOutcome.EmptyMapName;
            return NativeRandomFlyToOutcome.PostRandomFly;
        }
    }

    // =====================================================================
    // 4. CoupleFly  (wrapper sub_6E036C, declared at 0x0072B2BA;
    //                executor sub_6CEF14)
    //    procedure CoupleFly(const sTargetMap : string);
    //    Currently: PasApiBridge case "couplefly" => RejectUnsupportedNativeApi().
    // =====================================================================

    /// <summary>Outcome of the sub_6E036C wrapper + sub_6CEF14 executor ladder.</summary>
    public enum NativeCoupleFlyOutcome
    {
        /// <summary>Married flag byte [self+0x0B94] == 0: wrapper no-op
        /// (native cmp / jz at 0x006E0385-8C).</summary>
        NotMarried,

        /// <summary>Spouse name [self+0x0C48] is empty: executor no-op
        /// (sub_6CEF14 test ebx / jz at 0x006CEF20-22).</summary>
        NoSpouseName,

        /// <summary>Target map string is empty: executor no-op
        /// (sub_6CEF14 test edi / jz at 0x006CEF24-26).</summary>
        EmptyTargetMap,

        /// <summary>UserEngine.GetPlayObject(spouseName) (sub_652784) returned
        /// null — spouse offline: executor no-op (0x006CEF38-3A).</summary>
        SpouseOffline,

        /// <summary>Resolved spouse == self: executor no-op
        /// (cmp esi, ebx / jz at 0x006CEF3C-3E).</summary>
        SpouseIsSelf,

        /// <summary>Spouse predicate sub_772DA8(spouse) is true (blocking
        /// state): executor no-op (test al / jnz at 0x006CEF47-49).</summary>
        SpouseBlocked,

        /// <summary>Spouse environment [spouse+0x128] != self environment
        /// [self+0x128] — not on the same map: executor no-op
        /// (0x006CEF4B-57).</summary>
        DifferentEnvironment,

        /// <summary>All gates pass: move self to targetMap (sub_768C7C) then
        /// place spouse near self's new tile (sub_768CEC, X/Y = self tile
        /// +4 - Random(9) per axis).</summary>
        MoveBoth
    }

    /// <summary>
    /// Dormant model of CoupleFly (wrapper sub_6E036C -&gt; executor sub_6CEF14).
    /// The teleport executors (sub_768C7C self-move, sub_768CEC spouse-move) and
    /// the per-axis Random(9) jitter are abstracted as inputs; only the gate
    /// ladder is modeled. Evaluation order matches the native exactly and is
    /// short-circuit fail-closed.
    /// </summary>
    public static class NativeCoupleFlyPlanner
    {
        public const int WrapperAddress = 0x006E036C;
        public const int ExecutorAddress = 0x006CEF14;          // sub_6CEF14
        public const int SpouseLookupAddress = 0x00652784;      // sub_652784

        /// <param name="isMarried">byte [self+0x0B94] != 0.</param>
        /// <param name="spouseName">string [self+0x0C48].</param>
        /// <param name="targetMap">the CoupleFly argument.</param>
        /// <param name="spouseOnline">sub_652784 resolved a live spouse.</param>
        /// <param name="spouseIsSelf">resolved spouse reference == self.</param>
        /// <param name="spouseBlocked">sub_772DA8(spouse) is true.</param>
        /// <param name="sameEnvironment">[spouse+0x128] == [self+0x128].</param>
        public static NativeCoupleFlyOutcome Plan(bool isMarried,
            string spouseName, string targetMap, bool spouseOnline,
            bool spouseIsSelf, bool spouseBlocked, bool sameEnvironment)
        {
            if (!isMarried)
                return NativeCoupleFlyOutcome.NotMarried;
            if (string.IsNullOrEmpty(spouseName))
                return NativeCoupleFlyOutcome.NoSpouseName;
            if (string.IsNullOrEmpty(targetMap))
                return NativeCoupleFlyOutcome.EmptyTargetMap;
            if (!spouseOnline)
                return NativeCoupleFlyOutcome.SpouseOffline;
            if (spouseIsSelf)
                return NativeCoupleFlyOutcome.SpouseIsSelf;
            if (spouseBlocked)
                return NativeCoupleFlyOutcome.SpouseBlocked;
            if (!sameEnvironment)
                return NativeCoupleFlyOutcome.DifferentEnvironment;
            return NativeCoupleFlyOutcome.MoveBoth;
        }
    }

    // =====================================================================
    // 5. DoRelive  (wrapper sub_6E13C8, declared at 0x0072B42E)
    //    procedure DoRelive(const delayTime, hp : Integer);
    // =====================================================================

    /// <summary>Outcome of the sub_6E13C8 decision ladder.</summary>
    public enum NativeDoReliveOutcome
    {
        /// <summary>delayTime &lt;= 0 (native test esi / jle at
        /// 0x006E13D1-D3): silent no-op — no revive is scheduled.</summary>
        NonPositiveDelay,

        /// <summary>delayTime &gt; 0: schedule the delayed revive and send the
        /// immediate companion message.</summary>
        Schedule
    }

    /// <summary>Exact messages sub_6E13C8 emits on the Schedule path.</summary>
    public readonly struct NativeDoRelivePlan
    {
        public NativeDoReliveOutcome Outcome { get; init; }
        /// <summary>Delay for the scheduled message, milliseconds
        /// (delayTime * 1000; native imul esi, 0x3E8 at 0x006E13DE).</summary>
        public int DelayMilliseconds { get; init; }
        /// <summary>hp carried into the scheduled revive message.</summary>
        public int Hp { get; init; }
    }

    /// <summary>
    /// Dormant model of DoRelive (sub_6E13C8). On a positive delay the native
    /// (1) schedules SendDelayMsg wIdent DelayedMessageId (0x27B1) after
    /// delayTime*1000 ms carrying hp (sub_766060) and (2) sends the immediate
    /// SendMsg wIdent ImmediateMessageId (0x27B0) with param 0x3E9 and delayTime
    /// (sub_765E68). The message dispatch and the revive handler itself are
    /// abstracted as inputs; only the gate and the delay arithmetic are modeled.
    /// </summary>
    public static class NativeDoRelivePlanner
    {
        public const int WrapperAddress = 0x006E13C8;
        public const int SendDelayMsgAddress = 0x00766060;   // sub_766060
        public const int SendMsgAddress = 0x00765E68;        // sub_765E68
        public const int DelayedMessageId = 0x27B1;          // 10161, scheduled revive
        public const int ImmediateMessageId = 0x27B0;        // 10160, immediate companion
        public const int ImmediateMessageParam = 0x3E9;      // 1001
        public const int MillisecondsPerSecond = 0x3E8;      // 1000

        public static NativeDoRelivePlan Plan(int delayTime, int hp)
        {
            if (delayTime <= 0)
            {
                return new NativeDoRelivePlan
                {
                    Outcome = NativeDoReliveOutcome.NonPositiveDelay,
                    DelayMilliseconds = 0,
                    Hp = hp
                };
            }
            return new NativeDoRelivePlan
            {
                Outcome = NativeDoReliveOutcome.Schedule,
                DelayMilliseconds = delayTime * MillisecondsPerSecond,
                Hp = hp
            };
        }
    }

    // =====================================================================
    // Shared eligibility gate for the dynamic-room acquire/indexed fly
    // resolvers sub_5FB584 / sub_5FB714 (dynroom_fly_cluster_go_nogo_20260720.md;
    // bodies in pas-finish/ida-active-dynroom-managers.txt). Both start their
    // result at the failure sentinel and silently reject a null player, a
    // player whose ghost byte [player+0x73] != 0, or an object whose native
    // race byte [player+0x178] != 0 (i.e. require m_btRaceServer ==
    // RC_PLAYOBJECT). Confirmed opcodes: 0x005FB5AF/0x005FB5B5 and
    // 0x005FB73E/0x005FB744 (cmp byte [player+73h],0 / [player+178h],0).
    // =====================================================================
    public static class NativeDynRoomFlyEligibility
    {
        public static bool IsEligible(bool playerPresent, bool isGhost,
            bool raceIsPlayObject)
        {
            return playerPresent && !isGhost && raceIsPlayObject;
        }
    }

    // =====================================================================
    // 6. FlyToDynRoom  (wrapper sub_6DF088 -> resolver sub_5FB584)
    //    function FlyToDynRoom(sRoomName: string; x, y: Integer): Integer;
    //    Currently: PasApiBridge case "flytodynroom"/"flytodynenvirwithidx"
    //    => RejectUnsupportedNativeApi() (PasApiBridge.cs:1962).
    //    Wrapper sub_6DF088 is a thin delegate: it forwards (Self, sRoomName,
    //    x, y) to manager method sub_5FB584 and returns its int; there is no
    //    wrapper-level gate. The whole decision ladder lives in sub_5FB584.
    // =====================================================================

    /// <summary>Ladder outcome of resolver sub_5FB584 (acquire-and-fly).</summary>
    public enum NativeFlyToDynRoomOutcome
    {
        /// <summary>Null / ghost / non-playobject race: result stays -1.</summary>
        Ineligible,
        /// <summary>sub_5FB800(mgr, sRoomName) found no definition: -1.</summary>
        DefinitionMissing,
        /// <summary>Definition acquire virtual +0x8 (base sub_5FE6D8) yielded no
        /// environment (no idle instance and none constructed): -1.</summary>
        AcquisitionFailed,
        /// <summary>Acquired environment's blocked byte [env+0xF1] set: emits a
        /// server-side diagnostic only, does NOT move, returns -1.</summary>
        EnvironmentBlocked,
        /// <summary>Move via TPlayer VMT+0x1C0 (sub_6BD294) with x,y and zero
        /// flags; returns the environment's dynamic index [env+0xD4].</summary>
        MovedReturnsIndex
    }

    /// <summary>Resolved outcome plus the exact int the native returns.</summary>
    public readonly struct NativeFlyToDynRoomResult
    {
        public NativeFlyToDynRoomOutcome Outcome { get; init; }
        public int ResultIndex { get; init; }
    }

    /// <summary>
    /// Dormant model of FlyToDynRoom (sub_6DF088 -&gt; sub_5FB584). The virtual
    /// room-type acquisition (definition VMT+0x8 / sub_5FE6D8, which may create
    /// or select a player-name-associated instance) and the environment-pointer
    /// move (sub_6BD294) are abstracted as INPUTS; only the gate ladder and the
    /// int return contract are modeled. Every failure returns FailureIndex (-1).
    /// </summary>
    public static class NativeFlyToDynRoomPlanner
    {
        public const int WrapperAddress = 0x006DF088;
        public const int ResolverAddress = 0x005FB584;
        public const int AcquireVirtualBase = 0x005FE6D8;   // definition VMT+0x8
        public const int PlayerMoveVirtual = 0x006BD294;    // TPlayer VMT+0x1C0
        public const int FailureIndex = -1;

        /// <param name="eligiblePlayer">NativeDynRoomFlyEligibility.IsEligible.</param>
        /// <param name="definitionResolved">sub_5FB800 returned a definition.</param>
        /// <param name="acquiredEnvironment">acquire virtual yielded an env.</param>
        /// <param name="acquiredEnvironmentBlocked">[env+0xF1] set.</param>
        /// <param name="acquiredEnvironmentIndex">[env+0xD4] on success.</param>
        public static NativeFlyToDynRoomResult Plan(bool eligiblePlayer,
            bool definitionResolved, bool acquiredEnvironment,
            bool acquiredEnvironmentBlocked, int acquiredEnvironmentIndex)
        {
            if (!eligiblePlayer)
                return Fail(NativeFlyToDynRoomOutcome.Ineligible);
            if (!definitionResolved)
                return Fail(NativeFlyToDynRoomOutcome.DefinitionMissing);
            if (!acquiredEnvironment)
                return Fail(NativeFlyToDynRoomOutcome.AcquisitionFailed);
            if (acquiredEnvironmentBlocked)
                return Fail(NativeFlyToDynRoomOutcome.EnvironmentBlocked);
            return new NativeFlyToDynRoomResult
            {
                Outcome = NativeFlyToDynRoomOutcome.MovedReturnsIndex,
                ResultIndex = acquiredEnvironmentIndex
            };
        }

        private static NativeFlyToDynRoomResult Fail(
            NativeFlyToDynRoomOutcome outcome)
        {
            return new NativeFlyToDynRoomResult
            {
                Outcome = outcome,
                ResultIndex = FailureIndex
            };
        }
    }

    // =====================================================================
    // 7. FlyToDynEnvirWithIdx  (wrapper sub_6DF020 -> resolver sub_5FB714)
    //    function FlyToDynEnvirWithIdx(sRoomName: string; idx, x, y: Integer): Boolean;
    //    Wrapper sub_6DF020 is a thin delegate forwarding (Self, sRoomName,
    //    idx, x, y) to sub_5FB714 and returning its bool. Decision lives in
    //    sub_5FB714.
    // =====================================================================

    /// <summary>Ladder outcome of resolver sub_5FB714 (indexed fly).</summary>
    public enum NativeFlyToDynEnvirWithIdxOutcome
    {
        /// <summary>Null / ghost / non-playobject race: result stays false.</summary>
        Ineligible,
        /// <summary>sub_5FB800(mgr, sRoomName) found no definition: false.</summary>
        DefinitionMissing,
        /// <summary>Definition lookup virtual +0xC (sub_5FEA90) found no
        /// environment with state [env+0xF0]==2 AND index [env+0xD4]==idx (an
        /// idle/closing same-index instance is NOT reactivated): false.</summary>
        IndexNotActive,
        /// <summary>Target environment's blocked byte [env+0xF1] set: emits a
        /// server-side diagnostic only, does NOT move, returns false.</summary>
        EnvironmentBlocked,
        /// <summary>Move via TPlayer VMT+0x1C0 (sub_6BD294) with x,y and zero
        /// flags; returns true (native does not inspect the move result).</summary>
        MovedReturnsTrue
    }

    /// <summary>Resolved outcome plus the exact bool the native returns.</summary>
    public readonly struct NativeFlyToDynEnvirWithIdxResult
    {
        public NativeFlyToDynEnvirWithIdxOutcome Outcome { get; init; }
        public bool Result { get; init; }
    }

    /// <summary>
    /// Dormant model of FlyToDynEnvirWithIdx (sub_6DF020 -&gt; sub_5FB714). The
    /// active-instance lookup (sub_5FEA90) is exactly
    /// NativeDynamicRoomManager.TryGetActiveRoom's state/index contract; the
    /// environment-pointer move (sub_6BD294) is abstracted as an INPUT. Every
    /// failure returns false.
    /// </summary>
    public static class NativeFlyToDynEnvirWithIdxPlanner
    {
        public const int WrapperAddress = 0x006DF020;
        public const int ResolverAddress = 0x005FB714;
        public const int ActiveLookupVirtual = 0x005FEA90;  // definition VMT+0xC
        public const int PlayerMoveVirtual = 0x006BD294;    // TPlayer VMT+0x1C0

        /// <param name="eligiblePlayer">NativeDynRoomFlyEligibility.IsEligible.</param>
        /// <param name="definitionResolved">sub_5FB800 returned a definition.</param>
        /// <param name="activeIndexMatch">sub_5FEA90 found env with state
        /// +0xF0==2 and index +0xD4==idx.</param>
        /// <param name="targetEnvironmentBlocked">[env+0xF1] set.</param>
        public static NativeFlyToDynEnvirWithIdxResult Plan(bool eligiblePlayer,
            bool definitionResolved, bool activeIndexMatch,
            bool targetEnvironmentBlocked)
        {
            if (!eligiblePlayer)
                return Fail(NativeFlyToDynEnvirWithIdxOutcome.Ineligible);
            if (!definitionResolved)
                return Fail(NativeFlyToDynEnvirWithIdxOutcome.DefinitionMissing);
            if (!activeIndexMatch)
                return Fail(NativeFlyToDynEnvirWithIdxOutcome.IndexNotActive);
            if (targetEnvironmentBlocked)
                return Fail(NativeFlyToDynEnvirWithIdxOutcome.EnvironmentBlocked);
            return new NativeFlyToDynEnvirWithIdxResult
            {
                Outcome = NativeFlyToDynEnvirWithIdxOutcome.MovedReturnsTrue,
                Result = true
            };
        }

        private static NativeFlyToDynEnvirWithIdxResult Fail(
            NativeFlyToDynEnvirWithIdxOutcome outcome)
        {
            return new NativeFlyToDynEnvirWithIdxResult
            {
                Outcome = outcome,
                Result = false
            };
        }
    }
}
