using System.Buffers.Binary;
using System.Diagnostics;
using SystemModule;
using System.Runtime.CompilerServices;

namespace GameSvr
{
    public partial class TBaseObject
    {
        // Native sub_745A20 @0x745A20-0x745B5B — id 151 (0x97) 破釜沉舟 burst state
        // Disassembly evidence from D:/loym2/staging/_reunpack_work/flat_image.bin:
        //   00745A4D  e81a2fd8ff    call 0x4C896C                   ; effective level
        //   00745A52  48            dec eax
        //   00745A53  2c03          sub al, 3
        //   00745A55  0f83e5000000  jae 0x745B40                    ; reject if effLevel-1 >= 3
        //   00745A5B  ba97000000    mov edx, 0x97                   ; coldTime key = 151
        //   00745A60  ff93f4010000  call dword ptr [ebx+0x1F4]      ; probe cooldown
        //   00745A66  85c0          test eax, eax
        //   00745A68  7433          je 0x745A9D                     ; zero = ready
        // Refusal arm @0x745A6A-0x745A9B:
        //   00745A6A  b9e8030000    mov ecx, 0x3E8                  ; 1000
        //   00745A6F  f7f1          div ecx                         ; remaining seconds
        //   00745A88  66b9dbff      mov cx, 0xFFDB                  ; GREEN
        //   "还需要" + secs + "秒才能释放该技能"
        // Success arm @0x745A9D-0x745B37:
        //   00745A9D  0fb6c0        movzx eax, al                   ; effective level (1-3)
        //   00745AA0  668b04457efe7d00  mov ax, word ptr [eax*2+0x7D3EFE]  ; table[level]
        //   00745AA8  6689863f040000  mov word ptr [esi+0x3F4], ax  ; self+0x3F4 = strike count
        //   00745AAF  8b0485047f7d00  mov eax, dword ptr [eax*4+0x7D3F04] ; Single factor table
        //   00745AB6  8986f8030000  mov dword ptr [esi+0x3F8], eax  ; self+0x3F8 = Single factor bits
        //   00745AC5  66b9dbff      mov cx, 0xFFDB                  ; GREEN
        //   00745AD2  ba30a87400    mov edx, 0x74A830               ; message string
        //   "进入破釜沉舟状态，之后" + N + "次攻击会造成额外伤害"
        //   00745AFC  b997000000    mov ecx, 0x97                   ; key
        //   00745B01  ba30750000    mov edx, 0x7530                 ; 30000 ms
        //   00745B06  ff91f0010000  call dword ptr [ecx+0x1F0]      ; arm cooldown

        internal const int NativeSkill151Id = 151;
        private const int NativeSkill151CooldownMilliseconds = 30000;
        private const int NativeSkill151ColdTimeKey = 0x97;

        // Native tables, read raw from flat_image.bin. The previous values were
        // invented: the old comment's addresses each pointed one entry short of
        // the real table, and the damage table is Single, not Int32.
        //   0x7D3F00  01 00 | 02 00 | 04 00              Int16 strike counts
        //   0x7D3F08  00 00 80 3E | 00 00 00 3F | 00 00 80 3F
        //                                                Single 0.25 / 0.5 / 1.0
        // Both hold 3 entries indexed by effective level 1..3; slot [0] stays
        // unused so the callers' 1-based indexing is unchanged.
        private static readonly ushort[] NativeSkill151StrikeCounts = { 0, 1, 2, 4 };
        private static readonly float[] NativeSkill151DamageFactors =
            { 0f, 0.25f, 0.5f, 1.0f };

        // Native stores the per-hit bonus FACTOR (Single) at self+0x3F8, not a
        // flat additive int. The old { 0, 50, 80, 120 } was invented and has
        // been removed. The consumer at 0x746247 (shared with id 154) is now
        // fully reversed:
        //   00746247  66 83 BE F4 03 00 00 00  cmp word [esi+0x3F4],0
        //   0074624F  76 3B                    jbe 0x74628C        ; no strikes
        //   00746251  81 7D F8 FA 03 00 00     cmp [ebp-8],0x3FA   ; attack kind
        //   00746258  74 09                    je 0x746263
        //   0074625A  81 7D F8 E8 03 00 00     cmp [ebp-8],0x3E8
        //   00746261  75 29                    jne 0x74628C
        //   00746263  B2 01                    mov dl,1            ; flag = max
        //   00746265  8B C6                    mov eax,esi         ; Self
        //   00746267  E8 20 6B 02 00           call 0x76CD8C       ; job max att
        //   0074626C  BA 88 13 00 00           mov edx,0x1388      ; 5000
        //   00746271  E8 96 0D D8 FF           call 0x4C700C       ; min(pow,5000)
        //   00746276  8D 04 80                 lea eax,[eax+eax*4] ; * 5
        //   0074627C  DB 45 E8                 fild dword [ebp-0x18]
        //   0074627F  D8 8E F8 03 00 00        fmul dword [esi+0x3F8] ; * factor
        //   00746285  E8 F6 D2 CB FF           call 0x403580       ; @TRUNC (chop)
        //   0074628A  03 D8                     add ebx,eax        ; damage += bonus
        // i.e. bonus = TRUNC(min(jobMaxAttack, 5000) * 5 * factor[level]).
        // The consumer does not decrement +0x3F4 here. sub_770F50 decrements it
        // only after its main direct carrier returns a positive value.
        //
        // Native attack-kind discriminator [ebp-8] gate values (0x746251 /
        // 0x74625A): id 151's burst fires only for kinds 0x3FA and 0x3E8.
        private const int NativeSkill151AttackKindA = 0x3FA;
        private const int NativeSkill151AttackKindB = 0x3E8;

        private ushort m_nNativeSkill151StrikeCount;
        // self+0x3F8: the Single factor pulled from NativeSkill151DamageFactors.
        private float m_fNativeSkill151DamageFactor;

        internal bool TryActivateNativeSkill151(TUserMagic userMagic,
            [CallerFilePath] string sourceFile = "")
        {
            return TryActivateNativeSkill151(userMagic,
                HUtil32.GetTickCount(), sourceFile);
        }

        internal bool TryActivateNativeSkill151(TUserMagic userMagic,
            int now, [CallerFilePath] string sourceFile = "")
        {
            // Native @0x745A4D-0x745A55: effective level check
            byte effectiveLevel = GetNativeSkill151EffectiveLevel(userMagic);
            if (effectiveLevel < 1 || effectiveLevel > 3)
            {
                return false;
            }

            // Native @0x745A5B-0x745A68: coldTime probe (key 0x97)
            int remainingMs = GetNativeColdTimeRemaining(
                NativeSkill151ColdTimeKey);
            if (remainingMs != 0)
            {
                uint seconds = unchecked((uint)remainingMs) / 1000u;
                SendNativeSkill151Hint(
                    $"还需要{seconds}秒才能释放该技能");
                return false;
            }

            // Native @0x745A9D-0x745AB6: read tables and set state. self+0x3F4
            // takes the Int16 strike count, self+0x3F8 the Single factor.
            ushort strikeCount = NativeSkill151StrikeCounts[effectiveLevel];
            float factor = NativeSkill151DamageFactors[effectiveLevel];

            // Assertion (write-only, not executed here): the corrected tables
            // must hold exactly the raw bytes at 0x7D3F00 / 0x7D3F08.
            Debug.Assert(strikeCount == effectiveLevel switch
                { 1 => 1, 2 => 2, 3 => 4, _ => 0 },
                "151 strike count diverged from 0x7D3F00 {01,02,04}");
            Debug.Assert(factor == effectiveLevel switch
                { 1 => 0.25f, 2 => 0.5f, 3 => 1.0f, _ => 0f },
                "151 damage factor diverged from 0x7D3F08 {0.25,0.5,1.0}");

            m_nNativeSkill151StrikeCount = strikeCount;
            m_fNativeSkill151DamageFactor = factor;

            // Native @0x745AC5-0x745AD7: send success message
            SendNativeSkill151Hint(
                $"进入破釜沉舟状态，之后{strikeCount}次攻击会造成额外伤害");

            // Native @0x745AFC-0x745B06: arm cooldown (key 0x97, 30000 ms)
            SetNativeColdTime(NativeSkill151ColdTimeKey,
                NativeSkill151CooldownMilliseconds, now);

            return true;
        }

        /// <summary>
        /// Native consumer arm @0x746247. Adds the burst bonus to the running
        /// melee damage accumulator (ebx) when a strike is banked AND the attack
        /// kind matches. Byte-for-byte:
        ///   bonus = TRUNC(min(jobMaxAttack, 5000) * 5 * factor).
        /// </summary>
        internal int ApplyNativeSkill151BurstDamage(int damage, int attackKind)
        {
            // 0x746247 jbe / 0x746251+0x74625A attack-kind gate. Native does
            // NOT test damage>0 and does NOT decrement the counter here.
            if (m_nNativeSkill151StrikeCount > 0 &&
                (attackKind == NativeSkill151AttackKindA ||
                 attackKind == NativeSkill151AttackKindB))
            {
                int jobMax = GetNativeBurstJobMaxAttack();       // 0x76CD8C
                int capped = Math.Min(jobMax, 5000);             // 0x4C700C
                int base5 = unchecked(capped * 5);               // lea *,[*+*4]
                // 0x74627F fmul float32 factor then 0x403580 @TRUNC (chop). The
                // three factors 0.25/0.5/1.0 are exact in both float32 and
                // double, and base5 <= 25000, so the double product reproduces
                // the x87 result before truncation exactly.
                int bonus = (int)Math.Truncate(
                    base5 * (double)m_fNativeSkill151DamageFactor);
                return unchecked(damage + bonus);
            }
            return damage;
        }

        internal void ConsumeNativeSkill151StrikeAfterMainDamage(
            int mainApplied)
        {
            if (mainApplied > 0 && m_nNativeSkill151StrikeCount > 0)
                m_nNativeSkill151StrikeCount--;
        }

        /// <summary>
        /// sub_76CD8C @0x76CD8C, called from the 151/154 burst consumers with
        /// dl = 1. The dl flag is stored at [ebp-1] and, via the `push ebp`
        /// trampoline into the shared tail sub_76CD5C @0x76CD5C, selects the
        /// max-attack branch (`cmp byte [eax-1],0 / jne` @0x76CD68 returns the
        /// HIGH dword directly instead of rolling low + Random(high-low)). The
        /// job byte [self+0x72] then picks the attack pair:
        ///   job 0 @0x76CDA7  DC = dword[+0x28C]/[+0x290]  -> HiWord(m_WAbil.DC)
        ///   job 1 @0x76CDBD  MC = dword[+0x294]/[+0x298]  -> HiWord(m_WAbil.MC)
        ///   job 2 @0x76CDD3  SC = dword[+0x29C]/[+0x2A0]  -> HiWord(m_WAbil.SC)
        ///   job 3 @0x76CDE9  CC = dword[+0x2A4]/[+0x2A8]  -> CCHigh
        ///   else  @0x76CDFF  xor eax,eax                  -> 0
        /// The +0x28C..+0x2A8 layout is the same working-ability block the
        /// 65..68 charged counter reads, so the mapping matches that verified
        /// port. Because dl = 1 always, only the HIGH (max) word is used.
        /// </summary>
        private int GetNativeBurstJobMaxAttack()
        {
            switch (m_btJob)
            {
                case 0: return HUtil32.HiWord(m_WAbil.DC);
                case 1: return HUtil32.HiWord(m_WAbil.MC);
                case 2: return HUtil32.HiWord(m_WAbil.SC);
                case 3: return m_NativeCoreWorkingAbility.CCHigh;
                default: return 0;
            }
        }

        internal static byte GetNativeSkill151EffectiveLevel(
            TUserMagic magic)
        {
            if (magic?.MagicInfo == null)
                return 0;

            return (byte)Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        private void SendNativeSkill151Hint(string text)
        {
            // Native: cx=0xFFDB = GREEN
            if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                    0xDB, 0xFF, 0, text);
            }
        }

        private void ClearNativeSkill151StateOnExit()
        {
            m_nNativeSkill151StrikeCount = 0;
            m_fNativeSkill151DamageFactor = 0f;
        }
    }
}
