using System;
using System.IO;

namespace BagCapWeightCheck
{
    /// <summary>
    /// Bag capacity / weight / pickup-swallow contracts from the M2 flat image.
    /// Native evidence:
    ///   sub_6D0AE8  Count+(edx&amp;0xFF) &lt;= 48  (setle), VMT+0x244
    ///   sub_73D078  mov dl,1 / call [vmt+0x244] then TList.Add then 0x73CEE4
    ///   sub_73C950  Weight &lt; MaxWeight, dx overwritten
    ///   sub_63FF2E  1034 make-drug calls [vmt+0x248], never 0x73C950
    ///   sub_6B74D8  bag/weight gates before DeleteFromMap; fail SysMsg 0x6B7868
    /// </summary>
    class Program
    {
        static int Main()
        {
            var root = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var failed = 0;

            failed += Check(
                Path.Combine(root, "GameSvr", "Plugins", "BigBag", "BagCapacity.cs"),
                "NativeSlots = Grobal2.MAXBAGITEM",
                "BagCapacity.NativeSlots must be MAXBAGITEM (native 0x6D0AF9 cmp eax,0x30)");
            failed += Check(
                Path.Combine(root, "SystemModule", "Grobal2.cs"),
                "MAXBAGITEM = 48",
                "Grobal2.MAXBAGITEM must be 48");
            failed += Check(
                Path.Combine(root, "GameSvr", "Actors", "TBaseObject.cs"),
                "m_ItemList.Count < BagCapacity.Of(this)",
                "inner AddItemToBag must gate on BagCapacity.Of (native VMT+0x244)");
            failed += Check(
                Path.Combine(root, "GameSvr", "Actors", "TBaseObject.cs"),
                "WeightChanged();",
                "AddItemToBag success path must WeightChanged (native 0x73D0AF call 0x73CEE4)");
            failed += Check(
                Path.Combine(root, "GameSvr", "Players", "TPlayObject.cs"),
                "m_WAbil.Weight < m_WAbil.MaxWeight",
                "IsAddWeightAvailable must be Weight < MaxWeight (0x73C950 setl; dx ignored)");
            failed += Forbid(
                Path.Combine(root, "GameSvr", "Players", "TPlayObject.cs"),
                "Weight + nWeight",
                "IsAddWeightAvailable must not add nWeight");
            failed += Check(
                Path.Combine(root, "GameSvr", "Players", "TPlayObject.cs"),
                "无法再拾取更多物品。",
                "pickup fail must SysMsg native 0x6B7868");
            failed += ForbidInRange(
                Path.Combine(root, "GameSvr", "Players", "TPlayObject.cs"),
                "private bool ClientPickUpItem(MapItem mapItem",
                "// WinExp 现为 sub_6F7A18",
                "Dispose(UserItem);",
                "pickup must not Dispose(UserItem) on bag/weight fail (native 0x6B77BA never frees the ground item)");
            failed += Check(
                Path.Combine(root, "GameSvr", "Npcs", "Merchant.cs"),
                "if (PlayObject.AddItemToBag(UserItem))",
                "1034 ClientMakeDrugItem must add via AddItemToBag");
            failed += ForbidInRange(
                Path.Combine(root, "GameSvr", "Npcs", "Merchant.cs"),
                "public void ClientMakeDrugItem",
                "PlayObject.SendMsg(this, Grobal2.RM_MAKEDRUG_FAIL",
                "IsAddWeightAvailable",
                "1034 must not consult IsAddWeightAvailable (native 0x63FE2C never calls 0x73C950)");
            failed += Check(
                Path.Combine(root, "GameSvr", "Players", "TPlayObject.cs"),
                "i < BagCapacity.NativeSlots",
                "HumData bag encode must clip to NativeSlots=48 (0x6B171B cmp edi,0x30)");
            failed += Check(
                Path.Combine(root, "SystemModule", "Grobal2.cs"),
                "HUMAN_EQUIPPED_ITEM_COUNT = 16",
                "equipment slots must be 16 (0x75EEA9 cmp ebx,0x10)");
            failed += Check(
                Path.Combine(root, "SystemModule", "Packet", "NativeHeroDbFrameCodec.cs"),
                "BagItemCount = 40",
                "hero save bag slots must be 40");

            if (failed == 0)
                Console.WriteLine("PASS: bag capacity / weight / pickup-swallow contracts");
            return failed == 0 ? 0 : 1;
        }

        static int Check(string path, string needle, string why)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("FAIL: missing " + path);
                return 1;
            }
            var text = File.ReadAllText(path);
            if (!text.Contains(needle))
            {
                Console.WriteLine("FAIL: " + why);
                Console.WriteLine("      missing `" + needle + "` in " + Path.GetFileName(path));
                return 1;
            }
            return 0;
        }

        static int Forbid(string path, string needle, string why)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("FAIL: missing " + path);
                return 1;
            }
            var text = File.ReadAllText(path);
            if (text.Contains(needle))
            {
                Console.WriteLine("FAIL: " + why);
                Console.WriteLine("      found `" + needle + "` in " + Path.GetFileName(path));
                return 1;
            }
            return 0;
        }

        static int ForbidInRange(string path, string startMarker, string endMarker,
            string needle, string why)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("FAIL: missing " + path);
                return 1;
            }
            var text = File.ReadAllText(path);
            var start = text.IndexOf(startMarker, StringComparison.Ordinal);
            var end = text.IndexOf(endMarker, StringComparison.Ordinal);
            if (start < 0 || end < 0 || end <= start)
            {
                Console.WriteLine("FAIL: " + why);
                Console.WriteLine("      could not bound ClientMakeDrugItem in " + Path.GetFileName(path));
                return 1;
            }
            var slice = text.Substring(start, end - start);
            if (slice.Contains(needle))
            {
                Console.WriteLine("FAIL: " + why);
                return 1;
            }
            return 0;
        }
    }
}
