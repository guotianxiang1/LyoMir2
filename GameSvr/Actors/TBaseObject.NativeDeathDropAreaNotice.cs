using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// Native sub_73E4C4 @0x73E4C4 — equip-death worker tail (caller sub_73FC70 @0x73FFD4).
        /// The caller has already proved that at least one equipment object was present and that
        /// the resolved owner has a non-empty bag. The worker then gates on the map, reads the
        /// owner's live obj+0x610 mode, rolls the exact count-dependent denominator, and queues a
        /// fixed-colour RM_SYSMESSAGE on the dying actor.
        /// </summary>
        internal void TryNativeDeathDropAreaNotice(int dropCount, TPlayObject nativeOwner)
        {
            if (m_PEnvir?.Flag == null)
                return;

            // 0x73E4F3 cmp [map+0x5D],0 / 0x73E4FD cmp [map+0x5E],0
            if (m_PEnvir.Flag.boFightZone || m_PEnvir.Flag.boFight3Zone)
                return;

            // 0x73E507 mov eax,self / 0x73E509 call sub_76858C
            if (InNativeSafeZone12())
                return;

            // 0x73E51B call [vmt+0xB4]: player returns self; hero returns [self+0x68C].
            if (nativeOwner == null)
                return;

            var denominator = 50; // 0x73E516
            string bagName;
            switch (nativeOwner.HeroZodiacBlessGate)
            {
                case 2:
                    bagName = "极品神佑袋"; // 0x73E6CC
                    denominator = dropCount switch
                    {
                        0 => 40,
                        1 or 2 => 25,
                        _ => denominator
                    };
                    break;
                case 3:
                    bagName = "顶级神佑袋"; // 0x73E6E0
                    denominator = dropCount switch
                    {
                        0 => 30,
                        1 or 2 => 20,
                        _ => denominator
                    };
                    break;
                default:
                    return; // 0x73E595 clears bagName; 0x73E59D exits before RNG.
            }

            // 0x73E5A7 Random(bound), 0x73E5AE dec eax: only native result 1 hits.
            if (M2Share.RandomNumber.Random(denominator) != 1)
                return;

            var resultText = dropCount switch
            {
                0 => "您死亡没有爆出装备！",                 // 0x73E6F4
                1 => "您死亡应爆出装备的数量减少了一半！",   // 0x73E734
                2 => "您死亡爆出装备的件数减少了！",         // 0x73E760
                _ => null
            };
            if (resultText == null)
                return; // Other counts still consumed Random(50), then 0x73E66F clears text.

            var text = $"由于您的{bagName}发挥作用，{resultText}"; // Format @0x73E5E6/61F/658
            // 0x73E67D mov cx,0x38FF / call [vmt+0xD4]. Do not route through the
            // configurable SysMsg colour/prefix layer: the original word is fixed.
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0x38, 0, text);
        }
    }
}
