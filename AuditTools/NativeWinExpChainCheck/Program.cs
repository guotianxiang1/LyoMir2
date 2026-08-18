// 杀怪经验发放链审计 —— sub_6F7A18 / sub_6C0318 / sub_6C037C
//
// 战神链（逐调用点）：
//   sub_6C0148 击杀回调
//     0x6C0193 CalcGetExp(sub_6C02A4)  等级差衰减
//     0x6C019C sub_6F79E8              组队分配适配器
//     0x6C01A5 sub_6F7A18  WinExp
//       0x6F7A34 桶[1]:=dwExp / 0x6F7A3B 桶[2]:=Nx加成 / 0x6F7A47 桶[2]+=rate加成
//       0x6F7A4F cmp dwExp,桶[2] / 0x6F7A54 sub / 0x6F7A59 归零
//       0x6F7A63..0x6F7A80 遍历 5 桶，<=0 跳过，逐桶 0x6F7A74 call sub_6C0318
//       0x6C0328 英雄指针 / 0x6C0334 sub_772DA8(=[hero+0x74]) 死亡门
//       0x6C0344 sub_6C037C 比例分割 / 0x6C0349 主人份**覆盖**原额
//       0x6C0358 sub_687714 英雄落账
//       0x6C0365 xor ecx,ecx -> cl=0，**关掉** sub_6C03F8 里 8~12% 红利
//       0x6C036B sub_6C03F8 落账/升级（== GrantNativePlayerExperience）
//
// 本审计守四类事：
//   1. 桶算术（-1 哨兵、两加成相加后减基数、比较方向、只有桶1/桶2 会发）。
//   2. 比例分割（MIN(HL+10,ML)/(ML+HL)、先除后乘、银行家舍入、主人份被覆盖）。
//   3. 互斥性：击杀路 cl=0，所以英雄拿的是比例分割而**不是** 8~12% 随机红利。
//   4. **回归门（静态源码）**：五个非原生缩放不得回到 WinExp；机器人不得再复制
//      一份 WinExp；PAS 的 multitempexprate 不得再 /100 或耦合计时器。

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace NativeWinExpChainCheck
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

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                PrepareRuntimeConfig();
                InitializeRuntime();

                CheckBucketConstants();
                CheckExpBuffBonusSentinel();
                CheckMultiTempExpRateHasNoDivision();
                CheckBucketArithmetic();
                CheckSplitFormula();
                CheckSplitDividesBeforeMultiplying();
                CheckSplitRoundingIsBankers();
                CheckAwardWithoutHeroIsIdentity();
                CheckAwardWithDeadHeroIsIdentity();
                CheckAwardWithLiveHeroSplits();
                CheckKillPathDisablesHeroBonus();
                CheckNativeSwitchExperienceFields();
                CheckNonNativeScalersAreGone();
                CheckRobotDuplicateIsGone();
                CheckPasBindingIsRaw();
            }
            catch (Exception ex)
            {
                Failures.Add("unexpected exception: " + ex);
            }

            if (Failures.Count == 0)
            {
                Console.WriteLine(
                    $"AUDIT_PASS NativeWinExpChainCheck {_assertions} assertions");
                return 0;
            }

            Console.WriteLine("AUDIT_FAIL NativeWinExpChainCheck");
            foreach (var f in Failures) Console.WriteLine("  - " + f);
            return 1;
        }

        // ---------- runtime bootstrap ----------

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

        private static GameSvr.TPlayObject NewPlayer() =>
            (GameSvr.TPlayObject)Activator.CreateInstance(
                PlayObjectType, nonPublic: true);

        private static GameSvr.HeroObject NewHero() =>
            (GameSvr.HeroObject)Activator.CreateInstance(
                typeof(GameSvr.HeroObject), nonPublic: true);

        private static int ConstInt(string name)
        {
            var field = PlayObjectType.GetField(name,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (field == null) throw new MissingFieldException(name);
            return (int)field.GetRawConstantValue();
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null) throw new MissingMethodException(name);
            return method.Invoke(target, args);
        }

        private static int InvokeInt(object target, string name, params object[] args)
            => (int)Invoke(target, name, args);

        private static void SetLevel(GameSvr.TBaseObject actor, int level)
        {
            var abil = actor.m_Abil;
            abil.Level = (ushort)level;
            actor.m_Abil = abil;
        }

        private static int GetExp(GameSvr.TBaseObject actor) => actor.m_Abil.Exp;

        private static void SetExp(GameSvr.TBaseObject actor, int exp, int maxExp)
        {
            var abil = actor.m_Abil;
            abil.Exp = exp;
            abil.MaxExp = maxExp;
            actor.m_Abil = abil;
        }

        // ---------- 1. bucket layout ----------

        private static void CheckBucketConstants()
        {
            // 0x6F7A1B add esp,-0x14 (5 dwords) / 0x6F7A7D cmp bl,5
            Equal(5, ConstInt("NativeWinExpBucketCount"),
                "bucket count is 5 (0x6F7A7D cmp bl,5)");
            // 0x6F7A34 writes [ebp-0x10] = bucket 1 of [ebp-0x14]
            Equal(1, ConstInt("NativeWinExpBaseBucket"),
                "base bucket index is 1 (0x6F7A34 [ebp-0x10])");
            // 0x6F7A40 writes [ebp-0xc] = bucket 2
            Equal(2, ConstInt("NativeWinExpBonusBucket"),
                "bonus bucket index is 2 (0x6F7A40 [ebp-0xc])");
        }

        private static void CheckExpBuffBonusSentinel()
        {
            var player = NewPlayer();
            // 0x6F7A8C or ecx,-1 : the default is -1, NOT 0.
            player.m_nNativeExpBuffSeconds = 0;
            player.m_nNativeExpBuffMultiplier = 3;
            Equal(-1, InvokeInt(player, "NativeExpBuffBonus", 100),
                "inactive Nx buff returns the -1 sentinel (0x6F7A8C or ecx,0xFFFFFFFF)");
            player.m_nNativeExpBuffSeconds = -5;
            Equal(-1, InvokeInt(player, "NativeExpBuffBonus", 100),
                "negative seconds also return -1 (0x6F7A96 jle, signed)");
            // 0x6F7A98 mov ecx,[eax+0xBBC] / 0x6F7A9E imul ecx,edx
            player.m_nNativeExpBuffSeconds = 1;
            Equal(300, InvokeInt(player, "NativeExpBuffBonus", 100),
                "active buff returns N*dwExp (0x6F7A9E imul ecx,edx)");
        }

        private static void CheckMultiTempExpRateHasNoDivision()
        {
            var player = NewPlayer();
            // ctor 0x6ADA18 writes 0 -> 0 is a legal native default.
            Equal(0, player.m_nNativeMultiTempExpRate,
                "MultiTempExpRate default is 0 (0x6ADA18 mov [edi+0xBC0],0)");
            Equal(0, InvokeInt(player, "NativeMultiTempExpRateBonus", 100),
                "rate 0 contributes 0, with no Max(1,..) floor");
            // 0x6F7AA4 mov eax,[eax+0xBC0] / 0x6F7AAA imul edx -- plain multiply.
            player.m_nNativeMultiTempExpRate = 3;
            Equal(300, InvokeInt(player, "NativeMultiTempExpRateBonus", 100),
                "rate multiplies raw, with no /100 (0x6F7AAA imul edx)");
        }

        // ---------- 2. bucket arithmetic ----------

        private static int[] AwardedValues(GameSvr.TPlayObject player)
        {
            // WinExp posts each surviving bucket through GrantNativePlayerExperience,
            // which emits RM_WINEXP with nParam1 = the credited amount.
            return player.m_MsgList
                .Where(e => e.wIdent == SystemModule.Grobal2.RM_WINEXP)
                .Select(e => e.nParam1)
                .ToArray();
        }

        private static GameSvr.TPlayObject PlayerReadyForAward()
        {
            var player = NewPlayer();
            SetLevel(player, 10);
            // MaxExp = 0 keeps GrantNativePlayerExperience out of the level-up loop
            // (its `m_Abil.MaxExp != 0` guard), isolating the award arithmetic.
            SetExp(player, 0, 0);
            return player;
        }

        private static void CheckBucketArithmetic()
        {
            // No buffs: bonus = -1 + 0 = -1, and dwExp(100) > -1 so 0x6F7A59 zeroes
            // the bucket. Exactly ONE award (the base bucket) must be emitted.
            var player = PlayerReadyForAward();
            Invoke(player, "WinExp", 100);
            var awarded = AwardedValues(player);
            Equal(1, awarded.Length,
                "with no buffs only the base bucket is emitted (bonus bucket zeroed at 0x6F7A59)");
            Equal(100, awarded.Length == 1 ? awarded[0] : -1,
                "the base bucket carries dwExp unchanged");

            // Buff on: bonus = 3*100 + 0 = 300; dwExp(100) <= 300 so 0x6F7A54
            // subtracts one dwExp -> 200. Two awards: 100 then 200.
            var buffed = PlayerReadyForAward();
            buffed.m_nNativeExpBuffSeconds = 1;
            buffed.m_nNativeExpBuffMultiplier = 3;
            Invoke(buffed, "WinExp", 100);
            var two = AwardedValues(buffed);
            Equal(2, two.Length, "an active buff emits both buckets");
            Equal(100, two.Length == 2 ? two[0] : -1,
                "bucket 1 (base) is emitted first, at dwExp");
            Equal(200, two.Length == 2 ? two[1] : -1,
                "bucket 2 = N*dwExp - dwExp (0x6F7A54 sub), i.e. (N-1)*dwExp");

            // Rate and buff SUM (0x6F7A4C add), they do not override each other.
            var both = PlayerReadyForAward();
            both.m_nNativeExpBuffSeconds = 1;
            both.m_nNativeExpBuffMultiplier = 3;
            both.m_nNativeMultiTempExpRate = 2;
            Invoke(both, "WinExp", 100);
            var summed = AwardedValues(both);
            Equal(2, summed.Length, "buff + rate still emits two buckets");
            Equal(400, summed.Length == 2 ? summed[1] : -1,
                "bonuses SUM then lose one base: 3*100 + 2*100 - 100 (0x6F7A4C/0x6F7A54)");

            // dwExp 0: bucket[1]=0 fails 0x6F7A65, and bonus = -1 + 0 = -1 with
            // `0 > -1` true, so 0x6F7A59 zeroes bucket[2]. Nothing is emitted.
            var zero = PlayerReadyForAward();
            Invoke(zero, "WinExp", 0);
            Equal(0, AwardedValues(zero).Length,
                "dwExp 0 emits no bucket (0x6F7A65 test eax,eax / jle)");

            // NEGATIVE dwExp is a genuine native quirk, and the faithful result is
            // NOT "nothing". With dwExp = -50 and no buffs: bonus = -1, and
            // 0x6F7A4F `cmp ebx,[ebp-0xc]` compares -50 > -1 = FALSE, so control
            // falls into 0x6F7A54 `sub [ebp-0xc],ebx` = -1 - (-50) = 49. Bucket 2
            // then passes 0x6F7A65 and 49 is actually awarded.
            //
            // This audit asserts the quirk rather than "no award", because asserting
            // the intuitive answer would force a non-native guard into WinExp. It is
            // unreachable from the kill path: sub_6F79E8 @0x6F79EF tests `edx <= 0`
            // and returns 0, and CalcGetExp never returns negative -- so native never
            // feeds WinExp a negative value either.
            var negative = PlayerReadyForAward();
            Invoke(negative, "WinExp", -50);
            var neg = AwardedValues(negative);
            Equal(1, neg.Length,
                "negative dwExp: the -1 sentinel makes bucket 2 positive (0x6F7A54)");
            Equal(49, neg.Length == 1 ? neg[0] : -1,
                "negative dwExp awards -1-dwExp = 49, matching native; do NOT add a "
                + "guard -- sub_6F79E8 @0x6F79EF already makes this unreachable");
        }

        // ---------- 3. the ratio split ----------

        private static int Split(GameSvr.TPlayObject player, int dwExp,
            GameSvr.HeroObject hero, out int heroShare)
        {
            var method = PlayObjectType.GetMethod("NativeSplitExperienceWithHero",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
                throw new MissingMethodException("NativeSplitExperienceWithHero");
            var args = new object[] { dwExp, hero, 0 };
            var master = (int)method.Invoke(player, args);
            heroShare = (int)args[2];
            return master;
        }

        private static void CheckSplitFormula()
        {
            var player = NewPlayer();
            var hero = NewHero();

            // ML=100, HL=50 -> heroTerm = MIN(60,100) = 60, sum = 150
            //   hero   = Round(60/150*300)  = 120
            //   master = Round(100/150*300) = 200
            SetLevel(player, 100);
            SetLevel(hero, 50);
            var master = Split(player, 300, hero, out var heroShare);
            Equal(120, heroShare,
                "hero share = Round(MIN(HL+10,ML)/(ML+HL)*exp) (0x6C03B8..0x6C03C8)");
            Equal(200, master,
                "master share = Round(ML/(ML+HL)*exp) (0x6C03D2..0x6C03EC)");

            // The +10 must be CLAMPED by ML (0x6C03A6 cmp esi,edx / jle / mov esi,edx).
            // ML=20, HL=50 -> heroTerm = MIN(60,20) = 20, sum = 70
            //   hero = Round(20/70*700) = 200 ; unclamped would give Round(60/70*700)=600
            SetLevel(player, 20);
            SetLevel(hero, 50);
            Split(player, 700, hero, out heroShare);
            Equal(200, heroShare,
                "HL+10 is clamped by ML (0x6C03A6); unclamped would give 600");

            // Both shares use the SAME denominator ML+HL, so they need not sum to exp.
            SetLevel(player, 100);
            SetLevel(hero, 50);
            master = Split(player, 300, hero, out heroShare);
            True(master + heroShare != 300,
                "the two shares share a denominator and need NOT sum to exp (320 != 300)");
            Equal(320, master + heroShare,
                "concretely 200 + 120 = 320 for ML=100 HL=50 exp=300");
        }

        private static void CheckSplitDividesBeforeMultiplying()
        {
            // x87 order at 0x6C03B8..0x6C03C8 is: fild esi, fild edi, FDIVP (the
            // ratio), then fild dwExp, FMULP. So the division happens FIRST, on the
            // levels alone, and the resulting ratio is what gets multiplied.
            // Rewriting it as (heroTerm*dwExp)/sumLevels is algebraically equal in
            // exact arithmetic but NOT after double rounding.
            //
            // ML=3, HL=19 -> heroTerm = MIN(29,3) = 3, sum = 22, exp = 55.
            //   divide-first : Round(3/22*55)   = Round(7.4999..) = 7   <-- native
            //   multiply-first: Round(3*55/22)  = Round(7.5)      = 8
            // The 3/22 ratio is not representable in binary, so it lands just under
            // 7.5 and rounds down; the reassociated form hits the exact midpoint.
            var player = NewPlayer();
            var hero = NewHero();
            SetLevel(player, 3);
            SetLevel(hero, 19);
            Split(player, 55, hero, out var heroShare);
            Equal(7, heroShare,
                "the ratio is divided BEFORE multiplying (0x6C03C1 fdivp precedes "
                + "0x6C03C6 fmulp); reassociating to (num*exp)/den would give 8");
        }

        private static void CheckSplitRoundingIsBankers()
        {
            // sub_403574 = Delphi Round = banker's rounding.
            // ML=2, HL=2 -> heroTerm = MIN(12,2) = 2, sum = 4; ratio 0.5.
            // exp=5 -> 0.5*5 = 2.5 -> banker's gives 2, away-from-zero would give 3.
            var player = NewPlayer();
            var hero = NewHero();
            SetLevel(player, 2);
            SetLevel(hero, 2);
            var master = Split(player, 5, hero, out var heroShare);
            Equal(2, heroShare,
                "2.5 rounds to 2, banker's (sub_403574); away-from-zero would give 3");
            Equal(2, master, "master share rounds the same way");
        }

        // ---------- 4. award wiring ----------

        private static void CheckAwardWithoutHeroIsIdentity()
        {
            var player = PlayerReadyForAward();
            player.m_HeroObject = null;                  // 0x6C032E test edi,edi / je
            Invoke(player, "NativeAwardExperience", 100, 1);
            var awarded = AwardedValues(player);
            Equal(1, awarded.Length, "no hero: exactly one award");
            Equal(100, awarded[0], "no hero: the master keeps the full amount");
        }

        private static void CheckAwardWithDeadHeroIsIdentity()
        {
            var player = PlayerReadyForAward();
            var hero = NewHero();
            SetLevel(player, 100);
            SetLevel(hero, 50);
            hero.m_boDeath = true;                       // 0x6C0334 sub_772DA8 -> jne
            player.m_HeroObject = hero;
            Invoke(player, "NativeAwardExperience", 300, 1);
            var awarded = AwardedValues(player);
            Equal(1, awarded.Length, "dead hero: exactly one award");
            Equal(300, awarded[0],
                "dead hero: NO split, the master keeps the full amount (0x6C0334 gate)");
        }

        private static void CheckAwardWithLiveHeroSplits()
        {
            var player = PlayerReadyForAward();
            var hero = NewHero();
            SetLevel(player, 100);
            SetLevel(hero, 50);
            SetExp(hero, 0, 0);
            hero.m_boDeath = false;
            player.m_HeroObject = hero;

            Invoke(player, "NativeAwardExperience", 300, 1);

            var awarded = AwardedValues(player);
            Equal(1, awarded.Length, "live hero: the master still gets one award");
            // 0x6C0349 mov esi,eax -- the master's own credit is REPLACED by its share.
            Equal(200, awarded[0],
                "live hero: the master is REDUCED to its share (0x6C0349 mov esi,eax)");
            True(awarded[0] != 300,
                "live hero: the master must NOT keep 100% -- that was the C# bug");
            // The hero really received its share.
            Equal(120, GetExp(hero),
                "the hero is credited its own share via sub_687714 (0x6C0358)");
        }

        private static void CheckKillPathDisablesHeroBonus()
        {
            // 0x6C0365 xor ecx,ecx -> cl = 0 = shareWithHero:false, so the 8..12%
            // random bonus inside sub_6C03F8 (0x6C0482 cmp [ebp-1],0 / je) is OFF on
            // the kill path. The hero must therefore be credited EXACTLY the ratio
            // share -- one credit, not two.
            var player = PlayerReadyForAward();
            var hero = NewHero();
            SetLevel(player, 100);
            SetLevel(hero, 50);
            SetExp(hero, 0, 0);
            player.m_HeroObject = hero;

            Invoke(player, "NativeAwardExperience", 300, 1);

            var heroCredits = hero.m_MsgList
                .Where(e => e.wIdent == SystemModule.Grobal2.RM_WINEXP)
                .ToArray();
            Equal(1, heroCredits.Length,
                "the hero is credited ONCE: the ratio split and the 8-12% bonus are "
                + "mutually exclusive (0x6C0365 xor ecx,ecx)");
            Equal(120, GetExp(hero),
                "the hero's exp is exactly the ratio share, with no bonus added on top");
        }

        private static void CheckNativeSwitchExperienceFields()
        {
            var normal = PlayerReadyForAward();
            normal.m_nNativeSwitchOffsetD40 = 7;
            normal.GrantNativePlayerExperience(100, false, false, 0);
            Equal(107, normal.m_nNativeSwitchOffsetD40,
                "D38=0 accumulates the actual player award into D40");

            var active = PlayerReadyForAward();
            active.m_wNativeSwitchOffsetD38 = 1;
            active.m_nNativeSwitchOffsetD40 = 7;
            active.GrantNativePlayerExperience(100, false, false, 0);
            Equal(7, active.m_nNativeSwitchOffsetD40,
                "D38!=0 suppresses the D40 accumulator");

            var wrapped = PlayerReadyForAward();
            wrapped.m_nNativeSwitchOffsetD40 = unchecked((int)0xFFFFFFF0u);
            wrapped.GrantNativePlayerExperience(0x30, false, false, 0);
            Equal(0x20, wrapped.m_nNativeSwitchOffsetD40,
                "D40 accumulation uses unchecked 32-bit wraparound");

            var clipped = PlayerReadyForAward();
            SetExp(clipped, unchecked((int)0xFFB00000u), 0);
            clipped.m_nNativeSwitchOffsetD40 = 7;
            clipped.GrantNativePlayerExperience(0x00600000,
                false, false, 0);
            const int accepted = 0x00043480;
            Equal(7 + accepted, clipped.m_nNativeSwitchOffsetD40,
                "D40 adds only the overflow-clipped accepted block");
            Equal(accepted, AwardedValues(clipped).Single(),
                "D40 clipped block matches RM_WINEXP nParam1");

            var split = PlayerReadyForAward();
            var hero = NewHero();
            SetLevel(split, 100);
            SetLevel(hero, 50);
            SetExp(hero, 0, 0);
            split.m_HeroObject = hero;
            Invoke(split, "NativeAwardExperience", 300, 1);
            Equal(200, split.m_nNativeSwitchOffsetD40,
                "D40 adds the master's post-split award, not the original amount");

            var buckets = PlayerReadyForAward();
            buckets.m_nNativeExpBuffSeconds = 1;
            buckets.m_nNativeExpBuffMultiplier = 3;
            Invoke(buckets, "WinExp", 100);
            Equal(300, buckets.m_nNativeSwitchOffsetD40,
                "D40 accumulates every surviving WinExp bucket");

            var direct = PlayerReadyForAward();
            SetExp(direct, 0, int.MaxValue);
            direct.m_nNativeSwitchOffsetD40 = 9;
            Invoke(direct, "GetExp", 25);
            Equal(34, direct.m_nNativeSwitchOffsetD40,
                "non-kill GetExp maintains the mode2 D40 accumulator");
            Invoke(direct, "GetExp", 0);
            Invoke(direct, "GetExp", -5);
            Equal(34, direct.m_nNativeSwitchOffsetD40,
                "non-kill GetExp zero/negative values do not enter D40");
            direct.m_wNativeSwitchOffsetD38 = 1;
            Invoke(direct, "GetExp", 25);
            Equal(34, direct.m_nNativeSwitchOffsetD40,
                "non-kill GetExp respects the D38 suppression gate");

            var baseRun = ReadCode("GameSvr", "Actors",
                "TBaseObject.Base.cs");
            True(Regex.IsMatch(baseRun,
                    @"if\s*\(this is TPlayObject playObject\)\s*" +
                    @"playObject\.m_nNativeSwitchOffsetD3C\s*=\s*0;\s*" +
                    @"for\s*\(var i = m_SlaveList\.Count - 1; i >= 0; i--\)"),
                "TPlayer.Run clears D3C immediately before the native slave sweep");

            var nativeGive = ReadCode("GameSvr", "Players",
                "TPlayObject.NativeGive.cs");
            var heroReward = nativeGive.IndexOf("var hero = m_HeroObject;",
                StringComparison.Ordinal);
            var accumulator = nativeGive.IndexOf(
                "AccumulateNativeSwitchExperience(accepted);",
                StringComparison.Ordinal);
            var levelCap = nativeGive.IndexOf("if (m_Abil.Level >= 999)",
                StringComparison.Ordinal);
            var winExp = nativeGive.IndexOf(
                "SendMsg(this, Grobal2.RM_WINEXP", StringComparison.Ordinal);
            True(heroReward >= 0 && accumulator > heroReward &&
                 levelCap > accumulator && winExp > levelCap,
                "D40 accepted block stays after hero reward and before level-cap/RM_WINEXP");
        }

        // ---------- 5. static regression gates ----------

                private static string RepoRoot()
        {
            return AuditRepoRoot.Resolve();
        }

        /// <summary>
        /// Reads a repo file with <c>//</c> line comments stripped, so a gate scans
        /// CODE and not the comment that documents the gate. (A prior audit went
        /// falsely red exactly this way.)
        /// </summary>
        private static string ReadCode(params string[] parts)
        {
            var path = Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
            var lines = File.ReadAllLines(path)
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal));
            return string.Join("\n", lines);
        }

        private static void CheckNonNativeScalersAreGone()
        {
            var code = ReadCode("GameSvr", "Players", "TPlayObject.NativeWinExp.cs");
            True(code.Contains("private void WinExp(int dwExp)"),
                "WinExp lives in the native port file");

            // Each of these is VALIDATED-ABSENT in the image (zero string hits against
            // working positive controls), so none may appear in the exp path.
            foreach (var forbidden in new[]
                     {
                         "nLimitExpLevel", "nLimitExpValue", "dwKillMonExpMultiple",
                         "m_nKillMonExpMultiple", "m_nKillMonExpRate",
                         "boEXPRATE", "nEXPRATE", "m_boExpItem", "m_rExpItem",
                     })
            {
                True(!code.Contains(forbidden),
                    $"the non-native scaler `{forbidden}` must not re-enter WinExp");
            }
        }

        private static void CheckRobotDuplicateIsGone()
        {
            // Native has ONE WinExp shared by every THumanKind; a second C# copy is
            // how the two drifted apart before.
            var robot = ReadCode("GameSvr", "RobotPlay", "RobotPlayObject.cs");
            True(!robot.Contains("void WinExp("),
                "RobotPlayObject must not redeclare WinExp (native shares sub_6F7A18)");
            foreach (var forbidden in new[]
                     { "nLimitExpLevel", "dwKillMonExpMultiple", "nEXPRATE" })
            {
                True(!robot.Contains(forbidden),
                    $"the robot copy must not reintroduce `{forbidden}`");
            }
        }

        private static void CheckPasBindingIsRaw()
        {
            var bridge = ReadCode("GameSvr", "ScriptSystem", "PasEngine",
                "PasApiBridge.cs");
            var getter = "case \"multitempexprate\": result = " +
                         "PasValue.FromInt(CurrentPlayer.m_nNativeMultiTempExpRate);";
            True(bridge.Contains(getter),
                "PAS multitempexprate getter returns obj+0xBC0 raw (no Max(1,..), no /100)");
            True(bridge.Contains(
                    "CurrentPlayer.m_nNativeMultiTempExpRate = value.AsInt();"),
                "PAS multitempexprate setter writes obj+0xBC0 raw (no *100)");
            // Native's +0xBC0 write never touches +0xBB8; the coupling was invented.
            var setterRegion = bridge.Substring(
                bridge.IndexOf("case \"multitempexprate\":\n", StringComparison.Ordinal));
            var setterBody = setterRegion.Substring(0,
                Math.Min(260, setterRegion.Length));
            True(!setterBody.Contains("m_dwKillMonExpRateTime"),
                "the PAS setter must not clear the buff timer (+0xBC0 and +0xBB8 are "
                + "independent bonuses that SUM at 0x6F7A4C)");
        }
    }
}
