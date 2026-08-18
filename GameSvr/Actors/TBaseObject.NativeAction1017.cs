using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Action code 1017 (<c>0x3F9</c>) — the swing half of ident 3035.
    ///
    /// <para>
    /// Route: ident 3035 -> HIT CASE1 <c>0x6D9EAF</c> -> <c>sub_6EC078</c>
    /// (window 3002..3035, byte table <c>0x6EC178[33] = 0x09</c>) -> slot
    /// <c>0x6EC19A[9] = 0x6EC29C</c> `66 B9 F9 03 mov cx,0x3F9` ->
    /// <c>sub_7707A8</c> jump-table slot 17 (<c>0x77081C[17] = 0x770ABF</c>).
    /// </para>
    ///
    /// <para>
    /// The worker <c>sub_772388</c> has exactly ONE rel32 caller in the whole
    /// image, <c>0x770AF3</c>, i.e. this arm. Its sibling action 1018
    /// (<c>CM_CRSHIT</c>) calls <c>0x771BB8</c>, which is the nine-byte stub
    /// `55 8B EC 33 C0 5D C2 04 00`, so 1017 is the live one of the pair.
    /// </para>
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>0x7723FE `68 F9 03 00 00 push 0x3F9` — the action code the
        /// worker hands back to <c>sub_76E268</c> as its wIdent.</summary>
        internal const int NativeAction1017Code = 0x3F9;

        /// <summary>0x7723A5 `6A 02 push 2` — the reach of the fallback target
        /// search, in cells, measured from the caster along m_btDirection.
        /// Note the ADJACENT cell is probed first, by <c>sub_767E80</c> in
        /// <c>sub_7707A8</c>'s prologue; this second probe only runs when that
        /// came back nil.</summary>
        private const int NativeAction1017SearchRange = 2;

        /// <summary>0x77242B `66 B9 40 27 mov cx,0x2740` = 10048, the RunMsg
        /// ident the victim receives. Grobal2.cs carries no name for it and is
        /// off-limits, so the constant lives here.</summary>
        internal const int NativeAction1017StruckIdent = 0x2740;

        /// <summary>0x772429 `6A 46 push 0x46` — the LAST of the six stack
        /// pushes into <c>sub_766060</c>, i.e. dwDelay = 70 ms, not a payload
        /// field. The other five (wParam, nParam1, nParam2, nParam3, sMsg) are
        /// all `6A 00`.</summary>
        private const int NativeAction1017StruckDelayMs = 0x46;

        /// <summary>0x767073, the arm ident 10048 reaches from the RunMsg
        /// comparison tree (<c>0x766AB3 cmp eax,0x2740</c> /
        /// <c>0x766ABA je 0x767073</c>): `6A 20` = nParam1 32 on a 10501
        /// broadcast.</summary>
        private const int NativeAction1017StruckEffect = 0x20;

        /// <summary>
        /// The whole 1017 arm, <c>0x7707E3</c> + <c>0x7707EE</c> +
        /// <c>0x770ABF..0x770AF8</c>. The caller has already applied the
        /// facing write, which native does at <c>0x7707E3</c>
        /// `88 86 54 01 00 00 mov [esi+0x154],al` for every code in the
        /// 1000..1033 window.
        /// </summary>
        /// <returns>
        /// The final byte result after the native result-zero ordinary-attack
        /// fallback. A result of two runs the shared physical tail before the
        /// action frame is broadcast.
        /// </returns>
        internal int RunNativeAction1017()
        {
            // 0x7707EE `83 7D FC 00` / `75 0A jne` — the 3035 arm enters
            // sub_7707A8 with edx = 0 (0x6EC2A6 `33 D2`), so the nil branch
            // always runs and 0x7707F6 `call 0x767E80` resolves the target.
            // sub_767E80 is GetFrontPosition (sub_766214, which reads
            // [self+0x154] & 7) followed by GetMovingObject — GetPoseCreate.
            TBaseObject initialTarget = GetPoseCreate();
            TUserMagic frameMagic = m_NativeChargedCounterMagic;

            // 0x770AE5 `FF 93 CC 00 00 00 call [vmt+0xCC]` on
            // (edx = DC low, ecx = DC high - DC low). VMT+0xCC is 0x767F10,
            // the five-instruction thunk `push ebp / mov ebp,esp /
            // call 0x76C804 / pop ebp / ret 8`, so the two stack pushes at
            // 0x770ACC (the action code) and 0x770AD0 (the cached UserMagic)
            // are cleaned up unread.
            //
            // 0x770AEB saves this result at [ebp-0xC]; 0x770D3A consumes it
            // in the shared physical tail when the worker result is two.
            int tailPower = GetAttackPower(HUtil32.LoWord(m_WAbil.DC),
                HUtil32.HiWord(m_WAbil.DC) - HUtil32.LoWord(m_WAbil.DC));

            int result = RunNativeAction1017Swing(initialTarget);
            int frameAction = NativeAction1017Code;
            if (result == 0)
            {
                // 0x770CE1 replaces the frame-magic local with self+0x9C,
                // rerolls DC and enters the same ordinary fallback used by
                // the other zero-result physical workers.
                frameMagic = GetSunSwordFallbackMagic();
                tailPower = GetAttackPower(HUtil32.LoWord(m_WAbil.DC),
                    HUtil32.HiWord(m_WAbil.DC) -
                    HUtil32.LoWord(m_WAbil.DC));
                result = RunNativeBasicAttackFallback(initialTarget,
                    tailPower);
                frameAction = 1000;
            }

            if (result == 2)
                RunNativePhysicalAttackCommonTail(initialTarget, tailPower);

            // 0x770DC2..0x770DD3 is outside the result==2 block and tests
            // self+0x178 for the player race value zero.
            if (m_btRaceServer == Grobal2.RC_PLAYOBJECT &&
                initialTarget != null)
                CheckWeaponUpgrade();

            int effectiveLevel = frameMagic == null
                ? 0
                : NativeEffectiveMagicLevel(frameMagic);
            byte[] body = BuildSunSwordPhysicalAttackBody(frameAction,
                effectiveLevel, m_btDirection, m_nCurrX, m_nCurrY);
            SendRefMsg(Grobal2.RM_PHYSICAL_ATT, frameAction, m_nCurrX,
                m_nCurrY, 0, string.Empty,
                new NativePhysicalAttackFramePayload(body, false));
            return result;
        }

        /// <summary>
        /// <c>sub_772388</c> verbatim (eax = Self, edx = target, ret 4).
        /// </summary>
        internal int RunNativeAction1017Swing(TBaseObject target)
        {
            // 0x772394 `C6 45 FF 00`
            int result = 0;

            // 0x772398 `85 F6 / 75 49 jne 0x7723E5` — an already resolved
            // target skips the search entirely.
            if (target == null && m_PEnvir != null)
            {
                short nX = 0;
                short nY = 0;
                // 0x77239E..0x7723C1: GetNextPosition(CurrX, CurrY,
                // m_btDirection, 2, out X, out Y). The two `lea/push` at
                // 0x7723A7 / 0x7723AB are the out params in X, Y order, which
                // 0x7723D2 then reloads in the same order.
                if (m_PEnvir.GetNextPosition(m_nCurrX, m_nCurrY, m_btDirection,
                        NativeAction1017SearchRange, ref nX, ref nY))
                {
                    // 0x7723DE `call 0x7784A8` with `6A 01 / 6A 00 x3`.
                    target = (TBaseObject)m_PEnvir.GetMovingObject(nX, nY, true);
                }
            }

            // 0x7723EB `FF 51 4C call [vmt+0x4C]`. TPlayer VMT 0x6AC8C8+0x4C
            // and THumanKind VMT 0x73BC34+0x4C both hold 0x744388; the base
            // TCreature slot is `or eax,-1 / ret`, a flat refusal.
            int damage = ResolveNativeChargedCounterPower(target,
                HUtil32.GetTickCount());

            // 0x7723EE `85 C0` / 0x7723F0 `7C 60 jl 0x772452` — a refused
            // cast leaves the result at 0 and skips the training too.
            if (damage < 0)
            {
                return result;
            }
            // 0x7723F2 `C6 45 FF 01`
            result = 1;

            // 0x7723F6 `85 C0` / 0x7723F8 `7E 21 jle 0x77241B`
            if (damage > 0)
            {
                // 0x7723FA `C6 45 FF 02`
                result = 2;
                // 0x7723FE..0x772416. Pushes are left-to-right, so
                // [ebp+0x14] = 0x3F9 (the wIdent), [ebp+0x10] = damage,
                // [ebp+0x0C] = flags and [ebp+8] = arg0 = 1; ecx carries the
                // cached UserMagic and edx the target.
                //
                // The flags push is `A0 5C 24 77 00 mov al,byte [0x77245C]` —
                // a BYTE load that leaves the upper 24 bits of eax holding
                // the damage still in the register. sub_76E268 reads only the
                // low byte (0x76E292 `8A 45 0C mov al,[ebp+0xC]`), and
                // [0x77245C] measures 0x00 in the image, so flags = 0.
                ApplyNativeDirectMagicEffect(target, NativeAction1017Code,
                    true, MagicDamageContext.Capture(
                        m_NativeChargedCounterMagic), 0, damage);
            }

            // 0x77241B `85 F6` / `74 19 je 0x772438`. This is guarded on the
            // TARGET, not on the damage: a ghost target makes 0x744388 return
            // 0 at 0x7444E7, and the message still goes out.
            if (target != null)
            {
                // 0x77241F `6A 00` x5 then 0x772429 `6A 46`, cx = 0x2740,
                // edx = Self, eax = the target: the victim is the receiver
                // and the caster rides in as the message's BaseObject.
                target.SendDelayMsg(this, NativeAction1017StruckIdent,
                    0, 0, 0, 0, string.Empty, NativeAction1017StruckDelayMs);
            }

            // 0x772438 `B8 03 00 00 00 / call 0x403B4C / 8B C8 / 41` then
            // 0x77244F `FF 53 3C call [vmt+0x3C]` = 0x76AD30 for both the
            // player and war-hero VMTs. It is unconditional on damage >= 0,
            // so a swing that hits nothing still trains. [ebx+0xC4] cannot
            // be nil here: 0x7443AA would have returned -1 above.
            TrainNativePhysicalMagic(m_NativeChargedCounterMagic,
                M2Share.RandomNumber.Random(3) + 1);

            // 0x772452 `8A 45 FF mov al,[ebp-1]`
            return result;
        }

        /// <summary>
        /// RunMsg arm <c>0x767073</c>, reached from <c>sub_766A7C</c>'s
        /// comparison tree at <c>0x766AB3 cmp eax,0x2740</c> /
        /// <c>0x766ABA 0F 84 B3 05 00 00 je 0x767073</c>. The whole body is
        /// one broadcast and a jump to the shared tail <c>0x767166</c>:
        /// <code>
        /// 767073  6A 20              push 0x20        ; nParam1
        /// 767075  6A 00 x4           push 0 x4        ; nParam2/nParam3/sMsg/boFlag
        /// 76707D  33 C9              xor ecx,ecx      ; wParam
        /// 76707F  66 BA 05 29        mov dx,0x2905    ; 10501
        /// 767087  FF 93 D8 00 00 00  call [vmt+0xD8]
        /// </code>
        /// eax is edi, the message's own receiver, so the victim broadcasts
        /// effect 32 around itself.
        /// </summary>
        private void RunNativeAction1017StruckMessage()
        {
            SendRefMsg(Grobal2.RM_10501, 0, NativeAction1017StruckEffect,
                0, 0, string.Empty);
        }
    }
}
