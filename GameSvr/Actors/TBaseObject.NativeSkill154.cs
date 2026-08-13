using System.Buffers.Binary;
using SystemModule;
using System.Runtime.CompilerServices;

namespace GameSvr
{
    public partial class TBaseObject
    {
        // Native sub_74588C @0x74588C-0x7459C7 — id 154 (0x9A) 背水一战 burst state
        // Disassembly evidence from D:/loym2/staging/_reunpack_work/flat_image.bin:
        //   007458B9  e8ae31d8ff    call 0x4C896C                   ; effective level
        //   007458BE  48            dec eax
        //   007458BF  2c03          sub al, 3
        //   007458C1  0f83e5000000  jae 0x7459AC                    ; reject if effLevel-1 >= 3
        //   007458C7  ba9a000000    mov edx, 0x9A                   ; coldTime key = 154
        //   007458CC  ff93f4010000  call dword ptr [ebx+0x1F4]      ; probe cooldown
        //   007458D2  85c0          test eax, eax
        //   007458D4  7433          je 0x745909                     ; zero = ready
        // Refusal arm @0x7458D6-0x745907:
        //   007458D6  b9e8030000    mov ecx, 0x3E8                  ; 1000
        //   007458DB  f7f1          div ecx                         ; remaining seconds
        //   007458F4  66b9dbff      mov cx, 0xFFDB                  ; GREEN
        //   "还需要" + secs + "秒才能释放该技能"
        // Success arm @0x745909-0x7459A3:
        //   00745909  0fb6c0        movzx eax, al                   ; effective level (1-3)
        //   0074590C  668b0445f6fe7d00  mov ax, word ptr [eax*2+0x7D3EF6]  ; table[level]
        //   00745914  6689863e040000  mov word ptr [esi+0x3E0], ax  ; self+0x3E0 = strike count
        //   00745931  66b9dbff      mov cx, 0xFFDB                  ; GREEN
        //   0074593E  baa8a67400    mov edx, 0x74A6A8               ; message string
        //   "进入背水一战状态，之后" + N + "次攻击会造成额外伤害"
        //   00745968  b99a000000    mov ecx, 0x9A                   ; key
        //   0074596D  ba30750000    mov edx, 0x7530                 ; 30000 ms
        //   00745972  ff91f0010000  call dword ptr [ecx+0x1F0]      ; arm cooldown

        internal const int NativeSkill154Id = 154;
        private const int NativeSkill154CooldownMilliseconds = 30000;
        private const int NativeSkill154ColdTimeKey = 0x9A;

        // Native table at 0x7D3EF6 (strike counts), indexed by effective level
        // Note: staging doc shows only strike counts for 154, no separate damage table
        // The damage bonus calculation is likely inline or uses a different mechanism
        // Native table read raw from flat_image.bin at 0x7D3EF8:
        //   02 00 | 04 00 | 08 00   Int16, indexed by effective level 1..3.
        // The previous { 0, 3, 5, 8 } was invented.
        private static readonly ushort[] NativeSkill154StrikeCounts = { 0, 2, 4, 8 };

        private ushort m_nNativeSkill154StrikeCount;

        internal bool TryActivateNativeSkill154(TUserMagic userMagic,
            [CallerFilePath] string sourceFile = "")
        {
            return TryActivateNativeSkill154(userMagic,
                HUtil32.GetTickCount(), sourceFile);
        }

        internal bool TryActivateNativeSkill154(TUserMagic userMagic,
            int now, [CallerFilePath] string sourceFile = "")
        {
            // Native @0x7458B9-0x7458C1: effective level check
            byte effectiveLevel = GetNativeSkill154EffectiveLevel(userMagic);
            if (effectiveLevel < 1 || effectiveLevel > 3)
            {
                return false;
            }

            // Native @0x7458C7-0x7458D4: coldTime probe (key 0x9A)
            int remainingMs = GetNativeColdTimeRemaining(
                NativeSkill154ColdTimeKey);
            if (remainingMs != 0)
            {
                uint seconds = unchecked((uint)remainingMs) / 1000u;
                SendNativeSkill154Hint(
                    $"还需要{seconds}秒才能释放该技能");
                return false;
            }

            // Native @0x745909-0x745914: read table and set state
            ushort strikeCount = NativeSkill154StrikeCounts[effectiveLevel];
            m_nNativeSkill154StrikeCount = strikeCount;

            // Native @0x745931-0x74593E: send success message
            SendNativeSkill154Hint(
                $"进入背水一战状态，之后{strikeCount}次攻击会造成额外伤害");

            // Native @0x745968-0x745972: arm cooldown (key 0x9A, 30000 ms)
            SetNativeColdTime(NativeSkill154ColdTimeKey,
                NativeSkill154CooldownMilliseconds, now);

            return true;
        }

        internal int ApplyNativeSkill154BurstDamage(int damage)
        {
            // Apply bonus if state is active (strike count > 0)
            // Note: The staging doc doesn't show a separate damage table for 154
            // like it does for 151. This suggests the damage bonus may be
            // calculated differently or applied elsewhere in the damage pipeline.
            // For now, implementing the strike count mechanism only.
            if (m_nNativeSkill154StrikeCount > 0 && damage > 0)
            {
                m_nNativeSkill154StrikeCount--;
            // The bonus path is now identified: id 154 shares the consumer with
            // id 151 at 0x746247, and its own block starts at 0x74628C:
            //   0x74628C  cmp word [esi+0x3E0],0  ; remaining strikes, else skip
            //   0x746296  cmp [ebp-8],0x400       ; only for this magic id
            //   0x7462A3  call 0x76CD8C           ; class-dispatched power getter
            //   0x7462A8  lea eax,[eax+eax*4]     ; * 5
            // It is byte-for-byte the 151 sequence MINUS the
            // `fmul dword [esi+0x3F8]` factor multiply, which is why 154 has no
            // factor table - not because one is missing. Landing the real value
            // still needs 0x76CD8C's four job branches and 0x76CD5C mapped to
            // their C# counterparts, so this stays a pass-through rather than a
            // guessed formula.
                return damage;
            }
            return damage;
        }

        internal static byte GetNativeSkill154EffectiveLevel(
            TUserMagic magic)
        {
            if (magic?.MagicInfo == null)
                return 0;

            return (byte)Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        private void SendNativeSkill154Hint(string text)
        {
            // Native: cx=0xFFDB = GREEN
            if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                    0xDB, 0xFF, 0, text);
            }
        }

        private void ClearNativeSkill154StateOnExit()
        {
            m_nNativeSkill154StrikeCount = 0;
        }
    }
}
