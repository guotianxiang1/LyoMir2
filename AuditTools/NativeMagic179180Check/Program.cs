using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NativeMagic179180Check
{
    class Program
    {
        static int Main()
        {
            var thisFile = GetSourcePath();
            Console.WriteLine($"[AUDIT] NativeMagic179180Check from {thisFile}");

            var flat = @"D:\loym2\staging\_reunpack_work\flat_image.bin";
            if (!File.Exists(flat))
            {
                Console.WriteLine($"ERROR: binary not found at {flat}");
                return 1;
            }

            var data = File.ReadAllBytes(flat);
            const uint BASE = 0x400000;
            int passed = 0, total = 0;

            // Assertion 1: DoSpell boSpellFail initialization at 0x6ED63C
            // mov byte ptr [ebp-5], 0
            total++;
            var off1 = 0x6ED63C - BASE;
            if (data[off1] == 0xC6 && data[off1+1] == 0x45 && data[off1+2] == 0xFB && data[off1+3] == 0x00)
            {
                Console.WriteLine($"PASS: boSpellFail init @0x6ED63C");
                passed++;
            }
            else
            {
                Console.WriteLine($"FAIL: boSpellFail init @0x6ED63C");
                return 1;
            }

            // Assertion 2: First dispatch tier at 0x6ED79A - cmp eax, 0x97 (151)
            total++;
            var off2 = 0x6ED79A - BASE;
            if (data[off2] == 0x3D && data[off2+1] == 0x97 && data[off2+2] == 0x00 && data[off2+3] == 0x00 && data[off2+4] == 0x00)
            {
                Console.WriteLine($"PASS: dispatch tier cmp eax, 0x97 @0x6ED79A");
                passed++;
            }
            else
            {
                Console.WriteLine($"FAIL: dispatch tier cmp eax, 0x97 @0x6ED79A");
                return 1;
            }

            // Assertion 3: Second dispatch tier at 0x6ED884 - cmp eax, 0xE7 (231)
            total++;
            var off3 = 0x6ED884 - BASE;
            if (data[off3] == 0x3D && data[off3+1] == 0xE7 && data[off3+2] == 0x00 && data[off3+3] == 0x00 && data[off3+4] == 0x00)
            {
                Console.WriteLine($"PASS: dispatch tier cmp eax, 0xE7 @0x6ED884");
                passed++;
            }
            else
            {
                Console.WriteLine($"FAIL: dispatch tier cmp eax, 0xE7 @0x6ED884");
                return 1;
            }

            // Assertion 4: Third dispatch tier at 0x6ED891 - cmp eax, 0xA7 (167)
            total++;
            var off4 = 0x6ED891 - BASE;
            if (data[off4] == 0x3D && data[off4+1] == 0xA7 && data[off4+2] == 0x00 && data[off4+3] == 0x00 && data[off4+4] == 0x00)
            {
                Console.WriteLine($"PASS: dispatch tier cmp eax, 0xA7 @0x6ED891");
                passed++;
            }
            else
            {
                Console.WriteLine($"FAIL: dispatch tier cmp eax, 0xA7 @0x6ED891");
                return 1;
            }

            // Assertion 5: Fourth dispatch tier at 0x6ED8BC - sub eax, 0xBF (191)
            total++;
            var off5 = 0x6ED8BC - BASE;
            if (data[off5] == 0x2D && data[off5+1] == 0xBF && data[off5+2] == 0x00 && data[off5+3] == 0x00 && data[off5+4] == 0x00)
            {
                Console.WriteLine($"PASS: dispatch tier sub eax, 0xBF @0x6ED8BC");
                passed++;
            }
            else
            {
                Console.WriteLine($"FAIL: dispatch tier sub eax, 0xBF @0x6ED8BC");
                return 1;
            }

            // Assertion 6: Fifth dispatch tier at 0x6ED8C7 - sub eax, 0x16 (22)
            total++;
            var off6 = 0x6ED8C7 - BASE;
            if (data[off6] == 0x83 && data[off6+1] == 0xE8 && data[off6+2] == 0x16)
            {
                Console.WriteLine($"PASS: dispatch tier sub eax, 0x16 @0x6ED8C7");
                passed++;
            }
            else
            {
                Console.WriteLine($"FAIL: dispatch tier sub eax, 0x16 @0x6ED8C7");
                return 1;
            }

            // Assertion 7: Default convergence jump at 0x6ED8D0 - jmp 0x6EE04B
            total++;
            var off7 = 0x6ED8D0 - BASE;
            if (data[off7] == 0xE9)
            {
                int jumpOffset = BitConverter.ToInt32(data, (int)off7 + 1);
                uint target = (uint)(0x6ED8D0 + 5 + jumpOffset);
                if (target == 0x6EE04B)
                {
                    Console.WriteLine($"PASS: default convergence jmp 0x6EE04B @0x6ED8D0");
                    passed++;
                }
                else
                {
                    Console.WriteLine($"FAIL: default convergence jmp target is 0x{target:X}, expected 0x6EE04B");
                    return 1;
                }
            }
            else
            {
                Console.WriteLine($"FAIL: default convergence jmp @0x6ED8D0");
                return 1;
            }

            // Assertion 8: Convergence handler boSpellFail check at 0x6EE04B
            // cmp byte ptr [ebp-6], 0
            total++;
            var off8 = 0x6EE04B - BASE;
            if (data[off8] == 0x80 && data[off8+1] == 0x7D && data[off8+2] == 0xFA && data[off8+3] == 0x00)
            {
                Console.WriteLine($"PASS: convergence handler boSpellFail check @0x6EE04B");
                passed++;
            }
            else
            {
                Console.WriteLine($"FAIL: convergence handler boSpellFail check @0x6EE04B");
                return 1;
            }

            // Assertion 9: Success path return TRUE at 0x6EE0C3
            // mov byte ptr [ebp-5], 1
            total++;
            var off9 = 0x6EE0C3 - BASE;
            if (data[off9] == 0xC6 && data[off9+1] == 0x45 && data[off9+2] == 0xFB && data[off9+3] == 0x01)
            {
                Console.WriteLine($"PASS: success path return TRUE @0x6EE0C3");
                passed++;
            }
            else
            {
                Console.WriteLine($"FAIL: success path return TRUE @0x6EE0C3");
                return 1;
            }

            Console.WriteLine($"\n[RESULT] {passed}/{total} assertions passed");
            return passed == total ? 0 : 1;
        }

        static string GetSourcePath([CallerFilePath] string path = "") => path;
    }
}
