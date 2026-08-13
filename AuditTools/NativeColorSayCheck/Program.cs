// 彩色文字 (ColorSay) tier + duration 审计
//
// 战神真相（逐字节，2026-08-08 全镜像普查）：
//
//   obj+0xB86 = 颜色档位字节，obj+0xBD4 = 剩余秒数。
//   obj+0xB86 全镜像只有 6 个 disp32 原始命中、4 个真实指令引用：
//     0x6B0495  mov byte [edx+0xB86],al      <- LOAD, rec 0xD5, 无校验
//     0x6B1376  mov al,byte [ebx+0xB86]      -> SAVE, rec 0xD5
//     0x6C9442  mov al,byte [esi+0xB86]      -- say 路径选色
//     0x786845  mov byte [esi+0xB86],al      <- 发放 sub_786800
//   ⚠️ 没有 clamp、**没有清零**。tick sub_6CCBC4 的第三块
//     0x6CCC91..0x6CCCAF 只碰 obj+0xBD4，档位字节到期后**粘住**。
//
//   SAVE 的不对称是本审计的核心：
//     0x6B1356  mov eax,[ebx+0xBD4]
//     0x6B135C  test eax,eax
//     0x6B135E  jle 0x6B1376        ; 倒计时 <=0 时跳过 rec 0x120 的存储
//     0x6B1360..0x6B1375            ; (被跳过的 0x120 deadline 写入)
//     0x6B1376  mov al,[ebx+0xB86]  ; <- 跳转落点，故颜色**无条件**写
//     0x6B137C  mov [esi+0xD5],al
//   即：倒计时可以不写，档位一定写。
//
//   发放 sub_786800 = TColorSayProp VMT 0x77F7C8 槽 +0x18
//   （SelfPtr 自校验通过、类名读出 'TColorSayProp'；我早先 brief 里写的
//    0x77F7A4 +0x3C 是错的——那是 vmtParent 槽，两种写法指向同一 dword 所以
//    看起来"对"）：
//     0x78680F  test ecx,ecx                 ; player 为 nil        -> 失败
//     0x786813  cmp byte [ecx+0x178],0       ; race != 0            -> 失败
//     0x78681E  cmp dword [esi+0xBD4],0      ; 已在生效 -> 失败(不叠加)
//     0x786827  mov eax,[ebx+0x1C]           ; item -> StdItem
//     0x78682A  movzx eax,word [eax+0x1C]    ; StdItem.DuraMax
//     0x786835  div edi                      ; edi=1000，**无符号截断**
//     0x786837  imul edi,eax,0x15180         ; 0x15180=86400 -> **天**不是小时
//     0x786840  mov al,byte [eax+0x15]       ; Shape
//     0x786843  sub al,0x16                  ; -> 档位
//     0x786845  mov [esi+0xB86],al
//     0x78684B  mov [esi+0xBD4],edi
//     0x786851  mov byte [ebp-1],1           ; 成功标志
//   与经验发放 sub_786390 的对照（同一 +0x1C 惯用法，坐实字段身份）：
//     0x786480/0x786483 movzx eax,word [eax+0x1C] / 0x78648E div 1000
//     0x786493 imul 0xE10  -> 那个是**小时**(0xE10=3600)
//     0x7863D2 movzx esi,byte [eax+0x17]  ; AniCount = 倍率，2..0x40 clamp
//
//   消费者 sub_6C9354（普通说话，唯一调用点 0x6BB907）：
//     0x6C93FF  cmp dword [esi+0xBD4],0 / jne 0x6C9442   ; 倒计时门
//     0x6C9442  mov al,[esi+0xB86]
//     0x6C9448  cmp al,1 / 0x6C944C mov ax,0xFFF5
//     0x6C9454  cmp al,2 / 0x6C9456 mov ax,0xFFFA
//     0x6C945C  mov ax,0xFF01                            ; 其它一切值
//   注意档位 3（Shape 25）落的是 else 分支，与档位 99 走同一条路。

using System.Reflection;
using System.Text;

namespace NativeColorSayCheck
{
    internal static class Program
    {
        private static int _assertions;
        private static readonly List<string> Failures = new();

        private static void True(bool condition, string what)
        {
            _assertions++;
            if (!condition) Failures.Add(what);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            _assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                Failures.Add($"{what}: expected={expected} actual={actual}");
        }

        private static void NotEqual<T>(T unexpected, T actual, string what)
        {
            _assertions++;
            if (EqualityComparer<T>.Default.Equals(unexpected, actual))
                Failures.Add($"{what}: must not be {unexpected}");
        }

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                PrepareRuntimeConfig();
                InitializeRuntime();

                CheckOffsetConstants();
                CheckTierRoundTrip();
                CheckSaveOfTierIsUnconditional();
                CheckTierIsStickyAcrossExpiry();
                CheckTierIsNotValidatedOnLoad();
                CheckGrantGates();
                CheckGrantArithmetic();
                CheckGrantIsWiredToItemUse();
                CheckFactoryShapesMatchTierRange();
                CheckAntiDecExpAccumulatesNotRefuses();
                CheckAntiDecExpUsesHoursNotDays();
                CheckDoubleExpMultiplierTruncates();
                CheckDoubleExpMessagesSubstitute();
                CheckSiblingGrantersAreWired();
                CheckNetCafeElementIsDerivedNotGuessed();
                CheckNetCafeFlagRequiresBothHalves();
                CheckNetCafeFlagIsReadOnlyAndSuffixDerived();
                CheckNetCafeRefusalLadderPosition();
                CheckNoInventedCafeInputs();
            }
            catch (Exception ex)
            {
                Failures.Add("unexpected exception: " + ex);
            }

            if (Failures.Count == 0)
            {
                Console.WriteLine(
                    $"AUDIT_PASS NativeColorSayCheck {_assertions} assertions");
                // Scope grew past the directory name: this now covers all three
                // StdMode-1 timed-buff granters, because their differences are
                // exactly what a porter gets wrong.
                Console.WriteLine(
                    "  sub_786390 TDoubleExpProp   obj+0xBB8/0xBBC  hours 0xE10   "
                    + "accumulate; different multiplier while active REFUSES");
                Console.WriteLine(
                    "  sub_7865B4 TAntiDecExpProp  obj+0xBD0        hours 0xE10   "
                    + "accumulate; cap 0x7A1200 tested with jg (at-cap still tops up)");
                Console.WriteLine(
                    "  sub_786800 TColorSayProp    obj+0xBD4/0xB86  DAYS 0x15180  "
                    + "NON-stacking; tier byte sticky across expiry");
                Console.WriteLine(
                    "  persisted: rec 0xD5 <-> obj+0xB86, saved UNCONDITIONALLY at "
                    + "0x6B1376 (the jle target that skips the 0x120 store)");
                Console.WriteLine(
                    "  cafe refusal 0x786443..0x78646F NOW PORTED: obj+0xB74 = "
                    + "bit 0x10 of session-suffix byte 0x56");
                Console.WriteLine(
                    "    (raw+0xEF00 == SessionSuffixOffset), AND element 27 of the "
                    + "5-byte ServerSwitch set. Derived on read,");
                Console.WriteLine(
                    "    never latched and never written back -- native has zero "
                    + "writers of tail 0xEF56 and no IP check at all.");
                return 0;
            }

            Console.WriteLine("AUDIT_FAIL NativeColorSayCheck");
            foreach (var f in Failures) Console.WriteLine("  - " + f);
            return 1;
        }

        // ---------- runtime bring-up ----------

        private static void PrepareRuntimeConfig()
        {
            var directory = AppContext.BaseDirectory;
            File.WriteAllText(Path.Combine(directory, "!Setup.txt"), "[Server]");
            File.WriteAllText(Path.Combine(directory, "String.ini"), "[String]");
            File.WriteAllText(Path.Combine(directory, "Command.conf"), "[Command]");
            var share = Path.GetFullPath(Path.Combine(directory, "..", "Share"));
            Directory.CreateDirectory(share);
            File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
                "[PlayerLevelExp]");
            File.WriteAllText(Path.Combine(share, "ServerData.ini"), "[Integer]");
        }

        private static void InitializeRuntime()
        {
            GameSvr.M2Share.g_Config = new GameSvr.GameSvrConfig();
            GameSvr.M2Share.ObjectManager = new GameSvr.ObjectManager();
            GameSvr.M2Share.ProcessMsgCriticalSection = new object();
            GameSvr.M2Share.ProcessHumanCriticalSection = new object();
            GameSvr.M2Share.LogMsgCriticalSection = new object();
            GameSvr.M2Share.LogStringList = new System.Collections.ArrayList();
        }

        // ---------- reflection plumbing ----------

        private static Type PlayObjectType => typeof(GameSvr.TPlayObject);

        private static int ConstInt(string name)
        {
            var field = PlayObjectType.GetField(name,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (field == null) throw new MissingFieldException(name);
            return (int)field.GetRawConstantValue();
        }

        private static int RecordSize => DBSvr.Core.NativeHumanDataCodec.DataRecordSize;

        private static int NativeHumanDataCodecRecordSize =>
            DBSvr.Core.NativeHumanDataCodec.DataRecordSize;

        private static GameSvr.TPlayObject NewPlayer()
        {
            var player = (GameSvr.TPlayObject)Activator.CreateInstance(
                PlayObjectType, nonPublic: true);
            SetRecord(player, new byte[RecordSize]);
            return player;
        }

        private static void SetRecord(GameSvr.TPlayObject player, byte[] raw) =>
            Field("m_NativeHumanData").SetValue(player, raw);

        private static byte[] GetRecord(GameSvr.TPlayObject player) =>
            (byte[])Field("m_NativeHumanData").GetValue(player);

        private static FieldInfo Field(string name)
        {
            var field = PlayObjectType.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new MissingFieldException(name);
            return field;
        }

        private static byte GetTier(GameSvr.TPlayObject p) =>
            (byte)Field("m_btNativeColorSayTier").GetValue(p);

        private static void SetTier(GameSvr.TPlayObject p, byte v) =>
            Field("m_btNativeColorSayTier").SetValue(p, v);

        private static int GetSeconds(GameSvr.TPlayObject p) =>
            (int)Field("m_nNativeThirdBuffSeconds").GetValue(p);

        private static void SetSeconds(GameSvr.TPlayObject p, int v) =>
            Field("m_nNativeThirdBuffSeconds").SetValue(p, v);

        private static void SetRace(GameSvr.TPlayObject p, byte v) =>
            Field("m_btRaceServer").SetValue(p, v);

        private static void Restore(GameSvr.TPlayObject p) =>
            Method("RestoreNativeUnmappedScalars").Invoke(p, null);

        private static bool Persist(GameSvr.TPlayObject p) =>
            (bool)Method("PersistNativeUnmappedScalars").Invoke(p, null);

        private static bool Grant(GameSvr.TPlayObject p, byte shape, ushort dura) =>
            (bool)Method("GrantNativeColorSay").Invoke(p, new object[] { shape, dura });

        private static MethodInfo Method(string name)
        {
            var method = PlayObjectType.GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (method == null) throw new MissingMethodException(name);
            return method;
        }

        // ---------- checks ----------

        private static void CheckOffsetConstants()
        {
            // enc 0x6B137C `mov byte [esi+0xD5],al` / dec 0x6B0495
            Equal(0x00D5, ConstInt("NativeColorSayTierOffset"),
                "tier rec offset must be 0xD5 per enc 0x6B137C / dec 0x6B0495");
            // 0x786837 imul edi,eax,0x15180
            Equal(0x15180, ConstInt("NativeColorSayGrantUnitSeconds"),
                "grant unit must be 86400 (a DAY) per 0x786837 imul 0x15180");
            // guard against someone "harmonising" it with the exp granter's hour
            NotEqual(0x0E10, ConstInt("NativeColorSayGrantUnitSeconds"),
                "colour-say unit is NOT the exp granter's 0xE10 hour");
        }

        private static void CheckTierRoundTrip()
        {
            foreach (var tier in new byte[] { 0, 1, 2, 3, 200, 255 })
            {
                var player = NewPlayer();
                SetTier(player, tier);
                True(Persist(player), "persist must succeed on a full-length record");
                var raw = GetRecord(player);
                Equal(tier, raw[ConstInt("NativeColorSayTierOffset")],
                    $"persisted rec 0xD5 byte for tier={tier}");

                var reloaded = NewPlayer();
                SetRecord(reloaded, raw);
                // prove Restore actually assigns rather than leaving the default
                SetTier(reloaded, (byte)(tier ^ 0xFF));
                Restore(reloaded);
                Equal(tier, GetTier(reloaded), $"round-trip tier={tier}");
            }
        }

        private static void CheckSaveOfTierIsUnconditional()
        {
            // THE ASYMMETRY. 0x6B135E `jle 0x6B1376` skips the 0xBD4 -> rec 0x120
            // countdown store, but 0x6B1376 is the jump TARGET, so the tier byte is
            // written on every save -- including when the countdown is 0 or negative.
            foreach (var seconds in new[] { 0, -1, int.MinValue })
            {
                var player = NewPlayer();
                SetSeconds(player, seconds);
                SetTier(player, 2);
                True(Persist(player), $"persist must succeed with seconds={seconds}");
                Equal((byte)2, GetRecord(player)[ConstInt("NativeColorSayTierOffset")],
                    $"tier must be saved even when the countdown is {seconds} "
                    + "(0x6B1376 is the jle target, not inside the guarded block)");
            }
        }

        private static void CheckTierIsStickyAcrossExpiry()
        {
            // tick sub_6CCBC4 block C (0x6CCC91..0x6CCCAF) zeroes obj+0xBD4 only.
            // There is NO writer of obj+0xB86 anywhere in the tick, so the tier
            // must survive expiry untouched.
            var player = NewPlayer();
            SetTier(player, 2);
            SetSeconds(player, 5);
            Field("m_dwNativeTimedBuffTick").SetValue(player, 0);

            // one tick well past the 0x2710 gate and past the remaining balance
            Method("TickNativeExpBuff").Invoke(player, new object[] { 60_000 });

            Equal(0, GetSeconds(player),
                "the countdown must expire (0x6CCCA9 mov [ebx+0xBD4],0)");
            Equal((byte)2, GetTier(player),
                "the tier byte must be STICKY across expiry -- the tick has no "
                + "writer for obj+0xB86 (only 0x6B0495 and 0x786845 write it)");

            // and it must still persist afterwards
            True(Persist(player), "persist after expiry");
            Equal((byte)2, GetRecord(player)[ConstInt("NativeColorSayTierOffset")],
                "the sticky tier must still reach rec 0xD5 after expiry");
        }

        private static void CheckTierIsNotValidatedOnLoad()
        {
            // 0x6B0495 stores the byte raw. The consumer at 0x6C9448/0x6C9454
            // compares against 1 then 2 and lets EVERYTHING else fall to the
            // 0xFF01 default, so an out-of-range record byte is meaningful, not
            // an error to be clamped away.
            var offset = ConstInt("NativeColorSayTierOffset");
            foreach (var raw in new byte[] { 3, 0x16, 0x80, 0xFF })
            {
                var player = NewPlayer();
                var record = new byte[RecordSize];
                record[offset] = raw;
                SetRecord(player, record);
                Restore(player);
                Equal(raw, GetTier(player),
                    $"record byte 0x{raw:X2} must load verbatim (no clamp exists "
                    + "at 0x6B0495)");
            }
        }

        private static void CheckGrantGates()
        {
            // 0x786813 cmp byte [ecx+0x178],0 / jne -> fail
            var nonPlayer = NewPlayer();
            SetRace(nonPlayer, 54);      // RC_HEROOBJECT
            True(!Grant(nonPlayer, 23, 1000),
                "a non-zero race must be refused (0x786813)");
            Equal(0, GetSeconds(nonPlayer), "a refused grant must not set the timer");
            Equal((byte)0, GetTier(nonPlayer), "a refused grant must not set the tier");

            // 0x78681E cmp dword [esi+0xBD4],0 / jne -> fail (NON-STACKING)
            var busy = NewPlayer();
            SetSeconds(busy, 86400);
            SetTier(busy, 1);
            True(!Grant(busy, 25, 3000),
                "an active colour-say must refuse a second grant (0x78681E)");
            Equal(86400, GetSeconds(busy),
                "the refused grant must leave the running timer alone");
            Equal((byte)1, GetTier(busy),
                "the refused grant must leave the running tier alone -- native "
                + "returns before reaching 0x786845");

            // the gate is on the TIMER, not the tier: an expired buff with a
            // sticky non-zero tier must still accept a new grant.
            var expired = NewPlayer();
            SetSeconds(expired, 0);
            SetTier(expired, 3);
            True(Grant(expired, 23, 1000),
                "a zero timer with a sticky tier must still accept a grant -- the "
                + "gate reads 0xBD4, not 0xB86");
            Equal((byte)1, GetTier(expired), "the new grant overwrites the old tier");
        }

        private static void CheckGrantArithmetic()
        {
            // shapes 23/24/25 -> tiers 1/2/3 via 0x786843 sub al,0x16
            foreach (var (shape, tier) in new[]
                     { ((byte)23, (byte)1), ((byte)24, (byte)2), ((byte)25, (byte)3) })
            {
                var player = NewPlayer();
                True(Grant(player, shape, 1000), $"grant shape={shape}");
                Equal(tier, GetTier(player),
                    $"shape {shape} - 0x16 = tier {tier} (0x786843)");
                Equal(86400, GetSeconds(player),
                    "DuraMax 1000 / 1000 * 86400 = one day");
            }

            // 0x786835 `div` is UNSIGNED and TRUNCATING: below 1000 -> zero
            // seconds, yet native still sets its success flag at 0x786851 and
            // still writes the tier at 0x786845.
            var short1 = NewPlayer();
            True(Grant(short1, 24, 999),
                "DuraMax 999 still reports success (0x786851 is unconditional on "
                + "this path)");
            Equal(0, GetSeconds(short1), "999/1000 truncates to 0 days (0x786835 div)");
            Equal((byte)2, GetTier(short1),
                "the tier is written even when the duration truncates to zero");

            // truncation, not rounding
            var trunc = NewPlayer();
            True(Grant(trunc, 23, 2999), "grant DuraMax=2999");
            Equal(2 * 86400, GetSeconds(trunc),
                "2999/1000 = 2 (truncate, not round to 3)");

            // 0x786843 is BYTE arithmetic: a shape below 0x16 wraps rather than
            // going negative or clamping.
            var wrap = NewPlayer();
            True(Grant(wrap, 0, 1000), "grant shape=0");
            Equal((byte)0xEA, GetTier(wrap),
                "shape 0 - 0x16 wraps to 0xEA (byte arithmetic at 0x786843, "
                + "no clamp)");
        }

        private static void CheckGrantIsWiredToItemUse()
        {
            // A dormant granter is worthless: assert the item-use dispatch really
            // routes TColorSayProp here, on a non-comment line.
            var path = Path.Combine(RepoRoot(), "GameSvr", "Players",
                "TPlayObject.Operate.cs");
            var lines = File.ReadAllLines(path);
            var caseLine = -1;
            var callLine = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                var code = lines[i].TrimStart();
                if (code.StartsWith("//") || code.StartsWith("*") ||
                    code.StartsWith("/*")) continue;
                if (code.Contains("case \"TColorSayProp\"")) caseLine = i;
                if (code.Contains("GrantNativeColorSay(")) callLine = i;
            }

            True(caseLine >= 0,
                "TryUseItemEffect must have a live `case \"TColorSayProp\"`");
            True(callLine >= 0,
                "TryUseItemEffect must actually call GrantNativeColorSay "
                + "(a commented-out call does not count)");
            True(callLine > caseLine,
                "the GrantNativeColorSay call must sit under its own case label");
        }

        private static void CheckFactoryShapesMatchTierRange()
        {
            // The factory maps StdMode 1 / Shape 23,24,25 to TColorSayProp; the
            // granter's `sub al,0x16` therefore only ever produces 1,2,3 in
            // practice, which is exactly the domain the say-path select covers
            // (1 -> 0xFFF5, 2 -> 0xFFFA, everything else -> 0xFF01). If someone
            // widens the factory mapping the tier domain silently widens too.
            // NativeItemFactory is internal to GameSvr; reach it by reflection
            // rather than widening production visibility for a test.
            var factory = typeof(GameSvr.TPlayObject).Assembly
                .GetType("GameSvr.NativeItemFactory", throwOnError: true);
            var getClassName = factory.GetMethod("GetClassName",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(byte), typeof(byte), typeof(ushort) }, null);
            True(getClassName != null,
                "NativeItemFactory.GetClassName(byte,byte,ushort) must exist");

            for (byte shape = 0; shape < 40; shape++)
            {
                var name = (string)getClassName.Invoke(null,
                    new object[] { (byte)1, shape, (ushort)0 });
                var isColorSay = name == "TColorSayProp";
                var expected = shape is 23 or 24 or 25;
                Equal(expected, isColorSay,
                    $"StdMode 1 / Shape {shape} -> TColorSayProp?");
            }
        }

        // ---------- sibling granter sub_7865B4 (TAntiDecExpProp) ----------

        private static int GetTrueSight(GameSvr.TPlayObject p) =>
            (int)Field("m_nNativeTrueSightSeconds").GetValue(p);

        private static void SetTrueSight(GameSvr.TPlayObject p, int v) =>
            Field("m_nNativeTrueSightSeconds").SetValue(p, v);

        private static bool GrantAntiDec(GameSvr.TPlayObject p, ushort dura) =>
            (bool)Method("GrantNativeAntiDecExp").Invoke(p, new object[] { dura });

        private static void CheckAntiDecExpAccumulatesNotRefuses()
        {
            // THE ASYMMETRY vs ColorSay. sub_7865B4 has NO non-stacking gate:
            // 0x7865D2 is a CAP on the existing balance (`cmp ...,0x7A1200 / jg`)
            // and 0x7865F4 is an `add`, so an active buff is EXTENDED.
            var player = NewPlayer();
            True(GrantAntiDec(player, 1000), "first grant");
            Equal(3600, GetTrueSight(player), "1000/1000 * 0xE10 = one hour");
            True(GrantAntiDec(player, 2000),
                "an ACTIVE anti-decay buff must still accept a grant -- unlike "
                + "ColorSay there is no `cmp [..],0 / jne` gate at 0x7865D2");
            Equal(3600 + 7200, GetTrueSight(player),
                "0x7865F4 is `add`, so seconds ACCUMULATE (not overwrite)");

            // `jg` => strictly-greater refuses, so exactly-at-cap still tops up.
            var atCap = NewPlayer();
            SetTrueSight(atCap, ConstInt("NativeExpBuffGrantMaxSeconds"));
            True(GrantAntiDec(atCap, 1000),
                "a balance exactly AT the cap must still be topped up (0x7865DC "
                + "is `jg`, not `jge`)");

            var overCap = NewPlayer();
            SetTrueSight(overCap, ConstInt("NativeExpBuffGrantMaxSeconds") + 1);
            True(!GrantAntiDec(overCap, 1000),
                "one second over the cap must refuse (0x7865DC jg)");
            Equal(ConstInt("NativeExpBuffGrantMaxSeconds") + 1,
                GetTrueSight(overCap), "a refused grant must not add time");

            // race gate at 0x7865C7
            var hero = NewPlayer();
            SetRace(hero, 54);
            True(!GrantAntiDec(hero, 1000), "non-zero race must refuse (0x7865C7)");
            Equal(0, GetTrueSight(hero), "refused grant adds nothing");
        }

        private static void CheckAntiDecExpUsesHoursNotDays()
        {
            // 0x7865EE imul esi,eax,0xE10 -- HOURS. Guard against someone
            // "harmonising" it with the colour-say granter's 0x15180 day.
            var player = NewPlayer();
            True(GrantAntiDec(player, 5000), "grant 5 units");
            Equal(5 * 0x0E10, GetTrueSight(player),
                "anti-decay uses the 0xE10 HOUR (0x7865EE), not the colour-say "
                + "0x15180 day");
            NotEqual(5 * 0x15180, GetTrueSight(player),
                "must NOT use the colour-say day unit");

            // unsigned truncating div at 0x7865EC
            var trunc = NewPlayer();
            True(GrantAntiDec(trunc, 1999), "grant DuraMax=1999");
            Equal(1 * 0x0E10, GetTrueSight(trunc),
                "1999/1000 = 1 (truncate, not round)");
        }

        // ---------- sibling granter sub_786390 (TDoubleExpProp) ----------

        private static GameSvr.GoodItem DoubleExpItem(ushort aniCount,
            ushort duraMax) => new()
        {
            Name = "TEST",
            StdMode = 1,
            Shape = 1,          // factory: StdMode 1 / Shape 1 -> TDoubleExpProp
            AniCount = aniCount,
            DuraMax = duraMax,
        };

        private static bool UseDoubleExp(GameSvr.TPlayObject p,
            GameSvr.GoodItem item) =>
            (bool)Method("UseNativeDoubleExpProp").Invoke(p, new object[] { item });

        private static int GetBuffSeconds(GameSvr.TPlayObject p) =>
            (int)Field("m_nNativeExpBuffSeconds").GetValue(p);

        private static int GetBuffMultiplier(GameSvr.TPlayObject p) =>
            (int)Field("m_nNativeExpBuffMultiplier").GetValue(p);

        private static string LastSysMsg(GameSvr.TPlayObject p)
        {
            var list = (System.Collections.IEnumerable)
                typeof(GameSvr.TBaseObject)
                    .GetField("m_MsgList",
                        BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.Instance)!
                    .GetValue(p)!;
            object last = null;
            foreach (var m in list) last = m;
            if (last == null) return null;
            return (string)last.GetType()
                .GetField("Buff",
                    BindingFlags.Public | BindingFlags.NonPublic
                                        | BindingFlags.Instance)!
                .GetValue(last);
        }

        private static void CheckDoubleExpMultiplierTruncates()
        {
            // 0x7863D2 `movzx esi,byte [eax+0x17]` reads a single BYTE. C#'s
            // GoodItem.AniCount is a deliberately-widened ushort, so the faithful
            // reduction is TRUNCATION (GoodItem.cs:82's own wire convention), not
            // clamping: AniCount 0x102 is byte 0x02 to native, a legal 2x.
            var trunc = NewPlayer();
            True(UseDoubleExp(trunc, DoubleExpItem(0x102, 1000)),
                "AniCount 0x102 must grant");
            Equal(2, GetBuffMultiplier(trunc),
                "AniCount 0x102 truncates to byte 0x02 = 2x, it must NOT clamp "
                + "to 0x40 or fall back through the >0x40 branch");

            // 0x140 truncates to byte 0x40, still inside the 2..0x40 window
            var atMax = NewPlayer();
            True(UseDoubleExp(atMax, DoubleExpItem(0x140, 1000)),
                "AniCount 0x140 must grant");
            Equal(0x40, GetBuffMultiplier(atMax),
                "AniCount 0x140 truncates to byte 0x40 (in range), not clamped "
                + "from 320");

            // and 0x41 really is out of range -> the 0x7863DB fallback to 2
            var over = NewPlayer();
            True(UseDoubleExp(over, DoubleExpItem(0x41, 1000)),
                "AniCount 0x41 must grant");
            Equal(2, GetBuffMultiplier(over),
                "byte 0x41 exceeds the 0x40 grant bound so 0x7863DB falls back "
                + "to the 2x minimum");

            // hours, truncating (0x78648E div 1000 / 0x786493 imul 0xE10)
            var hours = NewPlayer();
            True(UseDoubleExp(hours, DoubleExpItem(3, 2999)), "grant DuraMax=2999");
            Equal(2 * 0x0E10, GetBuffSeconds(hours),
                "2999/1000 = 2 hours (truncate) * 0xE10");
        }

        private static void CheckDoubleExpMessagesSubstitute()
        {
            // The native literals use Delphi %d/%s, which string.Format silently
            // ignores -- a message still carrying a literal '%' never reached the
            // client in native.
            var ok = NewPlayer();
            True(UseDoubleExp(ok, DoubleExpItem(3, 5000)), "grant 5h 3x");
            var granted = LastSysMsg(ok);
            True(granted != null, "the success path must send a message");
            True(granted != null && !granted.Contains('%'),
                "the success message must have its %d placeholders substituted "
                + "(0x786598 is a Delphi Format template, so string.Format is a "
                + "no-op on it)");
            True(granted != null && granted.Contains("5") && granted.Contains("3"),
                "the success message formats hours then the NEW multiplier "
                + "(0x7864C9 / 0x7864D3)");

            // conflict path: active buff with a DIFFERENT multiplier
            var conflict = NewPlayer();
            True(UseDoubleExp(conflict, DoubleExpItem(3, 1000)), "prime 3x");
            True(!UseDoubleExp(conflict, DoubleExpItem(4, 1000)),
                "a different multiplier while active must refuse (0x7863EE)");
            var refusal = LastSysMsg(conflict);
            True(refusal != null, "the conflict path must send a message");
            True(refusal != null && !refusal.Contains('%'),
                "the conflict message must have its %d/%s substituted");
            True(refusal != null && refusal.Contains("3"),
                "the conflict message formats the ACTIVE multiplier (0x7863FA "
                + "reads obj+0xBBC), not the attempted one");
            True(refusal != null && refusal.Contains("TEST"),
                "the conflict message formats the item name (%s)");

            // over-cap path sends NOTHING (0x78647E jg -> False, no vmt+0xD4)
            var capped = NewPlayer();
            Field("m_nNativeExpBuffSeconds").SetValue(capped,
                ConstInt("NativeExpBuffGrantMaxSeconds") + 1);
            Field("m_nNativeExpBuffMultiplier").SetValue(capped, 2);
            var before = LastSysMsg(capped);
            True(!UseDoubleExp(capped, DoubleExpItem(2, 1000)),
                "over-cap must refuse");
            Equal(before, LastSysMsg(capped),
                "the over-cap refusal is SILENT -- 0x78647E jumps straight to the "
                + "False return with no message call");
        }

        private static void CheckSiblingGrantersAreWired()
        {
            // All three item classes the factory can produce must reach a live
            // granter; a mapped class with no `case` is a silent no-op on use.
            var path = Path.Combine(RepoRoot(), "GameSvr", "Players",
                "TPlayObject.Operate.cs");
            var lines = File.ReadAllLines(path);
            foreach (var (label, call) in new[]
                     {
                         ("TDoubleExpProp", "UseNativeDoubleExpProp("),
                         ("TAntiDecExpProp", "GrantNativeAntiDecExp("),
                         ("TColorSayProp", "GrantNativeColorSay("),
                     })
            {
                var caseLine = -1;
                var callAfterCase = -1;
                for (var i = 0; i < lines.Length; i++)
                {
                    var code = lines[i].TrimStart();
                    if (code.StartsWith("//") || code.StartsWith("*") ||
                        code.StartsWith("/*")) continue;
                    if (code.Contains("case \"" + label + "\"")) caseLine = i;
                    // Only a call that follows this class's own case label proves
                    // the class is wired. Counting a call anywhere in the file
                    // lets `case X: return false;` pass as long as the wrapper's
                    // own definition is still present -- which is exactly the
                    // hole the 'doubleexp wiring removed' mutation walked through.
                    if (caseLine >= 0 && callAfterCase < 0 && code.Contains(call))
                        callAfterCase = i;
                }

                True(caseLine >= 0,
                    $"TryUseItemEffect must have a live case for {label}");
                True(callAfterCase > caseLine,
                    $"{label} must reach a live granter call ({call}) placed "
                    + "under its own case label -- neither a commented-out call "
                    + "nor the wrapper's own definition elsewhere in the file "
                    + "counts");
                // and the case body must not have been short-circuited
                if (caseLine >= 0 && callAfterCase > caseLine)
                {
                    var between = string.Join("\n",
                        lines.Skip(caseLine + 1)
                            .Take(callAfterCase - caseLine - 1)
                            .Select(l => l.TrimStart())
                            .Where(l => !l.StartsWith("//")));
                    True(!between.Contains("return "),
                        $"nothing may return ahead of {call} inside the "
                        + $"{label} case");
                }
            }
        }

        // ---------- the 网吧 refusal, 0x786443..0x78646F ----------

        private static void SetSuffix(GameSvr.TPlayObject p, byte[] suffix) =>
            typeof(GameSvr.TPlayObject)
                .GetField("m_NativeDbSessionSuffix",
                    BindingFlags.Public | BindingFlags.NonPublic
                                        | BindingFlags.Instance)!
                .SetValue(p, suffix);

        private static byte[] CafeSuffix(bool on)
        {
            var suffix = new byte[GameSvr.NativeHumanDbCodec.SessionSuffixSize];
            if (on) suffix[0x56] = 0x10;
            return suffix;
        }

        private static void SetNetCafeActivity(bool on)
        {
            // The store owns a 5-byte set; drive it through the same IsBitSet
            // surface production uses rather than reaching past it.
            var store = GameSvr.M2Share.ServerSwitches;
            var field = store.GetType().GetField("_switches",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var bytes = (byte[])field!.GetValue(store)!;
            if (on) bytes[3] |= 0x08;
            else bytes[3] &= unchecked((byte)~0x08);
        }

        private static void CheckNetCafeElementIsDerivedNotGuessed()
        {
            // Assert the DERIVATION, not the literal: `test byte [eax+3],8` is
            // element 27 of a `set of 0..39` only because 27 div 8 == 3 and
            // 1 shl (27 mod 8) == 8. A porter who hardcodes (3, 0x08) against a
            // different element number would pass a literal-only check.
            var element = ConstInt("NativeSwitchNetCafeActivityElement");
            Equal(27, element, "网吧活动 is ServerSwitch element 27");

            var byteOffset = (int)PlayObjectType
                .GetMethod("NativeSwitchByteOffset",
                    BindingFlags.NonPublic | BindingFlags.Public
                                           | BindingFlags.Static)!
                .Invoke(null, new object[] { element })!;
            var mask = (byte)PlayObjectType
                .GetMethod("NativeSwitchMask",
                    BindingFlags.NonPublic | BindingFlags.Public
                                           | BindingFlags.Static)!
                .Invoke(null, new object[] { element })!;

            Equal(3, byteOffset,
                "element 27 div 8 == byte 3, matching `test byte [eax+3]` at "
                + "0x78644D");
            Equal((byte)0x08, mask,
                "1 shl (27 mod 8) == 8, matching the mask at 0x78644D");
            Equal(5, GameSvr.NativeServerSwitchStore.SwitchByteCount,
                "the switch set is 5 bytes (`set of 0..39`, cmp dl,0x27 at "
                + "0x62E91B)");
            True(element / 8 < GameSvr.NativeServerSwitchStore
                    .SwitchByteCount,
                "element 27 must fall inside the 5-byte set");

            // the source byte in the load payload
            Equal(0x56, ConstInt("NativeNetCafeSuffixOffset"),
                "0x6B0A2C `test byte [ebx+0x56],0x10` with ebx = raw+0xEF00");
            // Native's `raw` (0x6B0982 lea ebx,[eax+0xEF00], eax = [ebp-8]) is the
            // 0xF0A8-byte HumanInfo base, whereas the C# constants are absolute
            // within the wire payload and so carry the 0x48 message header. Assert
            // the offset RELATIVE to HumanInfo, which is what native's base is --
            // comparing the absolute constant would be off by exactly 0x48.
            Equal(0xEF00,
                GameSvr.NativeHumanDbCodec.SessionSuffixOffset
                - GameSvr.NativeHumanDbCodec.HumanInfoOffset,
                "raw+0xEF00 IS the session-suffix base (8 + 0xEEF8), so the cafe "
                + "byte is suffix[0x56], not a record byte");
            Equal(0xEF00,
                GameSvr.NativeHumanDbCodec.HumanInfoPrefixSize
                + NativeHumanDataCodecRecordSize,
                "0x08 + 0xEEF8 == 0xEF00 -- the derivation, not the literal");
        }

        private static void CheckNetCafeFlagRequiresBothHalves()
        {
            // 0x6EB28C: IsNetCafeUser := (27 in switches) and (raw <> 0).
            // The global switch GATES the property -- the raw byte is meaningless
            // while 网吧活动 is off.
            var prop = PlayObjectType.GetProperty("m_boNativeIsNetCafeUser",
                BindingFlags.NonPublic | BindingFlags.Public
                                       | BindingFlags.Instance);
            True(prop != null, "m_boNativeIsNetCafeUser must exist");

            foreach (var (switchOn, rawOn, expected) in new[]
                     {
                         (false, false, false),
                         (false, true, false),   // switch off wins (0x6EB296 je)
                         (true, false, false),   // raw off wins  (0x6EB29F jne)
                         (true, true, true),
                     })
            {
                SetNetCafeActivity(switchOn);
                var player = NewPlayer();
                SetSuffix(player, CafeSuffix(rawOn));
                Equal(expected, (bool)prop!.GetValue(player)!,
                    $"IsNetCafeUser(switch={switchOn}, raw={rawOn}) -- 0x6EB28C "
                    + "is AND, not OR");
            }

            SetNetCafeActivity(false);
        }

        private static void CheckNetCafeFlagIsReadOnlyAndSuffixDerived()
        {
            // Native's whole write surface for +0xB74 is the ctor zero and the
            // login apply (0x6AD921 / 0x6B0A36); RTTI says `set = none`. Deriving
            // it on read means there is no settable field to drift, and no DTO
            // member that could be zeroed on login.
            var raw = PlayObjectType.GetProperty("m_boNativeIsNetCafeUserRaw",
                BindingFlags.NonPublic | BindingFlags.Public
                                       | BindingFlags.Instance);
            True(raw != null, "m_boNativeIsNetCafeUserRaw must exist");
            True(raw != null && raw.GetSetMethod(true) == null,
                "IsNetCafeUser is read-only in RTTI (`set = none` @ 0x6AD529), so "
                + "the C# side must expose no setter");
            True(!PlayObjectType
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Instance)
                    .Any(f => f.Name.Contains("NetCafe",
                        StringComparison.OrdinalIgnoreCase)),
                "no backing FIELD may exist -- native recomputes the flag from the "
                + "load payload every login and never persists it, so a latched "
                + "field would be a new writer native does not have");

            // absent / short suffix must read false, matching the ctor zero-init
            var bare = NewPlayer();
            Equal(false, (bool)raw!.GetValue(bare)!,
                "a player with no session suffix reads false (ctor zero at "
                + "0x6AD921)");
            SetSuffix(bare, new byte[0x40]);
            Equal(false, (bool)raw.GetValue(bare)!,
                "a suffix too short to contain byte 0x56 reads false rather than "
                + "throwing");

            // mask discipline: bit 3 (mask 8) of the same byte is a DIFFERENT
            // feature (tested at 0x65482F / 0x6B0BB5), so it must not read as cafe
            var wrongBit = NewPlayer();
            var suffix = new byte[GameSvr.NativeHumanDbCodec.SessionSuffixSize];
            suffix[0x56] = 0x08;
            SetSuffix(wrongBit, suffix);
            Equal(false, (bool)raw.GetValue(wrongBit)!,
                "mask 0x08 on the same byte belongs to another feature -- only "
                + "0x10 is the cafe bit (0x6B0A2C)");
        }

        private static void CheckNetCafeRefusalLadderPosition()
        {
            // The refusal sits BETWEEN the conflict test and the cap test.
            SetNetCafeActivity(true);
            try
            {
                // fires for multiplier == 2
                var cafe = NewPlayer();
                SetSuffix(cafe, CafeSuffix(true));
                True(!UseDoubleExp(cafe, DoubleExpItem(2, 1000)),
                    "a cafe player must be refused the 2x scroll (0x786453)");
                Equal(0, GetBuffSeconds(cafe),
                    "the refusal grants nothing (0x78646F jumps past the add)");
                var msg = LastSysMsg(cafe);
                True(msg != null && msg.Contains("网吧"),
                    "the refusal sends 0x786564 (网吧 wording)");

                // does NOT fire for multiplier != 2 (0x786446 jne)
                var other = NewPlayer();
                SetSuffix(other, CafeSuffix(true));
                True(UseDoubleExp(other, DoubleExpItem(3, 1000)),
                    "0x786443 `cmp esi,2` means only the 2x scroll is refused; a "
                    + "3x scroll must still grant");
                Equal(0x0E10, GetBuffSeconds(other), "the 3x grant went through");

                // R3 precedes R4: a live 3x buff plus a 2x cafe scroll must give
                // the CONFLICT message, because 0x7863E5 comes before 0x786443
                var conflictFirst = NewPlayer();
                SetSuffix(conflictFirst, CafeSuffix(true));
                True(UseDoubleExp(conflictFirst, DoubleExpItem(3, 1000)),
                    "prime a 3x buff");
                True(!UseDoubleExp(conflictFirst, DoubleExpItem(2, 1000)),
                    "then a 2x cafe scroll must refuse");
                var first = LastSysMsg(conflictFirst);
                True(first != null && !first.Contains("网吧"),
                    "the CONFLICT message (0x78653C) wins over the cafe refusal -- "
                    + "0x7863E5 precedes 0x786443 in the ladder");

                // and the cafe gate precedes the cap test: at-cap + cafe must
                // report the cafe refusal, not the silent over-cap path
                var capped = NewPlayer();
                SetSuffix(capped, CafeSuffix(true));
                Field("m_nNativeExpBuffSeconds").SetValue(capped,
                    ConstInt("NativeExpBuffGrantMaxSeconds") + 1);
                Field("m_nNativeExpBuffMultiplier").SetValue(capped, 2);
                True(!UseDoubleExp(capped, DoubleExpItem(2, 1000)),
                    "over-cap cafe player refuses");
                var capMsg = LastSysMsg(capped);
                True(capMsg != null && capMsg.Contains("网吧"),
                    "0x786443 precedes 0x786474, so the cafe refusal (which DOES "
                    + "message) beats the silent over-cap return");

                // switch off -> the whole branch is unreachable, matching the
                // default ServerSwitch.Bin where 网吧活动 is off
                SetNetCafeActivity(false);
                var switchedOff = NewPlayer();
                SetSuffix(switchedOff, CafeSuffix(true));
                True(UseDoubleExp(switchedOff, DoubleExpItem(2, 1000)),
                    "with 网吧活动 off the refusal cannot fire (0x786451 je), even "
                    + "for a flagged player");
            }
            finally
            {
                SetNetCafeActivity(false);
            }
        }

        private static void CheckNoInventedCafeInputs()
        {
            // The cafe flag has exactly ONE input in native: bit 0x10 of byte 0x56
            // of the inbound load payload's tail (0x6B0A2C, ebx = raw+0xEF00 from
            // 0x6B0982). Nothing in the image COMPUTES it -- no IP check, no
            // cafe-IP config list, no SQL, no auth RPC. So the port must TRANSPORT
            // the bit and never derive it; this check stops a future porter from
            // "helpfully" inventing a source.
            var path = Path.Combine(RepoRoot(), "GameSvr", "Players",
                "TPlayObject.NativeTimedExpBuff.cs");
            var text = File.ReadAllText(path);
            foreach (var invented in new[]
                     {
                         "IPAddress", "RemoteIP", "NetCafeList", "CafeIp",
                         "IsCafeIp",
                     })
                True(!text.Contains(invented, StringComparison.OrdinalIgnoreCase),
                    $"the cafe flag must not be derived from {invented} -- native "
                    + "has no IP check anywhere in the chain, the bit arrives "
                    + "pre-computed in the load payload");

            // and it must not be written back: native has zero writers of tail
            // 0xEF56 image-wide, and the save-side counterpart (sub_6521BC) never
            // reads +0xB74, so the bit is dropped at logout and recomputed next
            // login. A C# writer would be a writer native does not have.
            foreach (var writer in new[]
                     {
                         "m_NativeDbSessionSuffix[NativeNetCafeSuffixOffset] =",
                         "m_NativeDbSessionSuffix[0x56] =",
                     })
                True(!text.Contains(writer),
                    "nothing may WRITE the cafe bit back -- it is inbound-only");
        }

                private static string RepoRoot()
        {
            return AuditRepoRoot.Resolve();
        }
    }
}
