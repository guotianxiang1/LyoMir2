// Delphi `is` 语义审计（物品类型门）
//
// 战神的类型门是 `is` 运算符，不是类名相等比较：
//   0x6866F9  mov edx,[0x75E7C4]   ; TDragonHeart 的类引用 cell
//   0x6866FF  call 0x404828        ; sub_404828 = Delphi `is`
// 而 `is` 在 sub_4048C8 里沿父链走：
//   0x4048CC  cmp eax,edx / je -> true
//   0x4048D0  mov eax,[eax-0x24]   ; 父类"引用 cell"的地址
//   0x4048CA  mov eax,[eax]        ; 再解一次才是父 VMT
// 即 vmt-0x24 存的是**指向**父类引用的指针 -> 取父必须**二次解引用**。
// 所以 `is TFoo` 接受 TFoo 的所有后代；C# 写成 GetClassName(x)=="TFoo"
// 只有在 TFoo 无后代时才等价。
//
// 镜像实测（593 个 SelfPtr 自洽 VMT）：这类门用到的类里**只有** TDragonHeart
// 有后代 —— TSuperDragonHeart(VMT 0x75E90C, parent 0x75E810)，且工厂真能造出来
// (分支 0x74D118 载入 cell 0x75E8C0 => StdMode 25 / Shape 10)。
// TMarkStoneCharm / TFixedCoordStone / TVessel / TUnionItem 均无后代，
// 它们的相等比较本来就与原版一致，故**不动**。
//
// 本审计守：
//   1. `is` 语义本身（自身/后代为真、父类/无关类为假、null 安全）。
//   2. 工厂确实把 (25,9)->TDragonHeart、(25,10)->TSuperDragonHeart，
//      即该后代可达、门确实会被触发（否则修复是空的）。
//   3. **回归门**：龙之心那处不得退回相等比较；四个等价门不得被"顺手放宽"
//      （放宽=引入非原版行为，比窄更糟）。

using System.Reflection;
using System.Text;

namespace NativeClassIsOperatorCheck
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
                CheckIsSemantics();
                CheckEquipHierarchy();
                CheckFactoryProducesBothDragonHearts();
                CheckChildlessClassesStayExact();
                CheckDragonHeartGateUsesIsOperator();
                CheckEquivalentGatesNotLoosened();
            }
            catch (Exception ex)
            {
                Failures.Add("unexpected exception: " + ex);
            }

            if (Failures.Count == 0)
            {
                Console.WriteLine(
                    $"AUDIT_PASS NativeClassIsOperatorCheck {_assertions} assertions");
                return 0;
            }

            Console.WriteLine("AUDIT_FAIL NativeClassIsOperatorCheck");
            foreach (var f in Failures) Console.WriteLine("  - " + f);
            return 1;
        }

        // ---------- reflection plumbing ----------

        private static Type FactoryType => typeof(GameSvr.TPlayObject).Assembly
            .GetType("GameSvr.NativeItemFactory", throwOnError: true);

        private static bool IsA(string cls, string base_)
        {
            var m = FactoryType.GetMethod("IsClassOrDescendantOf",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(string) }, null);
            if (m == null)
                throw new MissingMethodException("IsClassOrDescendantOf(string,string)");
            return (bool)m.Invoke(null, new object[] { cls, base_ });
        }

        private static string ClassName(byte stdMode, byte shape, ushort duraMax)
        {
            var m = FactoryType.GetMethod("GetClassName",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(byte), typeof(byte), typeof(ushort) }, null);
            return (string)m.Invoke(null, new object[] { stdMode, shape, duraMax });
        }

        // ---------- checks ----------

        private static void CheckIsSemantics()
        {
            // reflexive: `x is TFoo` when x IS TFoo
            True(IsA("TDragonHeart", "TDragonHeart"),
                "a class must satisfy `is` against itself");
            // descendant accepted -- the whole point (sub_4048C8 chain walk)
            True(IsA("TSuperDragonHeart", "TDragonHeart"),
                "TSuperDragonHeart must satisfy `is TDragonHeart` (parent 0x75E810)");
            // NOT symmetric: a parent is not an instance of its child
            True(!IsA("TDragonHeart", "TSuperDragonHeart"),
                "`is` must not be symmetric: parent is not a descendant of child");
            // unrelated classes
            True(!IsA("TVessel", "TDragonHeart"), "unrelated class must not match");
            True(!IsA("TUnionItem", "TMarkStoneCharm"), "unrelated class must not match");
            // null safety (native's sub_404828 tests the instance for nil first:
            // 0x40482E test ebx,ebx / je -> false)
            True(!IsA(null, "TDragonHeart"), "null class name must be false, not throw");
            True(!IsA("TDragonHeart", null), "null base name must be false, not throw");
        }

        private static void CheckFactoryProducesBothDragonHearts()
        {
            // If the factory could not produce the descendant, the fix would be
            // vacuous. Native: branch 0x74D102 -> cell 0x75E7C4 (TDragonHeart),
            // branch 0x74D118 -> cell 0x75E8C0 (TSuperDragonHeart).
            Equal("TDragonHeart", ClassName(25, 9, 0),
                "StdMode 25 / Shape 9 must be TDragonHeart");
            Equal("TSuperDragonHeart", ClassName(25, 10, 0),
                "StdMode 25 / Shape 10 must be TSuperDragonHeart");
            True(IsA(ClassName(25, 10, 0), "TDragonHeart"),
                "the factory-produced super amulet must pass the native gate");
        }

        private static void CheckEquipHierarchy()
        {
            var equipClasses = new[]
            {
                "TRWeapon", "TLWeapon", "THeadMask", "TWarDrum", "THelmet",
                "TNecklace", "TRing", "TArmRing", "TBelt", "TBoots", "TMaPai",
                "TCharm", "TClothes", "TEquipBujuk", "TBrokenWeapon", "TSpade",
                "TManClothes", "TWomanClothes", "TTemporaryManClothes",
                "TTemporaryWomanClothes", "TCryCharm", "THPCharm", "TMPCharm",
                "THPMPCharm", "TMarkStoneCharm", "TPoisons", "TBujuk",
                "TUnionItem", "TVessel", "TDragonHeart", "TSuperDragonHeart"
            };
            foreach (var className in equipClasses)
            {
                True(IsA(className, "TEquipItem"),
                    $"{className} must satisfy native `is TEquipItem`");
            }

            True(IsA("TTemporaryManClothes", "TClothes"),
                "multi-level clothes ancestry must reach TClothes");
            True(IsA("TSuperDragonHeart", "TEquipBujuk"),
                "multi-level dragon-heart ancestry must reach TEquipBujuk");
            True(!IsA("TBaseItem", "TEquipItem"),
                "an unrelated base item must not satisfy `is TEquipItem`");
        }

        private static void CheckChildlessClassesStayExact()
        {
            // These four are childless in the image, so `is` collapses to equality.
            // Asserting NO descendant is accepted keeps someone from adding a bogus
            // parent entry that would silently widen a gate beyond native.
            foreach (var cls in new[]
                     { "TMarkStoneCharm", "TFixedCoordStone", "TVessel", "TUnionItem" })
            {
                True(IsA(cls, cls), $"{cls} must satisfy `is` against itself");
                foreach (var other in new[]
                         { "TDragonHeart", "TSuperDragonHeart", "TBaseItem" })
                {
                    True(!IsA(other, cls),
                        $"{other} must NOT satisfy `is {cls}` (childless in the image)");
                }
            }
        }

        // ---------- static source gates ----------

                private static string RepoRoot()
        {
            return AuditRepoRoot.Resolve();
        }

        private static string ReadRepoFile(params string[] parts) =>
            File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

        private static void CheckDragonHeartGateUsesIsOperator()
        {
            var hero = ReadRepoFile("GameSvr", "Actors", "HeroObject.cs");
            True(hero.Contains("IsClassOrDescendantOf(amuletStd, \"TDragonHeart\")"),
                "the hero amulet gate must use the `is` helper");
            True(!hero.Contains("GetClassName(amuletStd) != \"TDragonHeart\""),
                "the hero amulet gate must NOT revert to exact name comparison");
        }

        private static void CheckEquivalentGatesNotLoosened()
        {
            // The other four gates are already equivalent; widening them would
            // invent non-native behaviour. Keep them as exact comparisons.
            var operate = ReadRepoFile("GameSvr", "Players", "TPlayObject.Operate.cs");
            var hero = ReadRepoFile("GameSvr", "Actors", "HeroObject.cs");
            foreach (var cls in new[] { "TVessel", "TUnionItem", "TMarkStoneCharm" })
            {
                True(!operate.Contains($"IsClassOrDescendantOf(amuletStd, \"{cls}\")")
                     && !operate.Contains($"IsClassOrDescendantOf(gemStd, \"{cls}\")"),
                    $"{cls} is childless: its Operate gate must stay an exact comparison");
                True(!hero.Contains($"IsClassOrDescendantOf(amuletStd, \"{cls}\")")
                     && !hero.Contains($"IsClassOrDescendantOf(gemStd, \"{cls}\")"),
                    $"{cls} is childless: its HeroObject gate must stay exact");
            }

            var stone = ReadRepoFile("GameSvr", "Players",
                "TPlayObject.NativeFixedCoordStone.cs");
            True(!stone.Contains("IsClassOrDescendantOf"),
                "TFixedCoordStone is childless: its gate must stay exact");
        }
    }
}
