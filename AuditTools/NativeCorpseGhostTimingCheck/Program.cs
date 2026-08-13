using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// SPWN-13 消费端守卫：尸体 -> ghost 的到期判据。
//
// 战神 TCreature.Run（0x7663BC，身份由自带异常串 '[Exception]: TCreature.Run - '
// @0x766848 坐实）的死亡分支只读 word[obj+0x38]：
//
//   007665FD  80 78 74 00           cmp   byte [eax+0x74],0        ; m_boDeath
//   00766601  75 71                 jne   0x766674
//   00766674  8B 45 FC              mov   eax,[ebp-4]
//   00766677  8B D6                 mov   edx,esi                  ; esi=GetTickCount @0x76658A
//   00766679  2B 90 30 03 00 00     sub   edx,[eax+0x330]          ; - m_dwDeathTick
//   0076667F  8B 45 FC              mov   eax,[ebp-4]
//   00766682  0F BF 40 38           movsx eax,word [eax+0x38]      ; 尸体存留秒数
//   00766686  69 C0 E8 03 00 00     imul  eax,eax,1000
//   0076668C  3B D0                 cmp   edx,eax
//   0076668E  72 0F                 jb    0x76669F                 ; 无符号！未到期则跳过
//   00766690  C7 45 F8 06 00 00 00  mov   [ebp-8],6
//   00766697  8B 45 FC              mov   eax,[ebp-4]
//   0076669A  E8 C1 19 00 00        call  0x768060                 ; TCreature.MarkDelete
//
// 没有 dwZenTime、没有配置项。本工具三路取证：
//   (A) 上面这段字节仍在镜像里，逐字节相同；
//   (B) 全镜像 `movsx r32, word [reg+0x38]` 只有 0x766682 这一条（消费点唯一）；
//   (C) 镜像里没有 "MakeGhostTime"/"ZenTime" 串（同区段 "Setup" 等 ini 键是明文的，
//       所以这个缺席是有意义的）——战神根本没有这个配置键。
// 再加静态源码断言：Run 的死亡分支只调 NativeCorpseGhostDue，且该谓词按 jb 的
// 无符号语义实现。
namespace NativeCorpseGhostTimingCheck
{
    internal static class Program
    {
        private const string ImagePath =
            @"D:\loym2\staging\_reunpack_work\flat_image.bin";
        private const int ImageBase = 0x400000;

        private static readonly List<string> Failures = new List<string>();
        private static int _checks;

        private static int Main()
        {
            var root = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "../../../../../"));

            CheckNativeImage();
            CheckRunDeathBranch(root);
            CheckPredicate(root);

            if (Failures.Count > 0)
            {
                Console.WriteLine(
                    $"NativeCorpseGhostTimingCheck: FAIL ({Failures.Count}/{_checks})");
                foreach (var f in Failures) Console.WriteLine("  - " + f);
                return 1;
            }

            Console.WriteLine(
                $"NativeCorpseGhostTimingCheck PASS checks={_checks} " +
                "consume=0x766682(word[obj+0x38]x1000) cmp=unsigned(jb@0x76668E) " +
                "sink=0x768060(MarkDelete) no-dwZenTime no-MakeGhostTime-key");
            return 0;
        }

        private static void Assert(bool ok, string message)
        {
            _checks++;
            if (!ok) Failures.Add(message);
        }

        private static void CheckNativeImage()
        {
            if (!File.Exists(ImagePath))
            {
                Assert(false, "flat_image.bin 不在 " + ImagePath + "（无法取证，fail-closed）");
                return;
            }

            var image = File.ReadAllBytes(ImagePath);

            // (A) 死亡门 + 到期判据 + MarkDelete 调用，逐字节。
            AssertBytes(image, 0x7665FD, "80 78 74 00 75 71",
                "0x7665FD cmp byte[Self+0x74],0 / jne 0x766674（m_boDeath 门）");
            AssertBytes(image, 0x766674,
                "8B 45 FC 8B D6 2B 90 30 03 00 00 8B 45 FC 0F BF 40 38 " +
                "69 C0 E8 03 00 00 3B D0 72 0F",
                "0x766674..0x76668F 到期判据（now-m_dwDeathTick vs word[+0x38]*1000，jb）");
            AssertBytes(image, 0x76669A, "E8 C1 19 00 00",
                "0x76669A call 0x768060（TCreature.MarkDelete）");

            // sub_768060 真的置 m_boGhost 并记录 ghost tick。
            AssertBytes(image, 0x7680E9, "80 7B 73 00 75 18 C6 43 73 01",
                "0x7680E9 幂等门 + 0x7680EF mov byte[Self+0x73],1（m_boGhost）");
            AssertBytes(image, 0x7680F8, "89 83 4C 01 00 00",
                "0x7680F8 mov [Self+0x14C],eax（GetTickCount 快照 = m_dwGhostTick）");

            // 构造函数默认 60 秒。
            AssertBytes(image, 0x764E9E, "66 C7 46 38 3C 00",
                "0x764E9E mov word[Self+0x38],0x3C（构造默认 60 秒）");

            // (B) 消费点唯一：全镜像 movsx r32, word [reg+disp8=0x38] 只有一条。
            var movsx = new List<int>();
            for (var off = 0x1000; off < 0x3D0000 - 4; off++)
            {
                if (image[off] != 0x0F || image[off + 1] != 0xBF) continue;
                var modrm = image[off + 2];
                var mod = modrm >> 6;
                var rm = modrm & 7;
                // mod=01 (disp8), rm != 4 (no SIB)，disp8 == 0x38
                if (mod != 1 || rm == 4) continue;
                if (image[off + 3] != 0x38) continue;
                movsx.Add(ImageBase + off);
            }
            Assert(movsx.Count == 1 && movsx[0] == 0x766682,
                "全镜像 `movsx r32,word [reg+0x38]` 应当只有 0x766682 一条，实测 [" +
                string.Join(", ", movsx.ConvertAll(v => "0x" + v.ToString("X6"))) + "]");

            // (C) 战神没有 MakeGhostTime / ZenTime 配置键；"Setup" 作为阳性对照。
            Assert(IndexOfAscii(image, "Setup") >= 0,
                "阳性对照失败：镜像里连 \"Setup\" 都搜不到，说明 ini 键不是明文，(C) 判据不成立");
            Assert(IndexOfAscii(image, "MakeGhostTime") < 0,
                "镜像里出现了 \"MakeGhostTime\"：战神可能确有该配置键，需重新裁决");
            Assert(IndexOfAscii(image, "ZenTime") < 0,
                "镜像里出现了 \"ZenTime\"：dwZenTime 可能确实参与，需重新裁决");
        }

        private static void AssertBytes(byte[] image, int va, string hex, string what)
        {
            var want = ParseHex(hex);
            var off = va - ImageBase;
            var ok = off >= 0 && off + want.Length <= image.Length;
            if (ok)
            {
                for (var i = 0; i < want.Length; i++)
                {
                    if (image[off + i] != want[i]) { ok = false; break; }
                }
            }
            if (!ok && off >= 0 && off + want.Length <= image.Length)
            {
                var got = new StringBuilder();
                for (var i = 0; i < want.Length; i++)
                    got.Append(image[off + i].ToString("X2")).Append(' ');
                what += "；实测 " + got.ToString().Trim();
            }
            Assert(ok, what);
        }

        private static byte[] ParseHex(string hex)
        {
            var parts = hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (var i = 0; i < parts.Length; i++)
                bytes[i] = Convert.ToByte(parts[i], 16);
            return bytes;
        }

        private static int IndexOfAscii(byte[] image, string needle)
        {
            var pat = Encoding.ASCII.GetBytes(needle);
            for (var i = 0; i + pat.Length <= image.Length; i++)
            {
                var hit = true;
                for (var j = 0; j < pat.Length; j++)
                {
                    if (image[i + j] != pat[j]) { hit = false; break; }
                }
                if (hit) return i;
            }
            return -1;
        }

        private static void CheckRunDeathBranch(string root)
        {
            var path = Path.Combine(root, "GameSvr", "Actors", "TBaseObject.Base.cs");
            if (!File.Exists(path))
            {
                Assert(false, "找不到 " + path);
                return;
            }

            var text = File.ReadAllText(path);
            var runStart = text.IndexOf("public virtual void Run()", StringComparison.Ordinal);
            Assert(runStart >= 0, "TBaseObject.Run() 不见了");
            if (runStart < 0) return;

            // Run 的第二个 try 块（sExceptionMsg1 那一段）里就是死亡分支。
            var end = text.IndexOf("sExceptionMsg1);", runStart, StringComparison.Ordinal);
            Assert(end > runStart, "Run() 里定位不到 sExceptionMsg1 的 catch");
            if (end <= runStart) return;

            var body = text.Substring(runStart, end - runStart);
            var code = StripComments(body);

            Assert(code.Contains("NativeCorpseGhostDue("),
                "Run() 的死亡分支必须走 NativeCorpseGhostDue()（0x766682 的判据）");
            Assert(!code.Contains("dwMakeGhostTime"),
                "Run() 里不得再出现 dwMakeGhostTime：战神没有这个配置键（镜像无 \"MakeGhostTime\" 串）");
            Assert(!code.Contains("dwZenTime"),
                "Run() 里不得再出现 dwZenTime：0x766674..0x76668E 不读刷怪间隔");
            Assert(body.Contains("0x766682") && body.Contains("0x768060"),
                "Run() 的死亡分支必须注明原生 EA 0x766682 / 0x768060");
        }

        private static void CheckPredicate(string root)
        {
            var path = Path.Combine(root, "GameSvr", "Actors",
                "TBaseObject.NativeCorpseSeconds.cs");
            if (!File.Exists(path))
            {
                Assert(false, "找不到 " + path);
                return;
            }

            var text = File.ReadAllText(path);
            var m = Regex.Match(text,
                @"public\s+bool\s+NativeCorpseGhostDue\s*\([^)]*\)\s*\{(?<body>[^}]*)\}",
                RegexOptions.Singleline);
            Assert(m.Success, "NativeCorpseGhostDue 方法不见了");
            if (!m.Success) return;

            var body = StripComments(m.Groups["body"].Value);

            // 0x76668E 是 jb（无符号），不是 jl。负的尸体秒数在原生里等于"几乎永不消失"，
            // 有符号比较会把它变成"立刻消失"，方向正好相反。
            Assert(Regex.IsMatch(body, @"\(uint\)\s*\(\s*dwCurrentTick\s*-\s*m_dwDeathTick\s*\)"),
                "左边必须转 uint：0x76668E 是 jb（无符号），不是 jl");
            Assert(Regex.IsMatch(body, @"\(uint\)\s*\(\s*m_wNativeCorpseSeconds\s*\*\s*1000\s*\)"),
                "右边必须转 uint 且乘 1000（0x766686 imul eax,eax,0x3E8）");
            Assert(body.Contains(">="),
                "jb 不成立才 MakeGhost，所以判据是 >=（不是 >）");
            Assert(!Regex.IsMatch(body, @"dwZenTime|dwMakeGhostTime|m_pMonGen|m_boCanReAlive"),
                "谓词里不得掺入 dwZenTime / dwMakeGhostTime / m_pMonGen / m_boCanReAlive");
        }

        /// <summary>去掉 // 行注释、/* */ 块注释和 /// 文档注释，只留可执行代码。</summary>
        private static string StripComments(string source)
        {
            var noBlock = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\r\n]*", " ");
        }
    }
}
