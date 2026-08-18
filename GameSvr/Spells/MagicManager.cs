using SystemModule;
using GameSvr.Plugins;

namespace GameSvr
{
    public class MagicManager
    {
        internal static void SendNativeSpell(TBaseObject source,
            TUserMagic userMagic, short targetX, short targetY)
        {
            // sub_769258: Param=X, Tag=Y, Series=MakeWord(effect,type),
            // followed by the magic id and effective level as two dwords.
            source.SendRefMsg(Grobal2.RM_SPELL,
                HUtil32.MakeWord(userMagic.MagicInfo.btEffect,
                    userMagic.MagicInfo.btEffectType),
                targetX, targetY, userMagic.MagicInfo.wMagicID,
                string.Empty, new NativeSpellRelayPayload(
                    TPlayObject.GetNativeMagicProducerEffectiveLevel(userMagic)));
        }

        internal static void SendNativeMagicFire(TBaseObject source,
            TUserMagic userMagic, short targetX, short targetY,
            TBaseObject target)
        {
            // sub_76920C: Param=X, Tag=Y, Series=MakeWord(type,effect),
            // followed by the target id and effective level as two dwords.
            source.SendRefMsg(Grobal2.RM_MAGICFIRE,
                HUtil32.MakeWord(userMagic.MagicInfo.btEffectType,
                    userMagic.MagicInfo.btEffect),
                targetX, targetY, target == null ? 0 : target.ObjectId,
                string.Empty, new NativeMagicFireRelayPayload(
                    TPlayObject.GetNativeMagicProducerEffectiveLevel(userMagic)));
        }

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

        
        
        
        
        
        // This is the CM_SPELL outer-dispatch gate, not a classification of
        // every skill with a warrior job requirement. Native skill 38 is
        // deliberately absent because it reaches the generic spell dispatcher.
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
            return (ushort)unchecked(UserMagic.MagicInfo.wPower + M2Share.RandomNumber.Random(UserMagic.MagicInfo.wMaxPower - UserMagic.MagicInfo.wPower));
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
            M2Share.AddNativeGameDataLog(PlayObject, 0x0A,
                StdItem == null ? string.Empty : StdItem.Name,
                charm.MakeIndex, 1, "持久耗尽");
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
            SendNativeSpell(PlayObject, UserMagic, nTargetX, nTargetY);
            if (TargeTBaseObject != null && TargeTBaseObject.m_boDeath)
            {
                TargeTBaseObject = null;
            }
            var boTrain = false;
            var boSpellFail = false;
            var boSpellFire = true;
            // A7 槽②（战神 0x76EA14 `B8 03 00 00 00 mov eax,3`）：激光命中后训练技能
            // 所用 Random 的实参。默认 3（原生 Random(3)+1），仅 SKILL_SHOOTLIGHTEN
            // 段会按 S(1,82)/开关改写——S(1,82) 补丁只钩激光生产者 0x76EA14，故其余
            // 法术的训练点保持原生 Random(3)+1，绝不受影响。
            var laserTrainRandomArg = Plugins.YanshenLaserSlots.NativeTrainRandom;
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
                            // POIS-27 @0x6ED9C6 call [edi+0x110] / @0x6ED9E0 call [edi+0x114] — no
                            // Random(antiPoison+7) in DoSpell or in appliers sub_76E540/sub_76E620
                            // (only IsProperTarget @0x767498 @0x76E561/@0x76E63F before SendDelayMsg).
                            switch (StdItem.Shape)
                            {
                                case 1:
                                    // 眼神「中毒时间上限」：绿毒施加器 sub_76E540 在 0x76E5CE
                                    // 装 trampoline，把这个 nParam1 上钳到 atoi(中毒时间上限_秒)。
                                    // 只钳时长，不动 nParam3。见 YanshenPoisonTimeCap。
                                    nPower = (ushort)YanshenPoisonTimeCap.Cap(DoSpell_GetPower13(UserMagic, 40) + DoSpell_GetRPow(PlayObject.m_WAbil.SC) * 2);// 中毒类型 - 绿毒
                                    TargeTBaseObject.SendDelayMsg(PlayObject, Grobal2.RM_POISON, Grobal2.POISON_DECHEALTH, nPower, PlayObject.ObjectId, HUtil32.Round(UserMagic.btLevel / 3.0 * ((double)nPower / M2Share.g_Config.nAmyOunsulPoint)), "", 1000);
                                    break;
                                case 2:
                                    // 红毒施加器 sub_76E620 的同名钳位在 0x76E675。
                                    nPower = (ushort)YanshenPoisonTimeCap.Cap(DoSpell_GetPower13(UserMagic, 30) + DoSpell_GetRPow(PlayObject.m_WAbil.SC) * 2);// 中毒类型 - 红毒
                                    TargeTBaseObject.SendDelayMsg(PlayObject, Grobal2.RM_POISON, Grobal2.POISON_DAMAGEARMOR, nPower, PlayObject.ObjectId, HUtil32.Round(UserMagic.btLevel / 3.0 * ((double)nPower / M2Share.g_Config.nAmyOunsulPoint)), "", 1000);
                                    break;
                            }
                            if (TargeTBaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT || TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                            {
                                boTrain = true;
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
                        else if (YanshenConfig12Behaviors.AntiPoisonAmuletFree(PlayObject))
                        {
                            // 免毒符 @0x6ED945：无 TPoisons 也走绿毒臂（native 0x6EE04B 成功路径）。
                            nPower = (ushort)YanshenPoisonTimeCap.Cap(
                                DoSpell_GetPower13(UserMagic, 40) +
                                DoSpell_GetRPow(PlayObject.m_WAbil.SC) * 2);
                            // POIS-27: same applier path as charm branch — no antiPoison RNG @0x6ED9C6.
                            TargeTBaseObject.SendDelayMsg(PlayObject, Grobal2.RM_POISON,
                                Grobal2.POISON_DECHEALTH, nPower, PlayObject.ObjectId,
                                HUtil32.Round(UserMagic.btLevel / 3.0 *
                                    ((double)nPower / M2Share.g_Config.nAmyOunsulPoint)),
                                "", 1000);
                            if (TargeTBaseObject.m_btRaceServer == Grobal2.RC_PLAYOBJECT ||
                                TargeTBaseObject.m_btRaceServer >= Grobal2.RC_ANIMAL)
                                boTrain = true;
                            PlayObject.SetTargetCreat(TargeTBaseObject);
                            boSpellFail = false;
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
                        // Native 0x76E9FD is `6A 08 push 8` (line span). The
                        // trampoline at 0x76EA07 patches the following `6A 01`
                        // arg0 slot (S(1,81) → mov al,[ebp+8]), not this 8.
                        const int laserSpan = 8;
                        PlayObject.m_PEnvir.GetNextPosition(PlayObject.m_nCurrX, PlayObject.m_nCurrY, n1C, laserSpan, ref nTargetX, ref nTargetY);
                        int laserLo = HUtil32.LoWord(PlayObject.m_WAbil.MC);
                        int laserHi = HUtil32.HiWord(PlayObject.m_WAbil.MC);
                        Plugins.YanshenSkillPatches.MainAttr(PlayObject,
                            SpellsDef.SKILL_SHOOTLIGHTEN, laserLo, laserHi,
                            out laserLo, out laserHi);
                        nPower = PlayObject.GetAttackPower(
                            DoSpell_GetPower(UserMagic) + laserLo,
                            (ushort)(laserHi - laserLo + 1));
                        nPower = (ushort)Plugins.YanshenSkillPatches.ScaleDamage(
                            PlayObject, SpellsDef.SKILL_SHOOTLIGHTEN, nPower);
                        if (PlayObject.MagPassThroughMagic(n14, n18, nTargetX, nTargetY, n1C, nPower, true) > 0)
                        {
                            boTrain = true;
                        }
                        // A7 槽②（S(1,82)，战神 0x76EA14）：激光命中后 TrainSkill 的
                        // Random 实参。原生在 beam（0x76EA0F call sub_76FE44）之后无条件
                        // 执行 `mov eax,N / call Random / inc ecx`（=Random(N)+1）。C# 把
                        // 该训练折叠到 DoMagic 尾部（下方 TrainSkill 处），故此处只求出 N，
                        // 由尾部执行 Random(N)+1；仅激光段改写，别的法术仍是 Random(3)+1。
                        laserTrainRandomArg = Plugins.YanshenLaserSlots.TrainRandomArg(PlayObject);
                        // A7 槽①（S(1,81)，战神 0x76EA07 arg0 低 8 位）——**故意不接**。
                        // 反汇编闭环：sub_76FE44 把 arg0 写进延迟魔法事件 0x27C1 的载荷
                        // struct[0x2C]（0x76FEA7 mov al,[ebp+8] → [ebp-8]）；但事件处理器
                        // 0x766D90 按 dispatchType=evt[2] 分派，激光生产者 0x76E9FF `push 2`
                        // 令激光恒为 type 2，其分支 0x766E16 及伤害函数 0x76DF5C 都**从不
                        // 读取 struct[0x2C]**（仅 type 1/5 分支 0x766DA5 的 0x76DF9 mov
                        // al,[ebx+0x2c] 会读）。故 arg0/S(1,81) 对激光是死参，接入任何
                        // C# 行为都会引入原生不存在的分歧——fail-closed 保持原生 push 1。
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
                    SendNativeMagicFire(PlayObject, UserMagic, nTargetX,
                        nTargetY, TargeTBaseObject);
                    boSpellFire = false;
                    if (MagSaceMove(PlayObject, UserMagic.btLevel))
                    {
                        boTrain = true;
                    }
                    break;
                case SpellsDef.SKILL_EARTHFIRE:
                {
                    var holdSec = DoSpell_GetScaledPower(UserMagic, 10) +
                        (DoSpell_GetRPow(PlayObject.m_WAbil.MC) >> 1);
                    if (YanshenPangu2Patches.TryGetFireWallHoldSeconds(out var cfgSec))
                        holdSec = cfgSec;
                    if (MagMakeFireCross(PlayObject, UserMagic,
                            PlayObject.GetAttackPower(
                                DoSpell_GetPower(UserMagic) +
                                HUtil32.LoWord(PlayObject.m_WAbil.MC),
                                HUtil32.HiWord(PlayObject.m_WAbil.MC) -
                                HUtil32.LoWord(PlayObject.m_WAbil.MC) + 1),
                            holdSec, nTargetX, nTargetY) > 0)
                    {
                        boTrain = true;
                    }
                    break;
                }
                case SpellsDef.SKILL_FIREBOOM:
                    QueueNativeAreaBlast(PlayObject, UserMagic, nTargetX,
                        nTargetY);
                    break;
                case SpellsDef.SKILL_LIGHTFLOWER:
                    {
                        int hlLo = HUtil32.LoWord(PlayObject.m_WAbil.MC);
                        int hlHi = HUtil32.HiWord(PlayObject.m_WAbil.MC);
                        Plugins.YanshenSkillPatches.MainAttr(PlayObject,
                            SpellsDef.SKILL_LIGHTFLOWER, hlLo, hlHi,
                            out hlLo, out hlHi);
                        nPower = PlayObject.GetAttackPower(
                            DoSpell_GetPower(UserMagic) + hlLo, hlHi - hlLo + 1);
                        if (MagElecBlizzard(PlayObject, nPower,
                                Plugins.YanshenSkillPatches.RangeByte(PlayObject,
                                    SpellsDef.SKILL_LIGHTFLOWER,
                                    M2Share.g_Config.nElecBlizzardRange),
                                Plugins.YanshenSkillPatches.HellLightDivisor(
                                    PlayObject)))
                        {
                            boTrain = true;
                        }
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
                case SpellsDef.SKILL_213:
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
                    PlayObject.TryProduceNativeMagic43(UserMagic);
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
                            nPower = (ushort)Plugins.YanshenSkillPatches.BloodSuck(
                                PlayObject, nPower);
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
                // Magic IDs 140, 141, 145, 146, 149, 150: Native DEFAULT
                // convergence handlers - all six route to the default sink at
                // 0x6EE04B in sub_6ED62C. Dispatch proof (2026-08-13, raw read
                // of flat_image.bin): ids 112..150 reach 0x6ED841
                // `add eax,-0x75` / `cmp eax,0xB` / `ja 0x6EE04B`, so only
                // 117..128 have TABLE3 slots (@0x6ED854) and 129..150 all fall
                // to DEFAULT. Native behavior (audit Magic140_141_145_146Check):
                //   - boSpellFail remains FALSE (entry default at 0x6ED63C)
                //   - DoSpell returns TRUE at 0x6EE0C3
                //   - Sends RM_MAGICFIRE (0x27E) effect packet
                //   - MP deducted at entry (0x6ED65E, before dispatch)
                //   - No gameplay effect, no skill training
                // NOTE: earlier comments here attributed 0x6EDEC1 to id 149 and
                // 0x6EDF01 to id 150. Both were misattributions - TABLE3 slot
                // reads prove 0x6EDEC1 = id 117 and 0x6EDF01 = id 118 (see
                // staging/magic_dispatch_map_FINAL_20260813.md). Id 150 used to
                // hard-reject here on the strength of that wrong comment; the
                // native default sink SUCCEEDS, so it now breaks like its
                // siblings.
                case SpellsDef.SKILL_140:
                case SpellsDef.SKILL_141:
                case SpellsDef.SKILL_145:
                case SpellsDef.SKILL_146:
                case SpellsDef.SKILL_149:
                case SpellsDef.SKILL_150:
                    break;
                // id 62 @0x6EDC71: 30 秒门 (+0x50C) -> 护身符消耗 sub_73EA20(cl=1,
                // edx=2000) -> boSpellFail:=0 -> VMT+0x124 = sub_76EEF4 "圣兽" 造宠
                // (返回值丢弃)。消耗在造宠之前且无条件, 造宠失败既不退也不提示。
                // 逐地址证据与三处修正见 TryProduceNativeMagic62 上方注释。
                // Verified 2026-08-14 against flat_image.bin.
                case SpellsDef.SKILL_62:
                    boSpellFail = !TryProduceNativeMagic62(PlayObject, UserMagic);
                    break;
                // ids 59 and 63 are ONE native handler, not two: TABLE2 slot for
                // 59 @0x6ED81D and for 63 @0x6ED82D both hold the dword
                // 0x6EDD27 (read raw from the image), so they share a single
                // instruction stream. The trampoline @0x6EDD27 pushes
                // `[ebp+0xC]` (targetY) then the literal 0x258 = 600 and calls
                // sub_76F33C, discarding the result (`jmp 0x6EE04B` @0x6EDD3C
                // without touching [ebp-6]) - so this id can never hard-reject.
                case SpellsDef.SKILL_59:
                case SpellsDef.SKILL_63:
                    TryProduceNativeMagic59(PlayObject, UserMagic, nTargetX,
                        nTargetY);
                    break;
                // id 111 @0x6EDE4F: 冰眼巨魔 summon with 10-min cooldown
                // Native sub_74633C: VMT+0x128 call, checks self+0x510 cooldown,
                // summons with hpAfterSlave=10%, royalty=300s
                case SpellsDef.SKILL_111:
                    boSpellFail = !PlayObject.TryActivateNativeSkill111IceEyeTrollSummon(
                        UserMagic);
                    break;
                // id 151 @0x6EDD70 -> sub_745A20, id 154 @0x6EDD83 ->
                // sub_74588C; both `xor al,1` / `mov [ebp-6],al`, so a false
                // return is a hard reject. VMT+0x1F4 is NOT a sub-dispatcher:
                // it is sub_748288, the keyed cooldown query on the obj+0x504
                // TList (see TBaseObject.NativeColdTime.cs), which those two
                // bodies call with keys 0x97 / 0x9A. The comment that used to
                // stand here claimed the slot was an unreversed dispatcher and
                // that both TryActivateNativeSkill bodies were fail-closed;
                // neither was true of the code it sat above.
                case SpellsDef.SKILL_151:
                    boSpellFail = !PlayObject.TryActivateNativeSkill151(UserMagic);
                    break;
                case SpellsDef.SKILL_154:
                    boSpellFail = !PlayObject.TryActivateNativeSkill154(UserMagic);
                    break;
                // ids 117, 125, 126, 127 are constant-TRUE stubs. Each
                // trampoline calls a real function whose entire body is
                // `push ebp; mov ebp,esp; mov al,1; pop ebp; ret N`:
                //   117 -> sub_6EEE34 raw 558becb0015dc20400
                //   125 -> sub_6EEE40 raw 558becb0015dc20400
                //   126 -> sub_6EEE4C raw 558becb0015dc20800
                //   127 -> sub_6EEE58 raw 558becb0015dc20400
                // The `xor al,1` then makes boSpellFail = 0, so DoSpell returns
                // TRUE and the tail broadcasts the 0x27E effect packet. Net
                // observable behaviour is identical to the convergence-routed
                // ids: mana spent, effect packet sent, no gameplay effect.
                // They are listed explicitly rather than left to fall through
                // so that the FALSE-returning stubs next door cannot be
                // "tidied" into the same arm later — the two groups send
                // OPPOSITE packets and conflating them inverts six ids.
                case SpellsDef.SKILL_117:
                case SpellsDef.SKILL_125:
                case SpellsDef.SKILL_126:
                case SpellsDef.SKILL_127:
                    break;
                // ids 231 and 236 also call constant-FALSE stubs, but their
                // trampolines DISCARD the result instead of storing it, so
                // unlike 118/128 they still succeed:
                //   231 @0x6EDE66: amulet gate sub_73E93C(edx=1); on failure
                //     `mov [ebp-6],1` @0x6EDE9E = hard reject. On success it
                //     probes bodyState bit 0x58 (sub_772960) and calls
                //     sub_76F8BC, which reads byte [0x76F8CC] and then
                //     CLOBBERS it with `xor eax,eax` @0x76F8C4 — so it is
                //     unconditionally FALSE regardless of that data byte. The
                //     `jne 0x6EE04B` @0x6EDE8E is therefore never taken and
                //     control falls to `xor eax,eax` / `mov [ebp+8],eax`, i.e.
                //     the target is cleared and the id succeeds. Native burns
                //     the charm first, so this is a charm-consuming no-op.
                //   236 @0x6EDFB4: calls sub_76F8B8, whose whole body is
                //     `xor eax,eax; ret` (raw 33c0c3 @0x76F8B8). Result used
                //     ONLY to decide whether to clear the target; [ebp-6] is
                //     never written, so it always returns TRUE.
                // Both therefore end as: mana spent, target cleared, 0x27E
                // sent, TRUE. Clearing the target changes which effect-packet
                // arm the tail takes (@0x6EE091 with target = nil), so it is
                // observable and must be replicated.
                case SpellsDef.SKILL_231:
                    boSpellFail = true;
                    if (Magic.CheckAmulet(PlayObject, 1, 1, ref nAmuletIdx))
                    {
                        Magic.UseAmulet(PlayObject, 1, 1, ref nAmuletIdx);
                        TargeTBaseObject = null;
                        boSpellFail = false;
                    }
                    break;
                case SpellsDef.SKILL_236:
                    TargeTBaseObject = null;
                    break;
                // id 232 @0x6EDEA7 calls sub_76F8A8 and throws the result away
                // (`e9 8a 01 00 00  jmp 0x6EE04B` at 0x6EDEBC, no write to
                // [ebp-6]), so it cannot reject and it does not clear the
                // target either -- one step further from 231/236, which at
                // least consume the return value.
                //   0076F8A8  55 8b ec           push ebp; mov ebp,esp
                //   0076F8AB  a0 b4 f8 76 00     mov al,[0x76F8B4]
                //   0076F8B0  5d c2 08 00        pop ebp; ret 8
                //   0076F8B4  00 00 00 00        the data byte, value 0
                // A whole-image dword scan for 0x76F8B4 returns exactly one
                // hit, 0x76F8AC, which is that instruction's own operand, so
                // nothing ever writes it. Listed explicitly because an id with
                // no case looks indistinguishable from an unexamined one, and
                // the 291-320 band already got rejected once on that guess.
                case SpellsDef.SKILL_232:
                    break;
                // ids 118 and 128 are constant-FALSE stubs whose result IS
                // stored, so they are hard rejects - NOT silent no-ops.
                //   118: TR @0x6EDF01 calls sub_6EEE28, whose whole body is
                //        `push ebp; mov ebp,esp; xor eax,eax; pop ebp; ret 8`
                //        (raw 558bec33c05dc20800 @0x6EEE28).
                //   128: TR @0x6EDF89 calls sub_6EEE64, whole body
                //        `push ebp; mov ebp,esp; push ecx; mov [ebp-4],edx;
                //         xor eax,eax; pop ecx; pop ebp; ret`
                //        (raw 558bec518955fc33c0595dc3 @0x6EEE64).
                // Both trampolines then do `mov [ebp-7],al` / `mov al,[ebp-7]` /
                // `xor al,1` / `mov [ebp-6],al` (@0x6EDF15 and @0x6EDFA4), so
                // boSpellFail becomes 1 and DoSpell returns FALSE. The caller
                // @0x6BCCBA then sends ident 0x27F = RM_MAGICFIREFAIL. Mana was
                // already spent at 0x6ED65E, and no 0x27E effect packet is sent.
                // TABLE3 slot proof: dword[0x6ED854+1*4] = 0x6EDF01 (id 118),
                // dword[0x6ED854+11*4] = 0x6EDF89 (id 128).
                case SpellsDef.SKILL_118:
                case SpellsDef.SKILL_128:
                    boSpellFail = true;
                    break;
                // ids 288, 289, 315, 316 dispatch through TPlayer VMT slots
                // whose targets are genuinely empty, and the trampolines store
                // the inverted result, so all four are HARD REJECTS.
                // Slots resolved on TPlayer VMT V = 0x6AC8C8 after passing the
                // SelfPtr self-check dword[V-0x4C] == V (required, because
                // adjacent VMTs are contiguous and a wrong base still yields a
                // plausible pointer):
                //   +0x224 -> 0x6ED26C  raw 33c0c390        xor eax,eax; ret
                //   +0x228 -> 0x6ED270  raw 558bec33c05d..  empty w/ frame, ret 4
                //   +0x234 -> 0x6ED290  raw 33c0c390        xor eax,eax; ret
                //   +0x238 -> 0x6ED294  raw 33c0c390        xor eax,eax; ret
                // Each trampoline (@0x6EDFF2, @0x6EE008, @0x6EE024, @0x6EE03A)
                // does `xor al,1` / `mov [ebp-6],al`, so boSpellFail = 1 and
                // DoSpell returns FALSE => the caller sends 0x27F. Note 289 is
                // an empty body WITH a stack frame (it takes one arg and
                // ignores it); that is a calling-convention difference, not a
                // real implementation, so it belongs with the other three.
                // TPlayer's only descendant, TGdMsgGMAgent (0x62EF8C),
                // inherits all four slots unchanged, so no override rescues
                // them for any object that can reach DoSpell.
                case SpellsDef.SKILL_288:
                case SpellsDef.SKILL_289:
                case SpellsDef.SKILL_315:
                case SpellsDef.SKILL_316:
                    boSpellFail = true;
                    break;
                // ids 66 and 67 are ONE native handler: TABLE2 slots 0x6ED839
                // and 0x6ED83D both hold 0x6EDE39, which calls sub_745744 and
                // inverts the result into [ebp-6] @0x6EDE47.
                case SpellsDef.SKILL_66:
                case SpellsDef.SKILL_67:
                    boSpellFail = !PlayObject.TryActivateNativeSkill66Or67(
                        UserMagic, TargeTBaseObject);
                    break;
                // id 167 @0x6EDEE1 -> 0x6EEE70. The trampoline stores the raw
                // result into boSpellFire (@0x6EDEF1) and its complement into
                // boSpellFail (@0x6EDEF9), so both flags move together.
                case SpellsDef.SKILL_167:
                    boSpellFire = PlayObject.TryActivateNativeSkill167Prison(
                        nTargetX, nTargetY);
                    boSpellFail = !boSpellFire;
                    break;
                // id 191 @0x6EDFCF -> TPlayer VMT+0x148 = 0x6EF340, result
                // inverted into [ebp-6] @0x6EDFED, so every refusal path in
                // that function is a hard reject.
                case SpellsDef.SKILL_191:
                    boSpellFail = !PlayObject.TryActivateNativeSkill191Freeze(
                        UserMagic, TargeTBaseObject);
                    break;
                case SpellsDef.SKILL_152:
                    boSpellFail = !PlayObject.TryActivateNativeSkill152(
                        UserMagic);
                    break;
                case SpellsDef.SKILL_153:
                    boSpellFail = !PlayObject.TryActivateNativeSkill153Shield(
                        UserMagic);
                    break;
                // ids 161 and 162 reach the DEFAULT sink, not a reject. The
                // 152..166 arm of the ladder is a three-step subtract chain and
                // neither id is one of its three landing pads:
                //   0x6ED89E  2d 98 00 00 00  sub eax,0x98   ; 152 -> 0x6EDD96
                //   0x6ED8A9  48              dec eax        ; 153 -> 0x6EDDA9
                //   0x6ED8B0  48              dec eax        ; 154 -> 0x6EDD83
                //   0x6ED8B7  e9 8f 07 00 00  jmp 0x6EE04B   ; everything else
                // The previous arm rejected both on the strength of a task
                // description with no VA attached; the sink leaves boSpellFail
                // at its 0x6ED6D9 zero, sends 0x27E and returns TRUE.
                case SpellsDef.SKILL_161:
                case SpellsDef.SKILL_162:
                    break;
                // Magic IDs 169 (0xA9), 170 (0xAA): Native DEFAULT convergence handlers.
                // Both route to 0x6EE04B in DoSpell (sub_6ED62C). Verified via dispatch
                // ladder trace (commit 26c0dd3):
                //   0x6ED891: cmp eax, 0xA7 (167); jg 0x6ED8BC  → 169/170 > 167, jump
                //   0x6ED8BC: sub eax, 0xBF (191); je handler_191  → 169/170 - 191 < 0, skip
                //   0x6ED8C7: sub eax, 0x16 (22); je handler_213  → negative, skip
                //   0x6ED8D0: jmp 0x6EE04B (DEFAULT convergence)
                // DEFAULT path sets result=TRUE (0x6EE0C3), sends RM_MAGICFIRE (0x27E),
                // returns success. No gameplay effect, no training. Silent success stubs.
                case SpellsDef.SKILL_169:
                case SpellsDef.SKILL_170:
                    break;
                // ids 171-174 take the same route 169/170 take, one arm down:
                //   0x6ED891  cmp eax,0xA7 (167)  -> above, so jg 0x6ED8BC
                //   0x6ED8BC  sub eax,0xBF (191)  -> not zero
                //   0x6ED8C7  sub eax,0x16 (213)  -> not zero
                //   0x6ED8D0  e9 76 07 00 00  jmp 0x6EE04B
                // The rejecting arm they used to share cited "per task
                // description" and a comparison at 0x6ED8A0 against 172; that
                // address is the middle of `sub eax,0x98` and no compare
                // against 172 exists anywhere in the ladder.
                case SpellsDef.SKILL_171:
                case SpellsDef.SKILL_172:
                case SpellsDef.SKILL_173:
                case SpellsDef.SKILL_174:
                    break;
                // ids 179 (0xB3) and 180 (0xB4) are convergence-routed no-ops.
                // DoSpell multi-level dispatch (sub_6ED62C) tiers above 151:
                // @0x6ED79A (151), @0x6ED884 (231), @0x6ED891 (167),
                // @0x6ED89E (152/153/154 subtract chain), @0x6ED8BC (191),
                // @0x6ED8C7 (213), @0x6ED8D0 (default sink). For ids 179/180:
                //   0x6ED79A: cmp eax, 0x97 (151) → 179 > 151, jmp 0x6ED884
                //   0x6ED884: cmp eax, 0xE7 (231) → 179 < 231, fall through
                //   0x6ED891: cmp eax, 0xA7 (167) → 179 > 167, jmp 0x6ED8BC
                //   0x6ED8BC: sub eax, 0xBF (191) → 179 != 191, fall through
                //   0x6ED8C7: sub eax, 0x16 (22) → (179-191) != 22, fall through
                //   0x6ED8D0: jmp 0x6EE04B → default convergence handler.
                // The convergence handler @0x6EE04B checks boSpellFail (initially
                // 0, set by @0x6ED63C `mov byte ptr [ebp-5],0`). Since no
                // intervening code sets the flag, both ids fall through to the
                // success path @0x6EE051-0x6EE0C3: send the 0x27E effect packet and
                // return TRUE (@0x6EE0C3: `mov byte ptr [ebp-5],1`). Mana was
                // already deducted in the DoSpell entry @0x6ED65C. Net effect: mana
                // spent, effect packet broadcast, no game state change.
                case SpellsDef.SKILL_179:
                case SpellsDef.SKILL_180:
                    break;
                // Magic IDs 291-314 and 317-320: DEFAULT convergence, silent
                // success. Dispatch proof (2026-08-13, raw read of
                // flat_image.bin, see staging/magic_dispatch_map_FINAL_20260813.md):
                // for id > 289 the ladder @0x6ED904 is
                //   sub eax,0x13B (315) / je 0x6EE024
                //   dec eax        (316) / je 0x6EE03A
                //   jmp 0x6EE04B          (everything else)
                // so ONLY 315/316 have real (reject) handlers - they live in
                // the evidence-backed arm above. The rest of the band falls to
                // the DEFAULT sink 0x6EE04B: boSpellFail stays 0, the 0x27E
                // effect packet is sent and DoSpell returns TRUE. The previous
                // arm here rejected the whole band on the claim that the
                // default sink "likewise rejects", which contradicts this
                // file's own 169/170 and 179/180 traces of the same sink.
                case SpellsDef.SKILL_291:
                case SpellsDef.SKILL_292:
                case SpellsDef.SKILL_293:
                case SpellsDef.SKILL_294:
                case SpellsDef.SKILL_295:
                case SpellsDef.SKILL_296:
                case SpellsDef.SKILL_297:
                case SpellsDef.SKILL_298:
                case SpellsDef.SKILL_299:
                case SpellsDef.SKILL_300:
                case SpellsDef.SKILL_301:
                case SpellsDef.SKILL_302:
                case SpellsDef.SKILL_303:
                case SpellsDef.SKILL_304:
                case SpellsDef.SKILL_305:
                case SpellsDef.SKILL_306:
                case SpellsDef.SKILL_307:
                case SpellsDef.SKILL_308:
                case SpellsDef.SKILL_309:
                case SpellsDef.SKILL_310:
                case SpellsDef.SKILL_311:
                case SpellsDef.SKILL_312:
                case SpellsDef.SKILL_313:
                case SpellsDef.SKILL_314:
                case SpellsDef.SKILL_317:
                case SpellsDef.SKILL_318:
                case SpellsDef.SKILL_319:
                case SpellsDef.SKILL_320:
                    break;
            }
            if (boSpellFail)
            {
                return result;
            }
            if (boSpellFire)
            {
                SendNativeMagicFire(PlayObject, UserMagic, nTargetX,
                    nTargetY, TargeTBaseObject);
            }
            if (UserMagic.btLevel < 3 && boTrain)
            {
                if (UserMagic.btLevel < UserMagic.MagicInfo.TrainLevel.Length && UserMagic.MagicInfo.TrainLevel[UserMagic.btLevel] <= PlayObject.m_Abil.Level)
                {
                    // 原生每条法术 DoSpell 尾都是 Random(3)+1（0x76EA14 mov eax,3 / call
                    // Random / inc）；laserTrainRandomArg 默认 3，仅激光段（A7 槽② S(1,82)）
                    // 改写，故此折叠尾对非激光法术仍是 Random(3)+1。
                    PlayObject.TrainSkill(UserMagic, M2Share.RandomNumber.Random(laserTrainRandomArg) + 1);
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
                                            // MagTamming sub_6ED2A4 @0x6ED528: RecalcAbilitys then
                                            // TList.Add [master+0x4FC] then SM 4469 (0x6F784C).
                                            BaseObject.NotifyNativeSlaveListChanged(joining: true, TargeTBaseObject);
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
                            // POIS-27 BLOCKED: native wMagicID 48 @0x6EDE26 calls sub_76FBBC, not
                            // [edi+0x110]/[edi+0x114]; no [target+0x26C]+7 Random dump for this site.
                            // Fail-closed: keep legacy <=6 gate until native group predicate is mapped.
                            if (M2Share.RandomNumber.Random(BaseObject.m_btAntiPoison + 7) <= 6)
                            {
                                int nPower;
                                switch (StdItem.Shape)
                                {
                                    case 1:
                                        nPower = YanshenPoisonTimeCap.Cap(Magic.GetPower13(40, UserMagic) + Magic.GetRPow(PlayObject.m_WAbil.SC) * 2);// 中毒类型 - 绿毒
                                        BaseObject.SendDelayMsg(PlayObject, Grobal2.RM_POISON, Grobal2.POISON_DECHEALTH, nPower, PlayObject.ObjectId, HUtil32.Round(UserMagic.btLevel / 3.0 * ((double)nPower / M2Share.g_Config.nAmyOunsulPoint)), "", 1000);
                                        break;
                                    case 2:
                                        nPower = YanshenPoisonTimeCap.Cap(Magic.GetPower13(30, UserMagic) + Magic.GetRPow(PlayObject.m_WAbil.SC) * 2);// 中毒类型 - 红毒
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
                    int nPower = PlayObject.GetAttackPower(HUtil32.LoWord(PlayObject.m_WAbil.DC), HUtil32.HiWord(PlayObject.m_WAbil.DC) - HUtil32.LoWord(PlayObject.m_WAbil.DC));
                    if (M2Share.RandomNumber.Random(BaseObject.m_wSpeedPoint) >= PlayObject.m_btHitPoint)
                    {
                        nPower = 0;
                    }
                    if (nPower > 0)
                    {
                        nPower = BaseObject.GetHitStruckDamage(PlayObject, nPower);
                        nPower = BaseObject.ApplyNativePhysicalCritical(PlayObject, nPower);
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
            SendNativeMagicFire(PlayObject, UserMagic, (short)nTargetX,
                (short)nTargetY, TargeTBaseObject);
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
        // Native sub_76F33C @0x76F33C-0x76F3EE, the shared body for wMagicID 59
        // and 63. Disassembled from its own verified prologue (raw 558bec83c4f8
        // 535657). Order is load-bearing for RandSeed fidelity:
        //   76F34C  mov  al,[0x76F3F4] ; mov [ebp-5],al   ; flags, image byte = 00
        //   76F35E  call sub_4C8648                       ; skill power (raw btLevel)
        //   76F365  mov  edi,[ebx+0x294]                  ; LoWord(MC)
        //   76F36B  add  edx,edi                          ; base = power + LoWord(MC)
        //   76F36D  mov  ecx,[ebx+0x298] ; sub ecx,edi ; inc ecx  ; spread, INCLUSIVE
        //   76F37A  call [edi+0xCC]                       ; GetAttackPower -> edi
        //   76F384  call sub_4C896C ; cmp al,4 ; jne 0x76F3A5     ; effLevel == 4 ONLY
        //   76F38D  fild dword [ebx+0x298]                ; HiWord(MC) as integer
        //   76F393  fld  xword [0x76F3F8]                 ; 80-bit extended 0.1
        //   76F399  fmulp ; call sub_403574                ; @ROUND, half-to-even
        //   76F3A0  add  eax,5 ; add edi,eax              ; += Round(HiWord(MC)*0.1)+5
        //   76F3BD..76F3C5  push 1 / push 3 / push flags / push 1
        //   76F3CD  call sub_76FE44 with edx = 0 (xor edx,edx @0x76F3C9)
        //   76F3D2  mov eax,3 ; call sub_403B4C ; inc ecx ; call [ebx+0x3C]
        // Three faithfulness points, each byte-checked:
        //  (a) The stat pair is +0x294/+0x298 = MC. Adjudicated against two
        //      controls rather than assumed: the id-2 heal body reads the OTHER
        //      pair +0x29C/+0x2A0 (@0x76E4B2/@0x76E4BE) and its audited C# case
        //      uses SC, while the id-9 body reads +0x294/+0x298 (@0x76EC7B) and
        //      its C# case uses MC. Two independent bodies agree.
        //  (b) `xor edx,edx` @0x76F3C9 means target = NIL — this is an AREA
        //      dispatch (category 3, range 1), so nothing lands at cast time.
        //  (c) The x87 constant at 0x76F3F8 is the 80-bit extended 0.1
        //      (raw cdccccccccccccccfb3f = 0.10000000000000000555...), NOT
        //      exactly 1/10. I brute-forced the whole reachable u16 domain
        //      comparing the exact 64-bit-significand product + banker's round
        //      against C# `Math.Round(v * 0.1d, ToEven)`: ZERO divergences, so
        //      plain double is provably safe here and RoundDivMulExtended is
        //      not needed.
        // The delay is the trampoline's literal 0x258 = 600 (@0x6EDD2B), passed
        // as a stack arg rather than baked into the body.
        private static void TryProduceNativeMagic59(TPlayObject PlayObject,
            TUserMagic UserMagic, short nTargetX, short nTargetY)
        {
            int lowMagic = HUtil32.LoWord(PlayObject.m_WAbil.MC);
            int highMagic = HUtil32.HiWord(PlayObject.m_WAbil.MC);
            Plugins.YanshenSkillPatches.MainAttr(PlayObject,
                UserMagic.MagicInfo.wMagicID, lowMagic, highMagic,
                out lowMagic, out highMagic);
            int rawDamage = PlayObject.GetAttackPower(
                TPlayObject.CalculateNativeMagicProducerSkillPower(UserMagic) +
                    lowMagic,
                highMagic - lowMagic + 1);
            if (TPlayObject.GetNativeMagicProducerEffectiveLevel(UserMagic) == 4)
            {
                rawDamage = unchecked(rawDamage +
                    HUtil32.Round(highMagic * 0.1d) + 5);
            }
            // 0x76F3BD `6A 01` is the range slot the 盘古流星火雨范围 arm
            // overwrites (plugin 0x100B3E47 `mov byte [0x76F3BE],al`).
            PlayObject.QueueNativeMagicEffect(3, null, rawDamage,
                UserMagic.MagicInfo.wMagicID, nTargetX, nTargetY,
                Plugins.YanshenSkillPatches.RangeByte(PlayObject,
                    UserMagic.MagicInfo.wMagicID, 1),
                true, 0,
                MagicDamageContext.Capture(UserMagic), 600);
            // Native trains UNCONDITIONALLY @0x76F3D2-0x76F3E5, outside the
            // DoSpell tail's `btLevel < 3 && boTrain` gate — same shape as the
            // already-audited QueueNativeAreaBlast.
            PlayObject.TrainNativeMagicProducer(UserMagic,
                M2Share.RandomNumber.Random(3) + 1);
        }

        private static void QueueNativeAreaBlast(TPlayObject PlayObject,
            TUserMagic UserMagic, short nTargetX, short nTargetY)
        {
            ushort magicId = UserMagic.MagicInfo.wMagicID;
            int lowMagic = HUtil32.LoWord(PlayObject.m_WAbil.MC);
            int highMagic = HUtil32.HiWord(PlayObject.m_WAbil.MC);
            Plugins.YanshenSkillPatches.MainAttr(PlayObject, magicId,
                lowMagic, highMagic, out lowMagic, out highMagic);
            int rawDamage = PlayObject.GetAttackPower(
                TPlayObject.CalculateNativeMagicProducerSkillPower(UserMagic) +
                    lowMagic,
                highMagic - lowMagic + 1);
            rawDamage = Plugins.YanshenSkillPatches.ScaleDamage(PlayObject,
                magicId, rawDamage);
            byte range = Plugins.YanshenSkillPatches.RangeByte(PlayObject, magicId, 1);
            PlayObject.QueueNativeMagicEffect(3, null, rawDamage,
                magicId, nTargetX, nTargetY, range, true, 0,
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

        private bool MagElecBlizzard(TBaseObject BaseObject, int nPower,
            int range, int divisor)
        {
            var result = false;
            IList<TBaseObject> BaseObjectList = new List<TBaseObject>();
            BaseObject.GetMapBaseObjects(BaseObject.m_PEnvir, BaseObject.m_nCurrX, BaseObject.m_nCurrY, range, BaseObjectList);
            for (var i = 0; i < BaseObjectList.Count; i++)
            {
                var TargeTBaseObject = BaseObjectList[i];
                int nPowerPoint;
                if (TargeTBaseObject.m_btLifeAttrib != Grobal2.LA_UNDEAD)
                {
                    nPowerPoint = divisor == 0 ? nPower : nPower / divisor;
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
                                        // G3 / STATE-19 D9 — native state 0x1A (26) is always VMT+0xC8,
                                        // never 10300 (§3.2: no wParam=0x1A; immscan: 0×8037, 0×push-650).
                                        // DoSpell wMagicID 50 -> DEFAULT @0x6EE04B (table @0x6ED7CD idx 11);
                                        // native MabMabe duration formula UNPROVEN — drop nPower/
                                        // nMabMabeHitMabeTimeRate, keep Random(nLevel) from Delphi nParam1 tail.
                                        TargeTBaseObject.NativeMakePosion(0x1A,
                                            (ushort)M2Share.RandomNumber.Random(nLevel), 0);
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
            // 眼神「召唤神兽触发」不是常量改写，是 trampoline：安装器 0x10032FD0 在
            // 0x6EDC5E 把 `E8 19 12 08 00 call 0x76EE7C` 整条换成 `jmp <桩体>`，桩体只
            // 派发 @SummonShinsu 然后 jmp 回 0x6EDC63。**它不重放那条 call**，所以开关
            // 打开后原生神兽根本不产生。0x76EE7C 全镜像只有 0x6EDC5E 一个调用者，
            // 所以拦在生产函数入口与拦在调用点等价。
            if (YanshenTriggerDispatch.FireSummonShinsu(PlayObject))
            {
                return false;
            }
            var result = false;
            if (!PlayObject.CheckServerMakeSlave())
            {
                var sMonName = M2Share.g_Config.sDragon;
                int nMakelevel = TPlayObject
                    .GetNativeMagicProducerEffectiveLevel(UserMagic);
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
                        nCount = M2Share.g_Config.DragonArray[i].nCount;
                    }
                }
                // 眼神「召唤神兽」改写的是宿主读到的那两个常量本身：名字串
                // 0x0076EEEC（4 字节 GBK，补丁点 0x100A9ED5）与数量 imm8
                // 0x0076EE99（补丁点 0x100A9E9B）。宿主取常量是在
                // 0x0076EEAF mov edx,0x76EEEC / 0x0076EE98 push 1，也就是造宠调用
                // 的最后一步，所以覆盖必须放在 MakeSlave 之前、任何表查询之后。
                var ysShinsu = new YanshenApi(PlayObject, null, M2Share.PluginManager);
                if (ysShinsu.IsSummonShenShou())
                {
                    sMonName = ysShinsu.ShenShouName();
                    nCount = YanshenPangu1Patches.ShenShouSlaveCount(ysShinsu);
                }
                // 「修改召唤神兽」装的是 detour，不是常量覆盖：0x100BA4E0 / 0x100BA4F9
                // 两次 call 0x10032B10 把 0x0076EE98 起 7 字节（push 数量 + push 叛变秒数）
                // 和 0x0076EEAF 起 5 字节（mov edx,名字指针）整段换成跳转，所以它决定的是
                // 最终交给 MakeSlave 的名字与数量，排在「召唤神兽」的常量覆盖之后——
                // 补丁区间本身就吃掉了 0x0076EE99 这个 imm8。取值域见 TryGetModifyShenShou。
                if (ysShinsu.TryGetModifyShenShou(PlayObject.m_Abil.Level,
                        out var ysDragonName, out var ysDragonCount))
                {
                    sMonName = ysDragonName;
                    nCount = ysDragonCount;
                }
                if (PlayObject.MakeNativeSlave(sMonName, nMakelevel, nCount,
                        dwRoyaltySec, fromHero: false, hpAfterSlave: 10) != null)
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
            // 眼神「召唤骷髅触发」同形：0x10032FD0 在 0x6EDB44 把
            // `E8 B3 12 08 00 call 0x76EDFC` 换成 jmp 桩体，桩体派发 @SummonSkele 后
            // jmp 0x6EDB49，不重放那条 call。0x76EDFC 全镜像同样只有 0x6EDB44 一个调用者。
            // 注意：C# 把 SKILL_SKELLETON 与 SKILL_51 两个分支都接到本函数，而原生它们
            // 分别走 sub_76EDFC 与 sub_76EFC0 两个生产函数；这条既有合并不是本次引入的，
            // 拆分前把门放在生产函数入口是能拿到的最贴近映射。
            if (YanshenTriggerDispatch.FireSummonSkele(PlayObject))
            {
                return false;
            }
            var result = false;
            if (!PlayObject.CheckServerMakeSlave())
            {
                var sMonName = M2Share.g_Config.sSkeleton;
                int nMakeLevel = TPlayObject
                    .GetNativeMagicProducerEffectiveLevel(UserMagic);
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
                        nCount = M2Share.g_Config.SkeletonArray[i].nCount;
                    }
                }
                // 眼神「召唤骷髅」只改数量 imm8 0x0076EE1F（补丁点 0x100AA04B）。
                // 名字常量 0x0076EE70「变异骷髅」全镜像没有任何补丁指向它，不覆盖。
                var ysSkele = new YanshenApi(PlayObject, null, M2Share.PluginManager);
                if (ysSkele.IsSummonKuLou())
                {
                    nCount = YanshenPangu1Patches.KuLouSlaveCount(ysSkele);
                }
                if (PlayObject.MakeNativeSlave(sMonName, nMakeLevel, nCount,
                        dwRoyaltySec, fromHero: false, hpAfterSlave: 10) != null)
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
                var dwRoyaltySec = 10 * 24 * 60 * 60;
                if (PlayObject.MakeNativeSlave(sMonName, nMakeLevel, 1,
                        dwRoyaltySec, fromHero: false, hpAfterSlave: 10) != null)
                {
                    result = true;
                }
            }
            return result;
        }

        // Native id 62 handler @0x6EDC71 (跳表 2 @0x6ED7CD 索引 62-0x27=23 存 0x6EDC71,
        // 原始字节 71 DC 6E 00 @0x6ED829 —— 确认这一块就是 wMagicID 62 "圣兽")。
        // 逐地址真值:
        //   006EDC71  C6 45 FA 01           mov byte [ebp-6],1      ; boSpellFail := True
        //   006EDC75  E8 C6 A6 D1 FF        call 0x408340           ; GetTickCount
        //   006EDC7A  2B 83 0C 05 00 00     sub eax,[ebx+0x50C]
        //   006EDC80  3D 30 75 00 00        cmp eax,0x7530          ; 30000
        //   006EDC85  76 3F                 jbe 0x6EDCC6            ; <=30s -> 冷却提示
        //   006EDC87  B1 01                 mov cl,1                ; boConsume = True
        //   006EDC89  BA D0 07 00 00        mov edx,0x7D0           ; nCount = 2000
        //   006EDC90  E8 8B 0D 05 00        call 0x73EA20           ; 【消耗在召唤之前】
        //   006EDC95  84 C0 / 74 15         test al,al / je 0x6EDCAE; 没护身符 -> 提示
        //   006EDC99  C6 45 FA 00           mov byte [ebp-6],0      ; boSpellFail := False
        //   006EDCA3  FF 91 24 01 00 00     call [ecx+0x124]        ; 0x76EEF4 造宠, 返回值丢弃
        //   006EDCAE  mov cx,0xFFDB / edx=0x6EE0DC / [vmt+0xD4]     ; "没有足够的护身符"
        //   006EDCC6  mov cx,0xFFDB / edx=0x6EE0F8 / [vmt+0xD4]     ; "圣兽刚收回不到30秒，元气尚未回复"
        // ([ebp-6] 是外层 sub_6ED62C 的 boSpellFail: 0x6EE04B `cmp byte[ebp-6],0 / jne`
        //  为真则整支 DoSpell 返回 False 且不发 SM_MAGICFIRE。)
        //
        // SKILL-62 (2026-08-14) 修正的三处顺序/条件错误:
        //   (a) 消耗必须在造宠【之前】且【无条件】——原生 0x6EDC90 一旦过了 30 秒门就扣,
        //       扣完才去造宠; 旧代码是 cl=0 试探 + 造宠成功后才 cl=1 真扣。
        //   (b) boSpellFail 在 0x6EDC99 就定死为 False, 造宠成功与否【不影响】返回值,
        //       失败也不退护身符、不发任何消息 (sub_76EEF4 三处早退全是静默 return nil)。
        //   (c) 冷却时间戳 +0x50C 【不】在本块写入 —— 唯一运行期写入点是
        //       THolyMonster.Die = sub_66C2F4 @0x66C327 (圣兽被收回/死亡时记到召唤者身上),
        //       已落地在 HolyMonster.Die; 旧代码在造宠成功后自行盖戳属臆造。
        //   另: 冷却提示串取自 0x6EE0F8 (GBK, len 0x20), 旧串 "魔法使用还没恢复30秒" 无出处;
        //       怪物名取 0x76EF74 的字面量 "圣兽", 不是 g_Config.sAngel(默认 "精灵")。
        private static bool TryProduceNativeMagic62(TPlayObject PlayObject,
            TUserMagic UserMagic)
        {
            // 0x6EDC75-0x6EDC85: 严格大于 30000 才放行 (jbe 走冷却臂)。
            if (HUtil32.GetTickCount() - PlayObject.m_dwMagic62LastTick <= 30000)
            {
                PlayObject.SysMsg("圣兽刚收回不到30秒，元气尚未回复", MsgColor.Red,
                    MsgType.Hint);
                return false;                                   // boSpellFail 保持 1
            }
            // 0x6EDC87-0x6EDC97: sub_73EA20(cl=1, edx=2000) —— 装备槽9 优先、其次背包,
            // 扣【原始】2000 耐久 (不 ×100), 余量 <100 才销毁。忠实移植见
            // TPlayObject.NativeAmuletConsume.cs; 返回值是"是否找到合格护身符"。
            if (!PlayObject.NativeConsumeBujukCharm(2000, true))
            {
                PlayObject.SysMsg("没有足够的护身符", MsgColor.Red,
                    MsgType.Hint);
                return false;                                   // boSpellFail 保持 1
            }
            // 0x6EDC99 boSpellFail := 0 —— 在造宠【之前】写死, 之后再不改。
            // 0x6EDCA3 VMT+0x124 = sub_76EEF4, 返回值被丢弃。
            PlayObject.NativeMakeHolyBeastSlave(UserMagic);
            return true;
        }

        // Native ids 66/67 shared handler @0x6EDE39: calls sub_745744(self,
        // target, targetY), inverts result. Bytes: 8B 4D 08 8B D6 8B C3
        // E8 FF 78 05 00 34 01 88 45 FA. Sub_745744 is BLOCKED pending reverse
        // engineering, so this is a stub that returns false.
        private static bool TryProduceNativeMagic66(TPlayObject PlayObject,
            TBaseObject target, short targetY)
        {
            // BLOCKED: sub_745744 not yet reversed. Returning false maintains
            // fail-closed semantics (boSpellFail=true => sends 0x27F).
            return false;
        }

        // Native id 111 handler @0x6EDE4F: VMT+0x128 call, sets boSpellFail
        // if result==0. Bytes: 8B D6 8B C3 8B 08 FF 91 28 01 00 00 85 C0
        // 0F 94 45 FA. VMT+0x128 -> sub_74633C is BLOCKED pending reverse
        // engineering, so this is a stub that returns false.
        private static bool TryProduceNativeMagic111(TPlayObject PlayObject,
            TBaseObject target)
        {
            // BLOCKED: VMT+0x128 (sub_74633C) not yet reversed. Returning false
            // maintains fail-closed semantics (boSpellFail=true => sends 0x27F).
            return false;
        }

        // Native id 151 handler @0x6EDD70: calls sub_745A20 which dispatches
        // to VMT+0x1F4 with magicId=151 (0x97). Bytes: 8B D6 8B C3 E8 A7 7C
        // 05 00 34 01 88 45 FA. VMT+0x1F4 dispatcher is BLOCKED pending reverse
        // engineering, so this is a stub that returns false.
        private static bool TryProduceNativeMagic151(TPlayObject PlayObject,
            TBaseObject target)
        {
            // BLOCKED: VMT+0x1F4 magic dispatcher (sub_745A20 -> sub_748288)
            // not yet reversed. Returning false maintains fail-closed semantics.
            return false;
        }

        // Native id 154 handler @0x6EDD83: calls sub_74588C which dispatches
        // to VMT+0x1F4 with magicId=154 (0x9A). Bytes: 8B D6 8B C3 E8 00 7B
        // 05 00 34 01 88 45 FA. VMT+0x1F4 dispatcher is BLOCKED pending reverse
        // engineering, so this is a stub that returns false.
        private static bool TryProduceNativeMagic154(TPlayObject PlayObject,
            TBaseObject target)
        {
            // BLOCKED: VMT+0x1F4 magic dispatcher (sub_74588C -> sub_748288)
            // not yet reversed. Returning false maintains fail-closed semantics.
            return false;
        }
    }
}
