using SystemModule;
using GameSvr.Plugins;

namespace GameSvr
{
    public class MagicManager
    {
        private int MagPushArround(TBaseObject PlayObject, int nPushLevel)
        {
            var result = 0;
            for (var i = 0; i < PlayObject.m_VisibleActors.Count; i++)
            {
                var actor = PlayObject.m_VisibleActors[i];
                if (actor?.BaseObject == null) continue;
                var BaseObject = actor.BaseObject;
                if (Math.Abs(PlayObject.m_nCurrX - BaseObject.m_nCurrX) <= 1 && Math.Abs(PlayObject.m_nCurrY - BaseObject.m_nCurrY) <= 1)
                {
                    if (!BaseObject.m_boDeath && BaseObject != PlayObject)
                    {
                        if (PlayObject.m_Abil.Level > BaseObject.m_Abil.Level && !BaseObject.m_boStickMode)
                        {
                            var levelgap = PlayObject.m_Abil.Level - BaseObject.m_Abil.Level;
                            if (M2Share.RandomNumber.Random(20) < 6 + nPushLevel * 3 + levelgap)
                            {
                                if (PlayObject.IsProperTarget(BaseObject))
                                {
                                    var push = 1 + HUtil32._MAX(0, nPushLevel - 1) + M2Share.RandomNumber.Random(2);
                                    var nDir = M2Share.GetNextDirection(PlayObject.m_nCurrX, PlayObject.m_nCurrY, BaseObject.m_nCurrX, BaseObject.m_nCurrY);
                                    BaseObject.CharPushed(nDir, push);
                                    result++;
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }

        private bool MagBigHealing(TBaseObject PlayObject, int nPower, int nX, int nY)
        {
            var result = false;
            IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
            PlayObject.GetMapBaseObjects(PlayObject.m_PEnvir, nX, nY, 1, BaseObjectList);
            for (var i = 0; i < BaseObjectList.Count; i++)
            {
                var BaseObject = BaseObjectList[i];
                if (PlayObject.IsProperFriend(BaseObject))
                {
                    if (BaseObject.m_WAbil.HP < BaseObject.m_WAbil.MaxHP)
                    {
                        BaseObject.SendDelayMsg(PlayObject, Grobal2.RM_MAGHEALING, 0, nPower, 0, 0, "", 800);
                        result = true;
                    }
                    if (PlayObject.m_boAbilSeeHealGauge)
                    {
                        PlayObject.SendMsg(BaseObject, Grobal2.RM_10414, 0, 0, 0, 0, "");
                    }
                }
            }
            return result;
        }

        
        
        
        
        
        public bool IsWarrSkill(int wMagIdx)
        {
            var result = false;
            switch (wMagIdx)
            {
                case SpellsDef.SKILL_ONESWORD:
                case SpellsDef.SKILL_ILKWANG:
                case SpellsDef.SKILL_YEDO:
                case SpellsDef.SKILL_ERGUM:
                case SpellsDef.SKILL_BANWOL:
                case SpellsDef.SKILL_FIRESWORD:
                case SpellsDef.SKILL_MOOTEBO:
                case SpellsDef.SKILL_CROSSMOON:
                case SpellsDef.SKILL_TWINBLADE:
                case SpellsDef.SKILL_58:
                    result = true;
                    break;
            }
            return result;
        }

        // Native has no standalone MPow function on the power path: sub_4C8658
        // draws `wPower + Random(wMaxPower - wPower)` inline @0x4C866E. Kept only
        // as the documented shape of that inline roll; the live power path uses
        // DoSpell_GetPower below, which must NOT pre-roll MPow separately or it
        // would draw RandSeed twice.
        private ushort DoSpell_MPow(TUserMagic UserMagic)
        {
            return (ushort)(UserMagic.MagicInfo.wPower + M2Share.RandomNumber.Random(Math.Max(1, UserMagic.MagicInfo.wMaxPower - UserMagic.MagicInfo.wPower)));
        }

        // Native sub_4C8658 (@0x4C8658-0x4C86B4), reached via the thin wrapper
        // sub_4C8648 which loads `dl = [magic+0x0C]` = the RAW btLevel. This is the
        // canonical mage/taoist power helper (35 native call sites) and its C#
        // equivalent already exists, byte-audited, as
        // TPlayObject.CalculateNativeMagicProducerSkillPower. Native FUSES the
        // MPow roll into the same function (@0x4C866E), so DoSpell_MPow must NOT be
        // called separately at the call sites that route here.
        //
        // Exact native body:
        //   MPow      = wPower[+0x15] + Random(wMaxPower[+0x16] - wPower)   @0x4C866E
        //   defRoll   = Random(btDefMaxPower[+0x19] - btDefPower[+0x18])    @0x4C8683
        //   scaled    = fistp( (btLevel + 1) * MPow / [0x4C86B8] )          @0x4C8696
        //   result    = scaled + defRoll + btDefPower[+0x18]                @0x4C86A9
        // [0x4C86B8] is a 4-byte float32 = 4.0f (raw bytes 00 00 80 40, verified).
        //
        // The divisor is that hardcoded 4.0 — NOT (btTrainLv + 1). Every memory read
        // in the body is [+0x15]/[+0x16]/[+0x18]/[+0x19]; btTrainLv (+0x1A) is never
        // read here at all. In native, btTrainLv is only ever a level CAP (setter
        // sub_4C88EC clamps btLevel to it; sub_4C896C clamps the effective level to
        // it). The two forms coincide only when btTrainLv == 3, which is why the
        // MySQL loader's hardcoded `btTrainLv = 3` (CommonDB.cs:185) masked the bug.
        // See staging/spellpower_formula_exact_20260803.md.
        private ushort DoSpell_GetPower(TUserMagic UserMagic)
        {
            return unchecked((ushort)TPlayObject
                .CalculateNativeMagicProducerSkillPower(UserMagic));
        }

        // Native sub_4C870C — the externally-supplied-power twin: same 4.0 divisor,
        // but the default-power roll comes FIRST and the level factor is the
        // EFFECTIVE level (sub_4C896C). Used by 魔法盾 and 地火 hold-time.
        private ushort DoSpell_GetScaledPower(TUserMagic UserMagic, int nPower)
        {
            return unchecked((ushort)TPlayObject
                .CalculateNativeMagicProducerScaledPower(UserMagic, nPower));
        }

        // Native sub_4C8764: Round(nInt * (2*effLevel + 6) / 12.0f) + defPower term,
        // default-power roll drawn FIRST. No btTrainLv divisor, no nInt/3 split.
        private ushort DoSpell_GetPower13(TUserMagic UserMagic, int nInt)
        {
            return unchecked((ushort)TPlayObject
                .CalculateNativeMagicProducer13Power(UserMagic, nInt));
        }

        private ushort DoSpell_GetRPow(int wInt)
        {
            ushort result;
            if (HUtil32.HiWord(wInt) > HUtil32.LoWord(wInt))
            {
                result = (ushort)(M2Share.RandomNumber.Random(Math.Max(1, HUtil32.HiWord(wInt) - HUtil32.LoWord(wInt) + 1)) + HUtil32.LoWord(wInt));
            }
            else
            {
                result = HUtil32.LoWord(wInt);
            }
            return result;
        }

        public void DoSpell_sub_4934B4(TPlayObject PlayObject)
        {
            if (PlayObject.m_UseItems[Grobal2.U_ARMRINGL] != null && PlayObject.m_UseItems[Grobal2.U_ARMRINGL].Dura < 100)
            {
                PlayObject.m_UseItems[Grobal2.U_ARMRINGL].Dura = 0;
                PlayObject.SendDelItems(PlayObject.m_UseItems[Grobal2.U_ARMRINGL]);
                PlayObject.m_UseItems[Grobal2.U_ARMRINGL].wIndex = 0;
            }
        }

        // Native sub_73CC18 — the post-poison-cast charm cleanup, invoked
        // UNCONDITIONALLY from the wMagicID 6 tail @0x6ED9EB-0x6ED9F0:
        //   73CC2F  test edx,edx                  ; nil charm -> nothing
        //   73CC33  cmp  word [edx+0x26],0x64     ; Dura vs 100
        //   73CC38  jae  0x73CC60                 ; Dura >= 100 -> nothing
        //   73CC40  call [ebx+0x268]              ; SendDelItems-equivalent
        //   73CC4E  mov  ecx,0x73CC8C             ; "持久耗尽" (len@0x73CC88 = 8)
        //   73CC53  mov  dl,9                     ; slot 9
        //   73CC5B  call sub_75F27C               ; remove from the slot
        // sub_75F27C @0x75F2B9-0x75F346, in order: null the slot pointer
        // (`mov [ebx+eax*4+8],0` @0x75F2BB), gate on sub_75F4F4 (which is
        // `mov al,1; ret` @0x75F4F4 — ALWAYS true) then sub_75EE78 + VMT+0x8C
        // (= RecalcAbilitys), call VMT+0x268 (@0x75F2FF), write a game-data log
        // via sub_768BE0 (@0x75F328) whose reason column is that string, then
        // VMT+0x1CC only when sub_75F1D8(slot) passes (slots 0/1/2/3/11 — 9 is
        // NOT one of them), and finally free the object.
        // NOTE: "持久耗尽" is a LOG field passed to sub_768BE0, NOT a SysMsg —
        // no player-visible message is emitted here natively.
        private static void ConsumeSpentPoisonCharm(TPlayObject PlayObject,
            TUserItem charm)
        {
            if (charm == null || charm.Dura >= 100)
                return;

            PlayObject.m_UseItems[Grobal2.U_BUJUK] = null;
            PlayObject.RecalcAbilitys();
            PlayObject.SendDelItems(charm);
            var StdItem = M2Share.UserEngine.GetStdItem(charm.wIndex);
            M2Share.AddGameDataLog("持久耗尽" + "\t" + PlayObject.m_sMapName +
                "\t" + PlayObject.m_nCurrX + "\t" + PlayObject.m_nCurrY +
                "\t" + PlayObject.m_sCharName + "\t" +
                (StdItem == null ? string.Empty : StdItem.Name) + "\t" +
                charm.MakeIndex + "\t" + '1' + "\t" + '0');
        }

        public bool DoSpell(TPlayObject PlayObject, TUserMagic UserMagic, short nTargetX, short nTargetY, TBaseObject TargeTBaseObject)
        {
            var result = false;
            short n14 = 0;
            short n18 = 0;
            int n1C;
            ushort nPower = 0;
            short nAmuletIdx = 0;
            if (IsWarrSkill(UserMagic.wMagIdx))
            {
                return result;
            }
            // Native sub_6ED62C @0x6ED663-0x6ED67E: the range bail is a HARDCODED
            // literal 9 applied to EVERY spell, with no config read and no
            // per-skill exception:
            //   6ED663  mov  eax,[ebx+0x130]        ; CurrY
            //   6ED66A  mov  ecx,[ebx+0x12C]        ; CurrX
            //   6ED670  mov  edx,[ebp+0x0C] ; mov eax,[ebp-4]   ; target X/Y
            //   6ED676  call sub_78FE88             ; Chebyshev distance
            //   6ED67B  cmp  eax,9
            //   6ED67E  jg   0x6EE0C7               ; bail (MP already spent)
            // sub_78FE88 @0x78FE91-0x78FEAC is max(|a-c|,|b-d|) (two cdq/xor/sub
            // abs idioms then `cmp ecx,eax; jge; mov ecx,eax`), so the
            // `Abs(dx) > R || Abs(dy) > R` form below is the identical predicate.
            // The old code used g_Config.nMagicAttackRage (shipped default 8) with
            // a SKILL_153-only escape hatch to 9 — i.e. every ranged spell but 153
            // reached one tile less than native.
            const int magicAttackRange = 9;
            if ((Math.Abs(PlayObject.m_nCurrX - nTargetX) > magicAttackRange) || (Math.Abs(PlayObject.m_nCurrY - nTargetY) > magicAttackRange))
            {
                return result;
            }
            PlayObject.SendRefMsg(Grobal2.RM_SPELL, UserMagic.MagicInfo.btEffect, nTargetX, nTargetY, UserMagic.MagicInfo.wMagicID, "");
            if (TargeTBaseObject != null && TargeTBaseObject.m_boDeath)
            {
                TargeTBaseObject = null;
            }
            var boTrain = false;
            var boSpellFail = false;
            var boSpellFire = true;
            if (PlayObject.m_nSoftVersionDateEx == 0 &&
                PlayObject.m_dwClientTick == 0 &&
                UserMagic.MagicInfo.wMagicID > 40 &&
                UserMagic.MagicInfo.wMagicID != SpellsDef.SKILL_153)
            {
                return result;
            }
            switch (UserMagic.MagicInfo.wMagicID)
            {
                case SpellsDef.SKILL_FIREBALL:
                case SpellsDef.SKILL_FIREBALL2:
                    PlayObject.TryProduceNativeMagic1Or5(UserMagic,
                        TargeTBaseObject);
                    break;
                case SpellsDef.SKILL_HEALLING:
                    if (TargeTBaseObject == null)
                    {
                        TargeTBaseObject = PlayObject;
                        nTargetX = PlayObject.m_nCurrX;
                        nTargetY = PlayObject.m_nCurrY;
                    }
                    if (PlayObject.IsProperFriend(TargeTBaseObject))
                    {
                        nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.SC) * 2, (HUtil32.HiWord(PlayObject.m_WAbil.SC) - HUtil32.LoWord(PlayObject.m_WAbil.SC)) * 2 + 1);
                        if (TargeTBaseObject.m_WAbil.HP < TargeTBaseObject.m_WAbil.MaxHP)
                        {
                            TargeTBaseObject.SendDelayMsg(PlayObject, Grobal2.RM_MAGHEALING, 0, nPower, 0, 0, "", 800);
                            boTrain = true;
                        }
                        if (PlayObject.m_boAbilSeeHealGauge)
                        {
                            PlayObject.SendMsg(TargeTBaseObject, Grobal2.RM_10414, 0, 0, 0, 0, "");
                        }
                    }
                    break;
                case SpellsDef.SKILL_AMYOUNSUL:
                    boSpellFail = true;
                    if (PlayObject.IsProperTarget(TargeTBaseObject))
                    {
                        // Native wMagicID 6 handler @0x6ED945-0x6ED9F5 does NOT go
                        // through CheckAmulet (sub_73E93C). It inlines its own
                        // slot-9 fetch, TPoisons type test and a FIXED 100-durability
                        // decrement:
                        //   6ED949  mov  dl,9                ; U_BUJUK only
                        //   6ED951  call sub_75EC20          ; GetUseItem(9)
                        //   6ED95D  je   0x6EE04B            ; nil -> fail
                        //   6ED966  mov  edx,[0x75E4E8]      ; TPoisons class
                        //   6ED96C  call sub_404828          ; Delphi `is`
                        //   6ED973  je   0x6EE04B            ; not TPoisons -> fail
                        //   6ED97C  cmp  word [eax+0x26],0x64 ; Dura vs 100
                        //   6ED981  jb   0x6ED9B0            ; Dura<100 -> SKIP the
                        //                                    ;   decrement and STILL cast
                        //   6ED986  sub  word [eax+0x26],0x64 ; LITERAL 100, not nCount*100
                        //   6ED9A3  mov  cx,0x278D           ; RM_DURACHANGE
                        // The previous C# used CheckAmulet+UseAmulet(nCount=1,type=2),
                        // whose UseAmulet subtracts `nCount * 100` AND also admitted a
                        // charm in U_ARMRINGL — so 施毒术 drained charms twice as fast
                        // as native and could be powered from a slot native never reads.
                        var poisonCharm = PlayObject.m_UseItems[Grobal2.U_BUJUK];
                        var StdItem = poisonCharm == null || poisonCharm.wIndex <= 0
                            ? null
                            : M2Share.UserEngine.GetStdItem(poisonCharm.wIndex);
                        // StdMode 25 + Shape 1/2 == native `is TPoisons` (item factory
                        // sub_74C338 bytetab -> 0x74D066, Shape switch @0x74D07B:
                        // Shape 1,2 -> new TPoisons, Shape 5 -> new TBujuk).
                        if (StdItem != null && StdItem.StdMode == 25 &&
                            StdItem.Shape >= 1 && StdItem.Shape <= 2)
                        {
                            if (poisonCharm.Dura >= 100)
                            {
                                poisonCharm.Dura -= 100;
                                PlayObject.SendMsg(PlayObject,
                                    Grobal2.RM_DURACHANGE, Grobal2.U_BUJUK,
                                    poisonCharm.Dura, poisonCharm.DuraMax, 0, "");
                            }
                            if (M2Share.RandomNumber.Random(TargeTBaseObject.m_btAntiPoison + 7) <= 6)
                            {
                                switch (StdItem.Shape)
                                {
                                    case 1:
                                        nPower = (ushort)(DoSpell_GetPower13(UserMagic, 40) + DoSpell_GetRPow(PlayObject.m_WAbil.SC) * 2);// 中毒类型 - 绿毒
                                        TargeTBaseObject.SendDelayMsg(PlayObject, Grobal2.RM_POISON, Grobal2.POISON_DECHEALTH, nPower, PlayObject.ObjectId, HUtil32.Round(UserMagic.btLevel / 3.0 * ((double)nPower / M2Share.g_Config.nAmyOunsulPoint)), "", 1000);
                                        break;
                                    case 2:
                                        nPower = (ushort)(DoSpell_GetPower13(UserMagic, 30) + DoSpell_GetRPow(PlayObject.m_WAbil.SC) * 2);// 中毒类型 - 红毒
                                        TargeTBaseObject.SendDelayMsg(PlayObject, Grobal2.RM_POISON, Grobal2.POISON_DAMAGEARMOR, nPower, PlayObject.ObjectId, HUtil32.Round(UserMagic.btLevel / 3.0 * ((double)nPower / M2Share.g_Config.nAmyOunsulPoint)), "", 1000);
                                        break;
                                }
                                if (TargeTBaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT || TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                                {
                                    boTrain = true;
                                }
                            }
                            PlayObject.SetTargetCreat(TargeTBaseObject);
                            boSpellFail = false;
                            // Native tail @0x6ED9EB-0x6ED9F0: sub_73CC18 is called
                            // UNCONDITIONALLY on the charm after the applier, and
                            // destroys it once Dura < 100 (0x73CC33 `cmp word
                            // [edx+0x26],0x64; jae skip` -> VMT+0x268 then
                            // sub_75F27C(slot 9, "持久耗尽")).
                            ConsumeSpentPoisonCharm(PlayObject, poisonCharm);
                        }
                    }
                    break;
                case SpellsDef.SKILL_FIREWIND:
                    if (MagPushArround(PlayObject, UserMagic.btLevel) > 0)
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_FIRE:
                    n1C = M2Share.GetNextDirection(PlayObject.m_nCurrX, PlayObject.m_nCurrY, nTargetX, nTargetY);
                    if (PlayObject.m_PEnvir.GetNextPosition(PlayObject.m_nCurrX, PlayObject.m_nCurrY, n1C, 1, ref n14, ref n18))
                    {
                        PlayObject.m_PEnvir.GetNextPosition(PlayObject.m_nCurrX, PlayObject.m_nCurrY, n1C, 5, ref nTargetX, ref nTargetY);
                        nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC), HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1);
                        if (PlayObject.MagPassThroughMagic(n14, n18, nTargetX, nTargetY, n1C, nPower, false) > 0)
                        {
                            boTrain = true;
                        }
                    }
                    break;
                case SpellsDef.SKILL_SHOOTLIGHTEN:
                    n1C = M2Share.GetNextDirection(PlayObject.m_nCurrX, PlayObject.m_nCurrY, nTargetX, nTargetY);
                    if (PlayObject.m_PEnvir.GetNextPosition(PlayObject.m_nCurrX, PlayObject.m_nCurrY, n1C, 1, ref n14, ref n18))
                    {
                        PlayObject.m_PEnvir.GetNextPosition(PlayObject.m_nCurrX, PlayObject.m_nCurrY, n1C, 8, ref nTargetX, ref nTargetY);
                        nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC), (ushort)(HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1));
                        if (PlayObject.MagPassThroughMagic(n14, n18, nTargetX, nTargetY, n1C, nPower, true) > 0)
                        {
                            boTrain = true;
                        }
                    }
                    break;
                case SpellsDef.SKILL_LIGHTENING:
                    if (!PlayObject.TryProduceNativeMagic11(UserMagic,
                            TargeTBaseObject))
                        TargeTBaseObject = null;
                    break;
                case SpellsDef.SKILL_FIRECHARM:
                case SpellsDef.SKILL_HANGMAJINBUB:
                case SpellsDef.SKILL_DEJIWONHO:
                case SpellsDef.SKILL_HOLYSHIELD:
                case SpellsDef.SKILL_SKELLETON:
                case SpellsDef.SKILL_CLOAK:
                case SpellsDef.SKILL_BIGCLOAK:
                    boSpellFail = true;
                    if (Magic.CheckAmulet(PlayObject, 1, 1, ref nAmuletIdx))
                    {
                        Magic.UseAmulet(PlayObject, 1, 1, ref nAmuletIdx);
                        switch (UserMagic.MagicInfo.wMagicID)
                        {
                            case SpellsDef.SKILL_FIRECHARM:
                                if (PlayObject.MagCanHitTarget(PlayObject.m_nCurrX, PlayObject.m_nCurrY, TargeTBaseObject))
                                {
                                    if (PlayObject.IsProperTarget(TargeTBaseObject))
                                    {
                                        if (M2Share.RandomNumber.Random(10) >= TargeTBaseObject.m_nAntiMagic)
                                        {
                                            if (Math.Abs(TargeTBaseObject.m_nCurrX - nTargetX) <= 1 && Math.Abs(TargeTBaseObject.m_nCurrY - nTargetY) <= 1)
                                            {
                                                nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.SC), HUtil32.HiWord(PlayObject.m_WAbil.SC) - HUtil32.LoWord(PlayObject.m_WAbil.SC) + 1);
                                                PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_DELAYMAGIC, (short)nPower, HUtil32.MakeLong(nTargetX, nTargetY), 2, TargeTBaseObject.ObjectId, "", 1200);
                                                if (TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                                                {
                                                    boTrain = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    TargeTBaseObject = null;
                                }
                                break;
                            case SpellsDef.SKILL_HANGMAJINBUB:
                                nPower = PlayObject.GetAttackPower(DoSpell_GetPower13(UserMagic, 60) + HUtil32.LoWord(PlayObject.m_WAbil.SC) * 10, HUtil32.HiWord(PlayObject.m_WAbil.SC) - HUtil32.LoWord(PlayObject.m_WAbil.SC) + 1);
                                if (PlayObject.MagMakeDefenceArea(nTargetX, nTargetY, 3, nPower, 1) > 0)
                                {
                                    boTrain = true;
                                }
                                break;
                            case SpellsDef.SKILL_DEJIWONHO:
                                nPower = PlayObject.GetAttackPower(DoSpell_GetPower13(UserMagic, 60) + HUtil32.LoWord(PlayObject.m_WAbil.SC) * 10, HUtil32.HiWord(PlayObject.m_WAbil.SC) - HUtil32.LoWord(PlayObject.m_WAbil.SC) + 1);
                                if (PlayObject.MagMakeDefenceArea(nTargetX, nTargetY, 3, nPower, 0) > 0)
                                {
                                    boTrain = true;
                                }
                                break;
                            case SpellsDef.SKILL_HOLYSHIELD:
                                if (MagMakeHolyCurtain(PlayObject, DoSpell_GetPower13(UserMagic, 40) + DoSpell_GetRPow(PlayObject.m_WAbil.SC) * 3, nTargetX, nTargetY) > 0)
                                {
                                    boTrain = true;
                                }
                                break;
                            case SpellsDef.SKILL_SKELLETON:
                                if (MagMakeSlave(PlayObject, UserMagic))
                                {
                                    boTrain = true;
                                }
                                break;
                            case SpellsDef.SKILL_CLOAK:
                                if (MagMakePrivateTransparent(PlayObject, DoSpell_GetPower13(UserMagic, 30) + DoSpell_GetRPow(PlayObject.m_WAbil.SC) * 3))
                                {
                                    boTrain = true;
                                }
                                break;
                            case SpellsDef.SKILL_BIGCLOAK:
                                if (MagMakeGroupTransparent(PlayObject, nTargetX, nTargetY, DoSpell_GetPower13(UserMagic, 30) + DoSpell_GetRPow(PlayObject.m_WAbil.SC) * 3))
                                {
                                    boTrain = true;
                                }
                                break;
                        }
                        boSpellFail = false;
                    }
                    break;
                case SpellsDef.SKILL_TAMMING:
                    if (PlayObject.IsProperTarget(TargeTBaseObject))
                    {
                        if (MagTamming(PlayObject, TargeTBaseObject, nTargetX, nTargetY, UserMagic.btLevel))
                        {
                            boTrain = true;
                        }
                    }
                    break;
                case SpellsDef.SKILL_SPACEMOVE:
                    var targerActors = TargeTBaseObject == null ? 0 : TargeTBaseObject.ObjectId;
                    PlayObject.SendRefMsg(Grobal2.RM_MAGICFIRE, 0, HUtil32.MakeWord(UserMagic.MagicInfo.btEffectType, UserMagic.MagicInfo.btEffect), HUtil32.MakeLong(nTargetX, nTargetY), targerActors, "");
                    boSpellFire = false;
                    if (MagSaceMove(PlayObject, UserMagic.btLevel))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_EARTHFIRE:
                    if (MagMakeFireCross(PlayObject, UserMagic, PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC), HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1), DoSpell_GetScaledPower(UserMagic, 10) + (DoSpell_GetRPow(PlayObject.m_WAbil.MC) >> 1), nTargetX, nTargetY) > 0)
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_FIREBOOM:
                    QueueNativeAreaBlast(PlayObject, UserMagic, nTargetX,
                        nTargetY);
                    break;
                case SpellsDef.SKILL_LIGHTFLOWER:
                    nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC),
                        HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1);
                    if (MagElecBlizzard(PlayObject, nPower))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_SHOWHP:
                    if (TargeTBaseObject != null && !TargeTBaseObject.m_boShowHP)
                    {
                        if (M2Share.RandomNumber.Random(6) <= UserMagic.btLevel + 3)
                        {
                            TargeTBaseObject.m_dwShowHPTick = HUtil32.GetTickCount();
                            TargeTBaseObject.m_dwShowHPInterval = DoSpell_GetPower13(UserMagic, DoSpell_GetRPow(PlayObject.m_WAbil.SC) * 2 + 30) * 1000;
                            TargeTBaseObject.SendDelayMsg(TargeTBaseObject, Grobal2.RM_DOOPENHEALTH, 0, 0, 0, 0, "", 1500);
                            boTrain = true;
                        }
                    }
                    break;
                case SpellsDef.SKILL_BIGHEALLING:
                    nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.SC) * 2, (HUtil32.HiWord(PlayObject.m_WAbil.SC) - HUtil32.LoWord(PlayObject.m_WAbil.SC)) * 2 + 1);
                    if (MagBigHealing(PlayObject, nPower, nTargetX, nTargetY))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_SINSU:
                    boSpellFail = true;
                    if (Magic.CheckAmulet(PlayObject, 5, 1, ref nAmuletIdx))
                    {
                        Magic.UseAmulet(PlayObject, 5, 1, ref nAmuletIdx);
                        if (MagMakeSinSuSlave(PlayObject, UserMagic))
                        {
                            boTrain = true;
                        }
                        boSpellFail = false;
                    }
                    break;
                case SpellsDef.SKILL_ANGEL:
                    boSpellFail = true;
                    if (Magic.CheckAmulet(PlayObject, 2, 1, ref nAmuletIdx))
                    {
                        Magic.UseAmulet(PlayObject, 2, 1, ref nAmuletIdx);
                        if (MagMakeAngelSlave(PlayObject, UserMagic))
                        {
                            boTrain = true;
                        }
                        boSpellFail = false;
                    }
                    break;
                case SpellsDef.SKILL_SHIELD:
                    if (PlayObject.MagBubbleDefenceUp(UserMagic.btLevel, DoSpell_GetScaledPower(UserMagic, DoSpell_GetRPow(PlayObject.m_WAbil.MC) + 15)))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_KILLUNDEAD:
                    if (PlayObject.IsProperTarget(TargeTBaseObject))
                    {
                        if (MagTurnUndead(PlayObject, TargeTBaseObject, nTargetX, nTargetY, UserMagic.btLevel))
                        {
                            boTrain = true;
                        }
                    }
                    break;
                case SpellsDef.SKILL_SNOWWIND:
                    QueueNativeAreaBlast(PlayObject, UserMagic, nTargetX,
                        nTargetY);
                    break;
                case SpellsDef.SKILL_UNAMYOUNSUL:
                    if (TargeTBaseObject == null)
                    {
                        TargeTBaseObject = PlayObject;
                        nTargetX = PlayObject.m_nCurrX;
                        nTargetY = PlayObject.m_nCurrY;
                    }
                    if (PlayObject.IsProperFriend(TargeTBaseObject))
                    {
                        if (M2Share.RandomNumber.Random(7) - (UserMagic.btLevel + 1) < 0)
                        {
                            if (TargeTBaseObject.m_wStatusTimeArr[Grobal2.POISON_DECHEALTH] != 0)
                            {
                                TargeTBaseObject.m_wStatusTimeArr[Grobal2.POISON_DECHEALTH] = 1;
                                boTrain = true;
                            }
                            if (TargeTBaseObject.m_wStatusTimeArr[Grobal2.POISON_DAMAGEARMOR] != 0)
                            {
                                TargeTBaseObject.m_wStatusTimeArr[Grobal2.POISON_DAMAGEARMOR] = 1;
                                boTrain = true;
                            }
                            if (TargeTBaseObject.m_wStatusTimeArr[Grobal2.POISON_STONE] != 0)
                            {
                                TargeTBaseObject.m_wStatusTimeArr[Grobal2.POISON_STONE] = 1;
                                boTrain = true;
                            }
                        }
                    }
                    break;
                case SpellsDef.SKILL_WINDTEBO:
                    if (!PlayObject.TryProduceNativeMagic35(UserMagic,
                            TargeTBaseObject))
                        TargeTBaseObject = null;
                    break;
                case SpellsDef.SKILL_MABE:
                    nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC), HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1);
                    if (MabMabe(PlayObject, TargeTBaseObject, nPower, UserMagic.btLevel, nTargetX, nTargetY))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_GROUPLIGHTENING:
                    if (MagGroupLightening(PlayObject, UserMagic, nTargetX, nTargetY, TargeTBaseObject, ref boSpellFire))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_GROUPAMYOUNSUL:
                    if (MagGroupAmyounsul(PlayObject, UserMagic, nTargetX, nTargetY, TargeTBaseObject))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_GROUPDEDING:
                    PlayObject.TryProduceNativeMagic39(UserMagic,
                        TargeTBaseObject);
                    break;
                case SpellsDef.SKILL_43:
                    PlayObject.m_bo43kill = !PlayObject.m_bo43kill;
                    if (PlayObject.m_bo43kill)
                    {
                        PlayObject.SysMsg("开启破空剑", MsgColor.Green, MsgType.Hint);
                    }
                    else
                    {
                        PlayObject.SysMsg("关闭破空剑", MsgColor.Green, MsgType.Hint);
                    }
                    boTrain = true;
                    break;
                case SpellsDef.SKILL_44:
                    if (MagHbFireBall(PlayObject, UserMagic, nTargetX, nTargetY, ref TargeTBaseObject))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_45:
                    if (PlayObject.IsProperTarget(TargeTBaseObject))
                    {
                        if (M2Share.RandomNumber.Random(10) >= TargeTBaseObject.m_nAntiMagic)
                        {
                            nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC), HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1);
                            if (TargeTBaseObject.m_btLifeAttrib == Grobal2.LA_UNDEAD)
                            {
                                nPower = (ushort)HUtil32.Round(nPower * 1.5);
                            }
                            PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_DELAYMAGIC, (short)nPower, HUtil32.MakeLong(nTargetX, nTargetY), 2, TargeTBaseObject.ObjectId, "", 600);
                            if (TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                            {
                                boTrain = true;
                            }
                        }
                        else
                        {
                            TargeTBaseObject = null;
                        }
                    }
                    else
                    {
                        TargeTBaseObject = null;
                    }
                    break;
                case SpellsDef.SKILL_46:
                    if (MagMakeClone(PlayObject, UserMagic))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_47:
                    if (MagBigExplosion(PlayObject, PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC), HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1), nTargetX, nTargetY, M2Share.g_Config.nFireBoomRage))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_ENERGYREPULSOR:
                    if (MagPushArround(PlayObject, UserMagic.btLevel) > 0)
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_49:
                    if (PlayObject.MagCanHitTarget(PlayObject.m_nCurrX, PlayObject.m_nCurrY, TargeTBaseObject))
                    {
                        if (PlayObject.IsProperTarget(TargeTBaseObject))
                        {
                            if (TargeTBaseObject.m_nAntiMagic <= M2Share.RandomNumber.Random(10) && Math.Abs(TargeTBaseObject.m_nCurrX - nTargetX) <= 1 && Math.Abs(TargeTBaseObject.m_nCurrY - nTargetY) <= 1)
                            {
                                nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC), HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1);
                                PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_DELAYMAGIC, (short)nPower, HUtil32.MakeLong(nTargetX, nTargetY), 2, TargeTBaseObject.ObjectId, "", 600);
                                if (TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                                {
                                    boTrain = true;
                                }
                            }
                            else
                            {
                                TargeTBaseObject = null;
                            }
                        }
                        else
                        {
                            TargeTBaseObject = null;
                        }
                    }
                    else
                    {
                        TargeTBaseObject = null;
                    }
                    break;
                case SpellsDef.SKILL_UENHANCER:
                    boSpellFail = true;
                    if (TargeTBaseObject == null)
                    {
                        TargeTBaseObject = PlayObject;
                        nTargetX = PlayObject.m_nCurrX;
                        nTargetY = PlayObject.m_nCurrY;
                    }
                    if (PlayObject.IsProperFriend(TargeTBaseObject))
                    {
                        if (Magic.CheckAmulet(PlayObject, 1, 1, ref nAmuletIdx))
                        {
                            Magic.UseAmulet(PlayObject, 1, 1, ref nAmuletIdx);
                            nPower = (ushort)(UserMagic.btLevel + 1 + M2Share.RandomNumber.Random(UserMagic.btLevel));
                            n14 = (short)PlayObject.GetAttackPower(DoSpell_GetPower13(UserMagic, 60) + HUtil32.LoWord(PlayObject.m_WAbil.SC) * 10, HUtil32.HiWord(PlayObject.m_WAbil.SC) - HUtil32.LoWord(PlayObject.m_WAbil.SC) + 1);
                            if (TargeTBaseObject.AttPowerUp(nPower, n14))
                            {
                                boTrain = true;
                            }
                            boSpellFail = false;
                        }
                    }
                    break;
                case SpellsDef.SKILL_51:// 灵魂召唤术
                    boSpellFail = true;
                    if (Magic.CheckAmulet(PlayObject, 1, 1, ref nAmuletIdx))
                    {
                        Magic.UseAmulet(PlayObject, 1, 1, ref nAmuletIdx);
                        if (MagMakeSlave(PlayObject, UserMagic))
                        {
                            boTrain = true;
                        }
                        boSpellFail = false;
                    }
                    break;
                case SpellsDef.SKILL_52:// 诅咒术
                    if (PlayObject.IsProperTarget(TargeTBaseObject))
                    {
                        if (M2Share.RandomNumber.Random(TargeTBaseObject.m_nAntiMagic + 5) <= 5)
                        {
                            nPower = (ushort)(DoSpell_GetPower13(UserMagic, 25) + DoSpell_GetRPow(PlayObject.m_WAbil.SC) * 2);
                            TargeTBaseObject.SendDelayMsg(PlayObject, Grobal2.RM_POISON, Grobal2.POISON_DAMAGEARMOR, nPower, PlayObject.ObjectId, HUtil32.Round(UserMagic.btLevel * 2 + 10), "", 1000);
                            if (TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                            {
                                boTrain = true;
                            }
                        }
                    }
                    break;
                case SpellsDef.SKILL_53:
                    if (PlayObject.MagCanHitTarget(PlayObject.m_nCurrX, PlayObject.m_nCurrY, TargeTBaseObject))
                    {
                        if (PlayObject.IsProperTarget(TargeTBaseObject))
                        {
                            if (TargeTBaseObject.m_nAntiMagic <= M2Share.RandomNumber.Random(10) && Math.Abs(TargeTBaseObject.m_nCurrX - nTargetX) <= 1 && Math.Abs(TargeTBaseObject.m_nCurrY - nTargetY) <= 1)
                            {
                                nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.SC), HUtil32.HiWord(PlayObject.m_WAbil.SC) - HUtil32.LoWord(PlayObject.m_WAbil.SC) + 1);
                                PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_DELAYMAGIC, (short)nPower, HUtil32.MakeLong(nTargetX, nTargetY), 2, TargeTBaseObject.ObjectId, "", 600);
                                if (TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                                {
                                    boTrain = true;
                                }
                            }
                            else
                            {
                                TargeTBaseObject = null;
                            }
                        }
                        else
                        {
                            TargeTBaseObject = null;
                        }
                    }
                    else
                    {
                        TargeTBaseObject = null;
                    }
                    break;
                case SpellsDef.SKILL_54:
                    if (PlayObject.MagCanHitTarget(PlayObject.m_nCurrX, PlayObject.m_nCurrY, TargeTBaseObject))
                    {
                        if (PlayObject.IsProperTarget(TargeTBaseObject))
                        {
                            if (TargeTBaseObject.m_nAntiMagic <= M2Share.RandomNumber.Random(10) && Math.Abs(TargeTBaseObject.m_nCurrX - nTargetX) <= 1 && Math.Abs(TargeTBaseObject.m_nCurrY - nTargetY) <= 1)
                            {
                                nPower = PlayObject.GetAttackPower(DoSpell_GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC), HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1);
                                PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_DELAYMAGIC, (short)nPower, HUtil32.MakeLong(nTargetX, nTargetY), 2, TargeTBaseObject.ObjectId, "", 600);
                                if (TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                                {
                                    boTrain = true;
                                }
                            }
                            else
                            {
                                TargeTBaseObject = null;
                            }
                        }
                        else
                        {
                            TargeTBaseObject = null;
                        }
                    }
                    else
                    {
                        TargeTBaseObject = null;
                    }
                    break;
                case SpellsDef.SKILL_152:
                    boSpellFail = !PlayObject.TryActivateNativeSkill152(
                        UserMagic);
                    break;
                case SpellsDef.SKILL_153:
                    boSpellFail = !PlayObject.TryActivateNativeSkill153Shield(
                        UserMagic);
                    break;
            }
            if (boSpellFail)
            {
                return result;
            }
            if (boSpellFire)
            {
                if (TargeTBaseObject == null)
                {
                    PlayObject.SendRefMsg(Grobal2.RM_MAGICFIRE, 0, HUtil32.MakeWord(UserMagic.MagicInfo.btEffectType, UserMagic.MagicInfo.btEffect), HUtil32.MakeLong(nTargetX, nTargetY), 0, "");
                }
                else
                {
                    PlayObject.SendRefMsg(Grobal2.RM_MAGICFIRE, 0, HUtil32.MakeWord(UserMagic.MagicInfo.btEffectType, UserMagic.MagicInfo.btEffect), HUtil32.MakeLong(nTargetX, nTargetY), TargeTBaseObject.ObjectId, "");
                }
            }
            if (UserMagic.btLevel < 3 && boTrain)
            {
                if (UserMagic.btLevel < UserMagic.MagicInfo.TrainLevel.Length && UserMagic.MagicInfo.TrainLevel[UserMagic.btLevel] <= PlayObject.m_Abil.Level)
                {
                    PlayObject.TrainSkill(UserMagic, M2Share.RandomNumber.Random(3) + 1);
                    if (!PlayObject.CheckMagicLevelup(UserMagic))
                    {
                        PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_MAGIC_LVEXP, 0, UserMagic.MagicInfo.wMagicID, UserMagic.btLevel, UserMagic.nTranPoint, "", 1000);
                    }
                }
            }

            return true;
        }

        public bool MagMakePrivateTransparent(TBaseObject BaseObject, int nHTime)
        {
            if (BaseObject.m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] > 0)
            {
                return false;
            }
            IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
            BaseObject.GetMapBaseObjects(BaseObject.m_PEnvir, BaseObject.m_nCurrX, BaseObject.m_nCurrY, 9, BaseObjectList);
            for (var i = 0; i < BaseObjectList.Count; i++)
            {
                var TargeTBaseObject = BaseObjectList[i];
                if (TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL && TargeTBaseObject.m_TargetCret == BaseObject)
                {
                    if (Math.Abs(TargeTBaseObject.m_nCurrX - BaseObject.m_nCurrX) > 1 || Math.Abs(TargeTBaseObject.m_nCurrY - BaseObject.m_nCurrY) > 1 || M2Share.RandomNumber.Random(2) == 0)
                    {
                        TargeTBaseObject.m_TargetCret = null;
                    }
                }
            }
            BaseObjectList.Clear();
            BaseObjectList = null;
            BaseObject.m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] = (ushort)nHTime;
            BaseObject.m_nCharStatus = BaseObject.GetCharStatus();
            BaseObject.StatusChanged();
            BaseObject.m_boHideMode = true;
            BaseObject.m_boTransparent = true;
            return true;
        }

        private bool MagTamming(TBaseObject BaseObject, TBaseObject TargeTBaseObject, int nTargetX, int nTargetY, int nMagicLevel)
        {
            var result = false;
            if (TargeTBaseObject.m_btRaceServer != Grobal2.RC_PLAYOBJECT && M2Share.RandomNumber.Random(4 - nMagicLevel) == 0)
            {
                TargeTBaseObject.m_TargetCret = null;
                if (TargeTBaseObject.m_Master == BaseObject)
                {
                    TargeTBaseObject.OpenHolySeizeMode((nMagicLevel * 5 + 10) * 1000);
                    result = true;
                }
                else
                {
                    if (M2Share.RandomNumber.Random(2) == 0)
                    {
                        if (TargeTBaseObject.m_Abil.Level <= BaseObject.m_Abil.Level + 2)
                        {
                            if (M2Share.RandomNumber.Random(3) == 0)
                            {
                                if (M2Share.RandomNumber.Random(BaseObject.m_Abil.Level + 20 + nMagicLevel * 5) > TargeTBaseObject.m_Abil.Level + M2Share.g_Config.nMagTammingTargetLevel)
                                {
                                    if (!TargeTBaseObject.m_boNoTame && TargeTBaseObject.m_btLifeAttrib != Grobal2.LA_UNDEAD && TargeTBaseObject.m_Abil.Level < M2Share.g_Config.nMagTammingLevel && BaseObject.m_SlaveList.Count < M2Share.g_Config.nMagTammingCount)
                                    {
                                        int n14 = TargeTBaseObject.m_WAbil.MaxHP / M2Share.g_Config.nMagTammingHPRate;
                                        if (n14 <= 2)
                                        {
                                            n14 = 2;
                                        }
                                        else
                                        {
                                            n14 += n14;
                                        }
                                        if (TargeTBaseObject.m_Master != BaseObject && M2Share.RandomNumber.Random(n14) == 0)
                                        {
                                            TargeTBaseObject.BreakCrazyMode();
                                            if (TargeTBaseObject.m_Master != null)
                                            {
                                                TargeTBaseObject.m_WAbil.HP /= 10;
                                            }

                                            if (TargeTBaseObject.m_boCanReAlive && TargeTBaseObject.m_Master == null)
                                            {
                                                TargeTBaseObject.m_boCanReAlive = false;
                                                if (TargeTBaseObject.m_pMonGen != null)
                                                {
                                                    if (TargeTBaseObject.m_pMonGen.nActiveCount > 0)
                                                    {
                                                        TargeTBaseObject.m_pMonGen.nActiveCount--;
                                                    }
                                                    else
                                                    {
                                                        TargeTBaseObject.m_pMonGen = null;
                                                    }
                                                }
                                            }
                                            TargeTBaseObject.m_Master = BaseObject;
                                            TargeTBaseObject.m_dwMasterRoyaltyTick = (M2Share.RandomNumber.Random(BaseObject.m_Abil.Level * 2) + (nMagicLevel << 2) * 5 + 20) * 60 * 1000 + HUtil32.GetTickCount();
                                            TargeTBaseObject.m_btSlaveMakeLevel = (byte)nMagicLevel;
                                            if (TargeTBaseObject.m_dwMasterTick == 0)
                                            {
                                                TargeTBaseObject.m_dwMasterTick = HUtil32.GetTickCount();
                                            }
                                            TargeTBaseObject.BreakHolySeizeMode();
                                            if (1500 - nMagicLevel * 200 < TargeTBaseObject.m_nWalkSpeed)
                                            {
                                                TargeTBaseObject.m_nWalkSpeed = 1500 - nMagicLevel * 200;
                                            }
                                            if (2000 - nMagicLevel * 200 < TargeTBaseObject.m_nNextHitTime)
                                            {
                                                TargeTBaseObject.m_nNextHitTime = 2000 - nMagicLevel * 200;
                                            }
                                            TargeTBaseObject.RefShowName();
                                            BaseObject.m_SlaveList.Add(TargeTBaseObject);
                                        }
                                        else
                                        {
                                            if (M2Share.RandomNumber.Random(14) == 0)
                                            {
                                                TargeTBaseObject.m_WAbil.HP = 0;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (TargeTBaseObject.m_btLifeAttrib == Grobal2.LA_UNDEAD && M2Share.RandomNumber.Random(20) == 0)
                                        {
                                            TargeTBaseObject.m_WAbil.HP = 0;
                                        }
                                    }
                                }
                                else
                                {
                                    if (TargeTBaseObject.m_btLifeAttrib != Grobal2.LA_UNDEAD && M2Share.RandomNumber.Random(20) == 0)
                                    {
                                        TargeTBaseObject.OpenCrazyMode(M2Share.RandomNumber.Random(20) + 10);
                                    }
                                }
                            }
                            else
                            {
                                if (TargeTBaseObject.m_btLifeAttrib != Grobal2.LA_UNDEAD)
                                {
                                    TargeTBaseObject.OpenCrazyMode(M2Share.RandomNumber.Random(20) + 10);// 变红
                                }
                            }
                        }
                    }
                    else
                    {
                        TargeTBaseObject.OpenHolySeizeMode((nMagicLevel * 5 + 10) * 1000);
                    }
                    result = true;
                }
            }
            else
            {
                if (M2Share.RandomNumber.Random(2) == 0)
                {
                    result = true;
                }
            }
            return result;
        }

        private bool MagTurnUndead(TBaseObject BaseObject, TBaseObject TargeTBaseObject, int nTargetX, int nTargetY, int nLevel)
        {
            var result = false;
            if (TargeTBaseObject.m_boSuperMan || TargeTBaseObject.m_btLifeAttrib != Grobal2.LA_UNDEAD)
            {
                return result;
            }
            ((AnimalObject)TargeTBaseObject).Struck(BaseObject);
            if (TargeTBaseObject.m_TargetCret == null)
            {
                ((AnimalObject)TargeTBaseObject).m_boRunAwayMode = true;
                ((AnimalObject)TargeTBaseObject).m_dwRunAwayStart = HUtil32.GetTickCount();
                ((AnimalObject)TargeTBaseObject).m_dwRunAwayTime = 10 * 1000;
            }
            BaseObject.SetTargetCreat(TargeTBaseObject);
            if (M2Share.RandomNumber.Random(2) + (BaseObject.m_Abil.Level - 1) > TargeTBaseObject.m_Abil.Level)
            {
                if (TargeTBaseObject.m_Abil.Level < M2Share.g_Config.nMagTurnUndeadLevel)
                {
                    var n14 = BaseObject.m_Abil.Level - TargeTBaseObject.m_Abil.Level;
                    if (M2Share.RandomNumber.Random(100) < (nLevel << 3) - nLevel + 15 + n14)
                    {
                        TargeTBaseObject.SetLastHiter(BaseObject);
                        TargeTBaseObject.m_WAbil.HP = 0;
                        result = true;
                    }
                }
            }
            return result;
        }

        private bool MagWindTebo(TPlayObject PlayObject, TUserMagic UserMagic)
        {
            var result = false;
            var PoseBaseObject = PlayObject.GetPoseCreate();
            if (PoseBaseObject != null && PoseBaseObject != PlayObject && !PoseBaseObject.m_boDeath && !PoseBaseObject.m_boGhost && PlayObject.IsProperTarget(PoseBaseObject) && !PoseBaseObject.m_boStickMode)
            {
                if (Math.Abs(PlayObject.m_nCurrX - PoseBaseObject.m_nCurrX) <= 1 && Math.Abs(PlayObject.m_nCurrY - PoseBaseObject.m_nCurrY) <= 1 && PlayObject.m_Abil.Level > PoseBaseObject.m_Abil.Level)
                {
                    if (M2Share.RandomNumber.Random(20) < UserMagic.btLevel * 6 + 6 + (PlayObject.m_Abil.Level - PoseBaseObject.m_Abil.Level))
                    {
                        PoseBaseObject.CharPushed(M2Share.GetNextDirection(PlayObject.m_nCurrX, PlayObject.m_nCurrY, PoseBaseObject.m_nCurrX, PoseBaseObject.m_nCurrY), HUtil32._MAX(0, UserMagic.btLevel - 1) + 1);
                        result = true;
                    }
                }
            }
            return result;
        }

        private bool MagSaceMove(TBaseObject BaseObject, int nLevel)
        {
            var result = false;
            if (M2Share.RandomNumber.Random(11) < nLevel * 2 + 4)
            {
                BaseObject.SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE2, 0, 0, 0, 0, "");
                if (BaseObject is TPlayObject)
                {
                    var Envir = BaseObject.m_PEnvir;
                    BaseObject.MapRandomMove(BaseObject.m_sHomeMap, 1);
                    if (Envir != BaseObject.m_PEnvir && BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                    {
                        var PlayObject = (TPlayObject)BaseObject;
                        PlayObject.m_boTimeRecall = false;
                    }
                }
                result = true;
            }
            return result;
        }

        private bool MagGroupAmyounsul(TPlayObject PlayObject, TUserMagic UserMagic, int nTargetX, int nTargetY, TBaseObject TargeTBaseObject)
        {
            short nAmuletIdx = 0;
            var result = false;
            IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
            PlayObject.GetMapBaseObjects(PlayObject.m_PEnvir, nTargetX, nTargetY, HUtil32._MAX(1, UserMagic.btLevel), BaseObjectList);
            for (var i = 0; i < BaseObjectList.Count; i++)
            {
                var BaseObject = BaseObjectList[i];
                if (BaseObject.m_boDeath || BaseObject.m_boGhost || PlayObject == BaseObject)
                {
                    continue;
                }
                if (PlayObject.IsProperTarget(BaseObject))
                {
                    if (Magic.CheckAmulet(PlayObject, 1, 2, ref nAmuletIdx))
                    {
                        var StdItem = M2Share.UserEngine.GetStdItem(PlayObject.m_UseItems[nAmuletIdx].wIndex);
                        if (StdItem != null)
                        {
                            Magic.UseAmulet(PlayObject, 1, 2, ref nAmuletIdx);
                            if (M2Share.RandomNumber.Random(BaseObject.m_btAntiPoison + 7) <= 6)
                            {
                                int nPower;
                                switch (StdItem.Shape)
                                {
                                    case 1:
                                        nPower = Magic.GetPower13(40, UserMagic) + Magic.GetRPow(PlayObject.m_WAbil.SC) * 2;// 中毒类型 - 绿毒
                                        BaseObject.SendDelayMsg(PlayObject, Grobal2.RM_POISON, Grobal2.POISON_DECHEALTH, nPower, PlayObject.ObjectId, HUtil32.Round(UserMagic.btLevel / 3.0 * ((double)nPower / M2Share.g_Config.nAmyOunsulPoint)), "", 1000);
                                        break;
                                    case 2:
                                        nPower = Magic.GetPower13(30, UserMagic) + Magic.GetRPow(PlayObject.m_WAbil.SC) * 2;// 中毒类型 - 红毒
                                        BaseObject.SendDelayMsg(PlayObject, Grobal2.RM_POISON, Grobal2.POISON_DAMAGEARMOR, nPower, PlayObject.ObjectId, HUtil32.Round(UserMagic.btLevel / 3.0 * ((double)nPower / M2Share.g_Config.nAmyOunsulPoint)), "", 1000);
                                        break;
                                }
                                if (BaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT || BaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                                {
                                    result = true;
                                }
                            }
                        }
                        PlayObject.SetTargetCreat(BaseObject);
                    }
                }
            }
            BaseObjectList.Clear();
            BaseObjectList = null;
            return result;
        }

        private bool MagGroupDeDing(TPlayObject PlayObject, TUserMagic UserMagic, int nTargetX, int nTargetY, TBaseObject TargeTBaseObject)
        {
            TBaseObject BaseObject;
            var result = false;
            IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
            PlayObject.GetMapBaseObjects(PlayObject.m_PEnvir, nTargetX, nTargetY, HUtil32._MAX(1, UserMagic.btLevel), BaseObjectList);
            for (var i = 0; i < BaseObjectList.Count; i++)
            {
                BaseObject = BaseObjectList[i];
                if (BaseObject.m_boDeath || BaseObject.m_boGhost || PlayObject == BaseObject)
                {
                    continue;
                }
                if (PlayObject.IsProperTarget(BaseObject))
                {
                    var nPower = PlayObject.GetAttackPower(HUtil32.LoWord(PlayObject.m_WAbil.DC), HUtil32.HiWord(PlayObject.m_WAbil.DC) - HUtil32.LoWord(PlayObject.m_WAbil.DC));
                    if (M2Share.RandomNumber.Random(BaseObject.m_wSpeedPoint) >= PlayObject.m_btHitPoint)
                    {
                        nPower = 0;
                    }
                    if (nPower > 0)
                    {
                        nPower = BaseObject.GetHitStruckDamage(PlayObject, nPower);
                    }
                    if (nPower > 0)
                    {
                        BaseObject.StruckDamage(nPower, PlayObject);
                        PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_DELAYMAGIC, (short)nPower, HUtil32.MakeLong(BaseObject.m_nCurrX, BaseObject.m_nCurrY), 1, BaseObject.ObjectId, "", 200);
                    }
                    if (BaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                    {
                        result = true;
                    }
                }
                PlayObject.SendRefMsg(Grobal2.RM_10205, 0, BaseObject.m_nCurrX, BaseObject.m_nCurrY, 1, "");
            }
            BaseObjectList.Clear();
            BaseObjectList = null;
            return result;
        }

        private bool MagGroupLightening(TPlayObject PlayObject, TUserMagic UserMagic, int nTargetX, int nTargetY, TBaseObject TargeTBaseObject, ref bool boSpellFire)
        {
            var result = false;
            boSpellFire = false;
            IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
            PlayObject.GetMapBaseObjects(PlayObject.m_PEnvir, nTargetX, nTargetY, HUtil32._MAX(1, UserMagic.btLevel), BaseObjectList);
            PlayObject.SendRefMsg(Grobal2.RM_MAGICFIRE, 0, HUtil32.MakeWord(UserMagic.MagicInfo.btEffectType, UserMagic.MagicInfo.btEffect), HUtil32.MakeLong(nTargetX, nTargetY), TargeTBaseObject.ObjectId, "");
            for (var i = 0; i < BaseObjectList.Count; i++)
            {
                var BaseObject = BaseObjectList[i];
                if (BaseObject.m_boDeath || BaseObject.m_boGhost || PlayObject == BaseObject)
                {
                    continue;
                }
                if (PlayObject.IsProperTarget(BaseObject))
                {
                    if (M2Share.RandomNumber.Random(10) >= BaseObject.m_nAntiMagic)
                    {
                        var nPower = PlayObject.GetAttackPower(Magic.GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC), HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1);
                        if (BaseObject.m_btLifeAttrib == Grobal2.LA_UNDEAD)
                        {
                            nPower = (ushort)HUtil32.Round(nPower * 1.5);
                        }
                        PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_DELAYMAGIC, (short)nPower, HUtil32.MakeLong(BaseObject.m_nCurrX, BaseObject.m_nCurrY), 2, BaseObject.ObjectId, "", 600);
                        if (BaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                        {
                            result = true;
                        }
                    }
                    if (BaseObject.m_nCurrX != nTargetX || BaseObject.m_nCurrY != nTargetY)
                    {
                        PlayObject.SendRefMsg(Grobal2.RM_10205, 0, BaseObject.m_nCurrX, BaseObject.m_nCurrY, 4, "");
                    }
                }
            }
            BaseObjectList.Clear();
            BaseObjectList = null;
            return result;
        }

        private bool MagHbFireBall(TPlayObject PlayObject, TUserMagic UserMagic, int nTargetX, int nTargetY, ref TBaseObject TargetBaseObject)
        {
            var result = false;
            if (!PlayObject.MagCanHitTarget(PlayObject.m_nCurrX, PlayObject.m_nCurrY, TargetBaseObject))
            {
                TargetBaseObject = null;
                return result;
            }
            if (!PlayObject.IsProperTarget(TargetBaseObject))
            {
                TargetBaseObject = null;
                return result;
            }
            if (TargetBaseObject.m_nAntiMagic > M2Share.RandomNumber.Random(10) || Math.Abs(TargetBaseObject.m_nCurrX - nTargetX) > 1 || Math.Abs(TargetBaseObject.m_nCurrY - nTargetY) > 1)
            {
                TargetBaseObject = null;
                return result;
            }
            var nPower = PlayObject.GetAttackPower(Magic.GetPower(UserMagic) + HUtil32.LoWord(PlayObject.m_WAbil.MC), HUtil32.HiWord(PlayObject.m_WAbil.MC) - HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1);
            PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_DELAYMAGIC, (short)nPower, HUtil32.MakeLong(nTargetX, nTargetY), 2, TargetBaseObject.ObjectId, "", 600);
            if (TargetBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
            {
                result = true;
            }
            if (PlayObject.m_Abil.Level > TargetBaseObject.m_Abil.Level && !TargetBaseObject.m_boStickMode)
            {
                var levelgap = PlayObject.m_Abil.Level - TargetBaseObject.m_Abil.Level;
                if (M2Share.RandomNumber.Random(20) < 6 + UserMagic.btLevel * 3 + levelgap)
                {
                    var push = M2Share.RandomNumber.Random(UserMagic.btLevel) - 1;
                    if (push > 0)
                    {
                        int nDir = M2Share.GetNextDirection(PlayObject.m_nCurrX, PlayObject.m_nCurrY, TargetBaseObject.m_nCurrX, TargetBaseObject.m_nCurrY);
                        PlayObject.SendDelayMsg(PlayObject, Grobal2.RM_DELAYPUSHED, (short)nDir, HUtil32.MakeLong(nTargetX, nTargetY), push, TargetBaseObject.ObjectId, "", 600);
                    }
                }
            }
            return result;
        }

        
        
        
        
        private int MagMakeFireCross(TPlayObject PlayObject, TUserMagic UserMagic,
            int nDamage, int nHTime, int nX, int nY)
        {
            const string sDisableInSafeZoneFireCross = "安全区不允许使用...";
            if (M2Share.g_Config.boDisableInSafeZoneFireCross && PlayObject.InSafeZone(PlayObject.m_PEnvir, nX, nY))
            {
                PlayObject.SysMsg(sDisableInSafeZoneFireCross, MsgColor.Red, MsgType.Notice);
                return 0;
            }

            void AddFire(int fireX, int fireY)
            {
                if (PlayObject.m_PEnvir.GetEvent(fireX, fireY,
                        Grobal2.ET_FIRE) != null)
                {
                    return;
                }

                var fireEvent = new FireBurnEvent(PlayObject, UserMagic, fireX,
                    fireY, Grobal2.ET_FIRE, nHTime * 1000, nDamage);
                M2Share.EventManager.AddEvent(fireEvent);
            }

            AddFire(nX, nY);
            AddFire(nX, nY + 1);
            AddFire(nX, nY - 1);
            AddFire(nX - 1, nY);
            AddFire(nX + 1, nY);
            return 1;
        }

        // Native 爆裂火焰 (wMagicID 23) = sub_76F21C, reached from the DoSpell
        // jump table entry T1[23] -> 0x6EDCFD -> `call sub_76F21C`;
        // 冰咆哮 (wMagicID 33) = sub_76F2AC, T1[33] -> 0x6EDD12. The two bodies
        // are byte-identical apart from the flag global they read.
        //   76F22A  mov  ax,[MagicInfo+0x10]        ; skillId       -> push
        //   76F234  call sub_4C8648                 ; skill power (raw btLevel)
        //   76F23B  mov  edi,[ebx+0x294]            ; LoWord(MC)
        //   76F241  add  edx,edi                    ; base = power + LoWord(MC)
        //   76F243  mov  ecx,[ebx+0x298] ; sub ecx,edi ; inc ecx   ; HiWord-LoWord+1
        //   76F250  call [edi+0xCC]                 ; GetAttackPower(base, spread)
        //   76F256  mov  edi,eax                    ; = the raw damage
        //   76F258  push esi                        -> [ebp+0x28] context blob
        //   76F25B  call sub_4C853C ; push          -> [ebp+0x24] skillId
        //   76F261  mov  ax,[ebp-4]  ; push         -> [ebp+0x20] X
        //   76F266  mov  ax,[ebp+8]  ; push         -> [ebp+0x1C] Y
        //   76F26B  push 0x258                      -> [ebp+0x18] delay = 600 ms
        //   76F270  push 1                          -> [ebp+0x14] range = 1
        //   76F272  push 3                          -> [ebp+0x10] dispatchCategory = 3 (AREA)
        //   76F274  mov  al,[0x76F2A8] ; push       -> [ebp+0x0C] flags  (image byte = 0)
        //   76F27A  push 1                          -> [ebp+0x08] arg0 = true
        //   76F27C  mov  ecx,edi ; xor edx,edx      ; rawDamage, target = NIL
        //   76F282  call sub_76FE44                 ; -> ident 0x27C1 = 10177 @0x76FEC4
        //   76F287  mov  eax,3 ; call sub_403B4C    ; Random(3)
        //   76F293  inc  ecx ; call [ebx+0x3C]      ; TrainSkill(magic, Random(3)+1)
        // sub_76FE44's slot map is confirmed from its own body @0x76FE80-0x76FEC4
        // ([+0x24]=skillId, [+0x20]=X, [+0x1C]=Y, [+0x18]=delay, [+0x14]=range,
        // [+0x10]=category, [+0x0C]=flags, [+0x08]=arg0) and cross-checked against
        // the already-audited magIdx 1/5 producer @0x76E403-0x76E437, which pushes
        // range=2/category=1 exactly as QueueNativeMagicProducerEffect does.
        // NOTE the `push 3` is the dispatchCategory, NOT a radius — the radius
        // slot is [+0x14] and is 1, matching g_Config.nFireBoomRage /
        // nSnowWindRange defaults. Nothing lands at cast time; the gather and the
        // per-target resolve happen 600 ms later inside the 10177 receiver
        // (ApplyNativeAreaMagicEffect), so targets can still walk out of the blast
        // and the damage takes the category-3 path rather than legacy RM_MAGSTRUCK.
        private static void QueueNativeAreaBlast(TPlayObject PlayObject,
            TUserMagic UserMagic, short nTargetX, short nTargetY)
        {
            int rawDamage = PlayObject.GetAttackPower(
                TPlayObject.CalculateNativeMagicProducerSkillPower(UserMagic) +
                    HUtil32.LoWord(PlayObject.m_WAbil.MC),
                HUtil32.HiWord(PlayObject.m_WAbil.MC) -
                    HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1);
            PlayObject.QueueNativeMagicEffect(3, null, rawDamage,
                UserMagic.MagicInfo.wMagicID, nTargetX, nTargetY, 1, true, 0,
                MagicDamageContext.Capture(UserMagic), 600);
            // Native trains unconditionally here (@0x76F287-0x76F29A), outside
            // the DoSpell tail's `btLevel < 3 && boTrain` gate.
            PlayObject.TrainNativeMagicProducer(UserMagic,
                M2Share.RandomNumber.Random(3) + 1);
        }

        private bool MagBigExplosion(TBaseObject BaseObject, int nPower, int nX, int nY, int nRage)
        {
            var result = false;
            IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
            BaseObject.GetMapBaseObjects(BaseObject.m_PEnvir, nX, nY, nRage, BaseObjectList);
            for (var i = 0; i < BaseObjectList.Count; i++)
            {
                var TargeTBaseObject = BaseObjectList[i];
                if (BaseObject.IsProperTarget(TargeTBaseObject))
                {
                    BaseObject.SetTargetCreat(TargeTBaseObject);
                    TargeTBaseObject.SendMsg(BaseObject, Grobal2.RM_MAGSTRUCK, 0, nPower, 0, 0, "");
                    result = true;
                }
            }
            BaseObjectList.Clear();
            BaseObjectList = null;
            return result;
        }

        private bool MagElecBlizzard(TBaseObject BaseObject, int nPower)
        {
            var result = false;
            IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
            BaseObject.GetMapBaseObjects(BaseObject.m_PEnvir, BaseObject.m_nCurrX, BaseObject.m_nCurrY, M2Share.g_Config.nElecBlizzardRange, BaseObjectList);
            for (var i = 0; i < BaseObjectList.Count; i++)
            {
                var TargeTBaseObject = BaseObjectList[i];
                int nPowerPoint;
                if (TargeTBaseObject.m_btLifeAttrib != Grobal2.LA_UNDEAD)
                {
                    nPowerPoint = nPower / 10;
                }
                else
                {
                    nPowerPoint = nPower;
                }
                if (BaseObject.IsProperTarget(TargeTBaseObject))
                {
                    TargeTBaseObject.SendMsg(BaseObject, Grobal2.RM_MAGSTRUCK, 0, nPowerPoint, 0, 0, "");
                    result = true;
                }
            }
            BaseObjectList.Clear();
            BaseObjectList = null;
            return result;
        }

        private int MagMakeHolyCurtain(TBaseObject BaseObject, int nPower, short nX, short nY)
        {
            var result = 0;
            if (BaseObject.m_PEnvir.CanWalk(nX, nY, true))
            {
                IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
                MagicEvent MagicEvent = null;
                BaseObject.GetMapBaseObjects(BaseObject.m_PEnvir, nX, nY, 1, BaseObjectList);
                for (var i = 0; i < BaseObjectList.Count; i++)
                {
                    var TargeTBaseObject = BaseObjectList[i];
                    if (TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL && M2Share.RandomNumber.Random(4) + (BaseObject.m_Abil.Level - 1) > TargeTBaseObject.m_Abil.Level && TargeTBaseObject.m_Master == null)
                    {
                        TargeTBaseObject.OpenHolySeizeMode(nPower * 1000);
                        if (MagicEvent == null)
                        {
                            MagicEvent = new MagicEvent
                            {
                                BaseObjectList = new List<TBaseObject>(),
                                dwStartTick = HUtil32.GetTickCount(),
                                dwTime = nPower * 1000
                            };
                        }
                        MagicEvent.BaseObjectList.Add(TargeTBaseObject);
                        result++;
                    }
                    else
                    {
                        result = 0;
                    }
                }
                BaseObjectList = null;
                if (result > 0 && MagicEvent != null && MagicEvent.Events != null && MagicEvent.Events.Length >= 8)
                {
                    var HolyCurtainEvent = new HolyCurtainEvent(BaseObject.m_PEnvir, nX - 1, nY - 2, Grobal2.ET_HOLYCURTAIN, nPower * 1000);
                    M2Share.EventManager.AddEvent(HolyCurtainEvent);
                    MagicEvent.Events[0] = HolyCurtainEvent;
                    HolyCurtainEvent = new HolyCurtainEvent(BaseObject.m_PEnvir, nX + 1, nY - 2, Grobal2.ET_HOLYCURTAIN, nPower * 1000);
                    M2Share.EventManager.AddEvent(HolyCurtainEvent);
                    MagicEvent.Events[1] = HolyCurtainEvent;
                    HolyCurtainEvent = new HolyCurtainEvent(BaseObject.m_PEnvir, nX - 2, nY - 1, Grobal2.ET_HOLYCURTAIN, nPower * 1000);
                    M2Share.EventManager.AddEvent(HolyCurtainEvent);
                    MagicEvent.Events[2] = HolyCurtainEvent;
                    HolyCurtainEvent = new HolyCurtainEvent(BaseObject.m_PEnvir, nX + 2, nY - 1, Grobal2.ET_HOLYCURTAIN, nPower * 1000);
                    M2Share.EventManager.AddEvent(HolyCurtainEvent);
                    MagicEvent.Events[3] = HolyCurtainEvent;
                    HolyCurtainEvent = new HolyCurtainEvent(BaseObject.m_PEnvir, nX - 2, nY + 1, Grobal2.ET_HOLYCURTAIN, nPower * 1000);
                    M2Share.EventManager.AddEvent(HolyCurtainEvent);
                    MagicEvent.Events[4] = HolyCurtainEvent;
                    HolyCurtainEvent = new HolyCurtainEvent(BaseObject.m_PEnvir, nX + 2, nY + 1, Grobal2.ET_HOLYCURTAIN, nPower * 1000);
                    M2Share.EventManager.AddEvent(HolyCurtainEvent);
                    MagicEvent.Events[5] = HolyCurtainEvent;
                    HolyCurtainEvent = new HolyCurtainEvent(BaseObject.m_PEnvir, nX - 1, nY + 2, Grobal2.ET_HOLYCURTAIN, nPower * 1000);
                    M2Share.EventManager.AddEvent(HolyCurtainEvent);
                    MagicEvent.Events[6] = HolyCurtainEvent;
                    HolyCurtainEvent = new HolyCurtainEvent(BaseObject.m_PEnvir, nX + 1, nY + 2, Grobal2.ET_HOLYCURTAIN, nPower * 1000);
                    M2Share.EventManager.AddEvent(HolyCurtainEvent);
                    MagicEvent.Events[7] = HolyCurtainEvent;
                    M2Share.UserEngine.m_MagicEventList.Add(MagicEvent);
                }
                else
                {
                    if (MagicEvent == null) return result;
                    MagicEvent.BaseObjectList = null;
                    MagicEvent = null;
                }
            }
            return result;
        }

        private bool MagMakeGroupTransparent(TBaseObject BaseObject, int nX, int nY, int nHTime)
        {
            var result = false;
            IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
            BaseObject.GetMapBaseObjects(BaseObject.m_PEnvir, nX, nY, 1, BaseObjectList);
            for (var i = 0; i < BaseObjectList.Count; i++)
            {
                var TargeTBaseObject = BaseObjectList[i];
                if (BaseObject.IsProperFriend(TargeTBaseObject))
                {
                    if (TargeTBaseObject.m_wStatusTimeArr[Grobal2.STATE_TRANSPARENT] == 0)
                    {
                        TargeTBaseObject.SendDelayMsg(TargeTBaseObject, Grobal2.RM_TRANSPARENT, 0, nHTime, 0, 0, "", 800);
                        result = true;
                    }
                }
            }
            BaseObjectList.Clear();
            BaseObjectList = null;
            return result;
        }

        
        
        
        
        
        
        
        
        
        
        private bool MabMabe(TBaseObject BaseObject, TBaseObject TargeTBaseObject, int nPower, int nLevel, int nTargetX, int nTargetY)
        {
            var result = false;
            if (BaseObject.MagCanHitTarget(BaseObject.m_nCurrX, BaseObject.m_nCurrY, TargeTBaseObject))
            {
                if (BaseObject.IsProperTarget(TargeTBaseObject))
                {
                    if (TargeTBaseObject.m_nAntiMagic <= M2Share.RandomNumber.Random(10) && Math.Abs(TargeTBaseObject.m_nCurrX - nTargetX) <= 1 && Math.Abs(TargeTBaseObject.m_nCurrY - nTargetY) <= 1)
                    {
                        BaseObject.SendDelayMsg(BaseObject, Grobal2.RM_DELAYMAGIC, (short)(nPower / 3), HUtil32.MakeLong(nTargetX, nTargetY), 2, TargeTBaseObject.ObjectId, "", 600);
                        if (M2Share.RandomNumber.Random(2) + (BaseObject.m_Abil.Level - 1) > TargeTBaseObject.m_Abil.Level)
                        {
                            var nLv = BaseObject.m_Abil.Level - TargeTBaseObject.m_Abil.Level;
                            if (M2Share.RandomNumber.Random(M2Share.g_Config.nMabMabeHitRandRate) < HUtil32._MAX(M2Share.g_Config.nMabMabeHitMinLvLimit, nLevel * 8 - nLevel + 15 + nLv))
                            {
                                if (M2Share.RandomNumber.Random(M2Share.g_Config.nMabMabeHitSucessRate) < nLevel * 2 + 4)
                                {
                                    if (TargeTBaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT)
                                    {
                                        BaseObject.SetPKFlag(BaseObject);
                                        BaseObject.SetTargetCreat(TargeTBaseObject);
                                    }
                                    TargeTBaseObject.SetLastHiter(BaseObject);
                                    nPower = TargeTBaseObject.GetMagStruckDamage(BaseObject, nPower);
                                    BaseObject.SendDelayMsg(BaseObject, Grobal2.RM_DELAYMAGIC, (short)nPower, HUtil32.MakeLong(nTargetX, nTargetY), 2, TargeTBaseObject.ObjectId, "", 600);
                                    if (!TargeTBaseObject.m_boUnParalysis)
                                    {
                                        
                                        TargeTBaseObject.SendDelayMsg(BaseObject, Grobal2.RM_POISON, Grobal2.POISON_STONE, nPower / M2Share.g_Config.nMabMabeHitMabeTimeRate + M2Share.RandomNumber.Random(nLevel), BaseObject.ObjectId, nLevel, "", 650);
                                    }
                                    result = true;
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }

        // Native 神兽 producer sub_76EE7C @0x76EE98-0x76EEB8 — byte-identical
        // shape to sub_76EDFC (see MagMakeSlave): `push 1 / push 0xD2F00 /
        // push 0 / push 0xA`, then `sub_4C896C -> cl` = the EFFECTIVE magic
        // level, `mov edx,0x76EEEC` = "神兽" (len@0x76EEE8 = 4), `call [esi+0xEC]`.
        private bool MagMakeSinSuSlave(TPlayObject PlayObject, TUserMagic UserMagic)
        {
            var result = false;
            if (!PlayObject.CheckServerMakeSlave())
            {
                var sMonName = M2Share.g_Config.sDragon;
                int nMakelevel = TPlayObject
                    .GetNativeMagicProducerEffectiveLevel(UserMagic);
                int nExpLevel = nMakelevel;
                var nCount = M2Share.g_Config.nDragonCount;
                var dwRoyaltySec = 10 * 24 * 60 * 60;
                for (var i = M2Share.g_Config.DragonArray.GetLowerBound(0); i <= M2Share.g_Config.DragonArray.GetUpperBound(0); i++)
                {
                    if (M2Share.g_Config.DragonArray[i].nHumLevel == 0)
                    {
                        break;
                    }
                    if (PlayObject.m_Abil.Level >= M2Share.g_Config.DragonArray[i].nHumLevel)
                    {
                        sMonName = M2Share.g_Config.DragonArray[i].sMonName;
                        nExpLevel = M2Share.g_Config.DragonArray[i].nLevel;
                        nCount = M2Share.g_Config.DragonArray[i].nCount;
                    }
                }
                if (PlayObject.MakeSlave(sMonName, nMakelevel, nExpLevel, nCount, dwRoyaltySec) != null)
                {
                    result = true;
                }
                else
                {
                    PlayObject.RecallSlave(sMonName);
                }
            }
            return result;
        }

        // Native 变异骷髅 producer sub_76EDFC @0x76EE0F-0x76EE3E:
        //   76EE0F  mov  dx,0x28A1                 ; RM_10401 = CheckServerMakeSlave
        //   76EE15  call sub_7661E8 ; test al,al ; jne ret
        //   76EE1E  push 1                         -> [ebp+0x14] = nMaxMob
        //   76EE20  push 0xD2F00                   -> [ebp+0x10] = royaltySec (=864000 = 10 d)
        //   76EE25  push 0                         -> [ebp+0x0C] = a Boolean flag
        //   76EE27  push 0xA                       -> [ebp+0x08] = a DWORD percent
        //   76EE29  call sub_4C896C                ; the EFFECTIVE magic level
        //   76EE31  mov  cl,al                     ; ecx = effective level
        //   76EE35  mov  edx,0x76EE70              ; "变异骷髅" (len@0x76EE6C = 8)
        //   76EE3E  call [esi+0xEC]                ; TPlayer.MakeSlave = sub_6CB070
        // Callee sub_6CB070 (`ret 0x10`) writes ecx to BOTH slave-level bytes:
        //   6CB2F9  mov  al,byte [ebp-8]           ; = ecx = effective level
        //   6CB2FC  mov  byte [esi+0x483],al       ; m_btSlaveMakeLevel
        //   6CB302  mov  byte [esi+0x482],al       ; m_btSlaveExpLevel  (SAME value)
        //   6CB308  mov  eax,dword [ebp+8]         ; the literal 10
        //   6CB30B  mov  dword [esi+0x48C],eax     ; a DWORD percentage field
        // Field ids: sub_71F3D0 @0x71F427-0x71F442 is GainSlaveExp
        //   (`movzx eax,[+0x482]; movzx edx,[+0x483]; add edx,edx; inc edx;
        //     cmp eax,edx; jae skip; inc byte [+0x482]`), matching C#'s
        //   `if (m_btSlaveExpLevel < m_btSlaveMakeLevel*2+1) m_btSlaveExpLevel++`;
        //   and TMonster.RecalcAbilitys = sub_71DF70 (VMT+0x8C) reads +0x482
        //   @0x71DFB3/0x71DFD7/0x71DFF8 for the HP/DC scaling. +0x48C is NOT a
        //   level: sub_71E50C @0x71E6FD-0x71E717 does `HP = HP * [+0x48C] / 100`.
        // => the literal 10 is a PERCENT, not nExpLevel. The only divergence here
        //    is raw btLevel vs the effective level (btLevel + NativeLevelBonus,
        //    clamped to btTrainLv), so items/buffs granting +skill level were
        //    being dropped from both summon levels.
        private bool MagMakeSlave(TPlayObject PlayObject, TUserMagic UserMagic)
        {
            var result = false;
            if (!PlayObject.CheckServerMakeSlave())
            {
                var sMonName = M2Share.g_Config.sSkeleton;
                int nMakeLevel = TPlayObject
                    .GetNativeMagicProducerEffectiveLevel(UserMagic);
                int nExpLevel = nMakeLevel;
                var nCount = M2Share.g_Config.nSkeletonCount;
                var dwRoyaltySec = 10 * 24 * 60 * 60;//叛变时间
                for (var i = M2Share.g_Config.SkeletonArray.GetLowerBound(0); i <= M2Share.g_Config.SkeletonArray.GetUpperBound(0); i++)
                {
                    if (M2Share.g_Config.SkeletonArray[i].nHumLevel == 0)
                    {
                        break;
                    }
                    if (PlayObject.m_Abil.Level >= M2Share.g_Config.SkeletonArray[i].nHumLevel)
                    {
                        sMonName = M2Share.g_Config.SkeletonArray[i].sMonName;
                        nExpLevel = M2Share.g_Config.SkeletonArray[i].nLevel;
                        nCount = M2Share.g_Config.SkeletonArray[i].nCount;
                    }
                }
                if (PlayObject.MakeSlave(sMonName, nMakeLevel, nExpLevel, nCount, dwRoyaltySec) != null)
                {
                    result = true;
                }
            }
            return result;
        }

        private bool MagMakeClone(TPlayObject PlayObject, TUserMagic UserMagic)
        {
            var playCloneObject = new TPlayCloneObject(PlayObject);
            return true;
        }

        // Native 天使 producer sub_76EEF4 @0x76EF16-0x76EF36 — same shape again:
        // `push 1 / push 0xD2F00 / push 0 / push 0xA`, `sub_4C896C -> cl`
        // (EFFECTIVE level), `mov edx,0x76EF74`, `call [esi+0xEC]`.
        private bool MagMakeAngelSlave(TPlayObject PlayObject, TUserMagic UserMagic)
        {
            var result = false;
            if (!PlayObject.CheckServerMakeSlave())
            {
                var sMonName = M2Share.g_Config.sAngel;
                int nMakeLevel = TPlayObject
                    .GetNativeMagicProducerEffectiveLevel(UserMagic);
                int nExpLevel = nMakeLevel;
                var dwRoyaltySec = 10 * 24 * 60 * 60;
                if (PlayObject.MakeSlave(sMonName, nMakeLevel, nExpLevel, 1, dwRoyaltySec) != null)
                {
                    result = true;
                }
            }
            return result;
        }
    }
}
