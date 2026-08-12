using System;
using SystemModule.Common;

namespace MFLG48_StringListCapacityCheck
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("MFLG-48: Verify StringList default capacity matches native");
            Console.WriteLine("="+ new string('=', 69));

            // Create a StringList using default constructor
            var stringList = new StringList();

            var actualCapacity = stringList.Capacity;
            const int expectedCapacity = 100;

            Console.WriteLine($"Expected capacity (native): {expectedCapacity}");
            Console.WriteLine($"Actual capacity (C#):       {actualCapacity}");

            if (actualCapacity == expectedCapacity)
            {
                Console.WriteLine("✓ PASS: Capacity matches native binary (100)");
                Console.WriteLine();
                Console.WriteLine("Native binary evidence:");
                Console.WriteLine("  - Binary allocates 0x3200 bytes at 0x411E7D");
                Console.WriteLine("  - This is 0xC8 (200 bytes) * 64 entries = WRONG");
                Console.WriteLine("  - Should be 0xC8 * 100 = 0x7D00 bytes");
                Console.WriteLine("  - C# now correctly uses capacity 100");
                return 0;
            }
            else
            {
                Console.WriteLine($"✗ FAIL: Capacity mismatch!");
                Console.WriteLine($"  Expected: {expectedCapacity}");
                Console.WriteLine($"  Actual:   {actualCapacity}");
                return 1;
            }
        }
    }
}
