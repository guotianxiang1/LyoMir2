using System.Buffers.Binary;
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
        //   00745AAF  8b0485047f7d00  mov eax, dword ptr [eax*4+0x7D3F04] ; damage table
        //   00745AB6  8986f8030000  mov dword ptr [esi+0x3F8], eax  ; self+0x3F8 = bonus damage
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

        // DIVERGENT, deliberately not guessed. Native does NOT add a flat bonus.
        // The consumer at 0x746247 (shared with id 154) computes it per hit:
        //   0x746247  cmp word [esi+0x3F4],0   ; remaining strikes, else skip
        //   0x746267  call 0x76CD8C            ; class-dispatched power getter:
        //                                      ;   reads byte [self+0x72] (job)
        //                                      ;   job 0 -> ([+0x28C],[+0x290])
        //                                      ;   into 0x76CD5C; jobs 1/2/3 at
        //                                      ;   0x76CDBD / 0x76CDD3 / 0x76CDE9
        //   0x74626C  mov edx,0x1388           ; 5000
        //   0x746271  call 0x4C700C            ; min(power, 5000)
        //   0x746276  lea eax,[eax+eax*4]      ; * 5
        //   0x74627F  fmul dword [esi+0x3F8]   ; * the stored Single factor
        //   0x746285  call 0x403580            ; @TRUNC
        //   0x74628A  add ebx,eax
        // i.e. bonus = TRUNC(min(classPower, 5000) * 5 * factor[level]).
        // Id 154's block at 0x74628C is the same shape with the fmul absent,
        // which is why it has no factor table of its own.
        // Landing this needs the four job branches of 0x76CD8C and the
        // semantics of 0x76CD5C mapped to their C# counterparts first; until
        // then the additive model below is knowingly wrong rather than
        // speculatively rewritten.
        private static readonly int[] NativeSkill151BonusDamages = { 0, 50, 80, 120 };

        private ushort m_nNativeSkill151StrikeCount;
        private int m_nNativeSkill151BonusDamage;

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

            // Native @0x745A9D-0x745AB6: read tables and set state
            ushort strikeCount = NativeSkill151StrikeCounts[effectiveLevel];
            int bonusDamage = NativeSkill151BonusDamages[effectiveLevel];

            m_nNativeSkill151StrikeCount = strikeCount;
            m_nNativeSkill151BonusDamage = bonusDamage;

            // Native @0x745AC5-0x745AD7: send success message
            SendNativeSkill151Hint(
                $"进入破釜沉舟状态，之后{strikeCount}次攻击会造成额外伤害");

            // Native @0x745AFC-0x745B06: arm cooldown (key 0x97, 30000 ms)
            SetNativeColdTime(NativeSkill151ColdTimeKey,
                NativeSkill151CooldownMilliseconds, now);

            return true;
        }

        internal int ApplyNativeSkill151BurstDamage(int damage)
        {
            // Apply bonus if state is active (strike count > 0)
            if (m_nNativeSkill151StrikeCount > 0 && damage > 0)
            {
                m_nNativeSkill151StrikeCount--;
                return unchecked(damage + m_nNativeSkill151BonusDamage);
            }
            return damage;
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
            m_nNativeSkill151BonusDamage = 0;
        }
    }
}
