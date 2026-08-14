using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// DURA-39/DURA-16: the struck-side equipment durability worker.
    /// <para>
    /// Native chain, re-derived end to end from
    /// <c>staging/_reunpack_work/flat_image.bin</c> (ImageBase 0x400000):
    /// </para>
    /// <code>
    /// sub_73F9FC (StruckDamage) @0x73FB55  mov edx,[ebp-8]      ; nDam = Random(10)+5
    ///                           @0x73FB58  mov eax,ebx          ; self = the defender
    ///                           @0x73FB5A  call sub_73FBE8
    /// sub_73FBE8               @0x73FBEB  mov eax,[eax+0x4C0]  ; the UseItems container
    ///                          @0x73FBF1  call sub_75EBC0      ; edx = nDam preserved
    /// sub_75EBC0               @0x75EBD2  xor esi,esi
    ///                          @0x75EBD7  mov eax,[eax+esi*4+8] ; container[i]
    ///                          @0x75EBE3  call sub_75EA40
    ///                          @0x75EBEB  cmp byte [ebp-0xA],0 ; the notify flag
    ///                          @0x75EBF6  call sub_75F49C      ; SendDuraChange(container,i)
    ///                          @0x75EC03  inc esi; cmp esi,0x10; jne  = the 16-SLOT LOOP
    ///                          @0x75EC12  call sub_75EE78      ; RecalcAbilitys, only if
    ///                                                          ; something was destroyed
    /// </code>
    /// <para>
    /// The whole loop applies to every slot 0..15, so slots 0/2/3/7/8/10/15 do
    /// wear on being struck. Slot 9 and slot 12 are excluded not by index but by
    /// the per-class predicate below.
    /// </para>
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>
        /// The visible durability point. sub_75EA40 compares the point before and
        /// after the wear to decide whether the client needs an update:
        /// <c>@0x75EAAA mov ecx,0x3E8 / cdq / idiv ecx</c> for the old value and
        /// <c>@0x75EAB8 mov ebx,0x3E8 / xor edx,edx / div ebx</c> for the new one.
        /// Both are TRUNCATING integer divisions, not a rounding conversion.
        /// </summary>
        private static int NativeStruckDuraDisplayPoint(int nDura) => nDura / 1000;

        /// <summary>
        /// The per-item "does this wear when struck" gate. sub_75EA40 @0x75EA69
        /// dispatches it virtually: <c>mov edx,[eax]; call dword [edx+0x74]</c>
        /// (TEquipItem virtual slot 29), and returns immediately when it is false.
        /// <para>
        /// Three implementations exist across the whole VMT set, and every class
        /// that can occupy an equipment slot resolves to one of them (only
        /// TEquipItem descendants have a slot 29 at all — TBaseItem's VMT is 20
        /// slots long, so nothing else can legally be equipped):
        /// </para>
        /// <list type="bullet">
        /// <item><c>0x75F6C8</c> — <c>cmp word [eax+0x26],0 / jbe -&gt; 0 / mov al,1</c>,
        /// i.e. <c>Dura &gt; 0</c>. Used by TEquipItem, TClothes, TManClothes,
        /// TWomanClothes, TLWeapon, TRWeapon, TBrokenWeapon, TSpade, THelmet,
        /// THeadMask, TNecklace, TRing, TArmRing, TBelt, TBoots, TMaPai, ...</item>
        /// <item><c>0x763344</c> — <c>xor eax,eax / ret</c>, i.e. ALWAYS FALSE.
        /// Used by the TCharm family (TCharm, THPCharm, TMPCharm, THPMPCharm,
        /// TCryCharm, TMarkStoneCharm) — the U_CHARM slot.</item>
        /// <item><c>0x762C18</c> — <c>xor eax,eax / ret</c>, i.e. ALWAYS FALSE.
        /// Used by the TEquipBujuk family (TBujuk, TDragonHeart,
        /// TSuperDragonHeart, TPoisons, TVessel, TUnionItem) — the U_BUJUK slot.</item>
        /// </list>
        /// <para>
        /// Only the <c>Dura &gt; 0</c> arm is expressible here: C# carries no item
        /// class, so the two always-false arms cannot be reproduced without the
        /// StdMode/Shape to item-class factory (native <c>0x74CE00-0x74D1A0</c>),
        /// which is not ported. That remaining gap is reported as DIVERGENT rather
        /// than guessed at — see docs/dura_writers_census_20260814.md.
        /// </para>
        /// </summary>
        private static bool NativeStruckWearsOnHit(TUserItem item)
        {
            if (item.Dura <= 0)
            {
                return false;
            }
            // DURA-39: sub_75EA40 @0x75EA69 `call [item_vmt+0x74]`. The TCharm family
            // (0x763344) and the TEquipBujuk family (0x762C18) both `xor eax,eax/ret`
            // = never wear; all other equippable classes use 0x75F6C8 = `Dura > 0`.
            // Item class comes from the native factory (0x74C338) via NativeItemFactory.
            var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
            return NativeItemFactory.GetClassName(stdItem) switch
            {
                "TCharm" or "THPCharm" or "TMPCharm" or "THPMPCharm" or "TCryCharm"
                    or "TMarkStoneCharm" => false,
                "TBujuk" or "TDragonHeart" or "TSuperDragonHeart" or "TPoisons"
                    or "TVessel" or "TUnionItem" => false,
                _ => true
            };
        }
    }
}
