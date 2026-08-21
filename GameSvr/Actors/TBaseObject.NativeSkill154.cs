using System.Buffers.Binary;
using System.Diagnostics;
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

        // Native strike-count table at 0x7D3EF8, indexed by effective level.
        // 154 has NO damage-factor table (unlike 151): its consumer arm at
        // 0x74628C multiplies the job max attack by 5 with no fmul, so the
        // bonus is a flat *5 rather than a factor-scaled value.
        //   0x7D3EF8  02 00 | 04 00 | 08 00   Int16, indexed by effective level 1..3.
        // The previous { 0, 3, 5, 8 } was invented.
        private static readonly ushort[] NativeSkill154StrikeCounts = { 0, 2, 4, 8 };

        // Native attack-kind discriminator [ebp-8] gate value @0x746296: id
        // 154's burst fires only for kind 0x400.
        private const int NativeSkill154AttackKind = 0x400;

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

            // Assertion (write-only, not executed here): must equal the raw
            // Int16s at 0x7D3EF8 { 02, 04, 08 }.
            Debug.Assert(strikeCount == effectiveLevel switch
                { 1 => 2, 2 => 4, 3 => 8, _ => 0 },
                "154 strike count diverged from 0x7D3EF8 {02,04,08}");

            m_nNativeSkill154StrikeCount = strikeCount;

            // Native @0x745931-0x74593E: send success message
            SendNativeSkill154Hint(
                $"进入背水一战状态，之后{strikeCount}次攻击会造成额外伤害");

            // Native @0x745968-0x745972: arm cooldown (key 0x9A, 30000 ms)
            SetNativeColdTime(NativeSkill154ColdTimeKey,
                NativeSkill154CooldownMilliseconds, now);

            return true;
        }

        /// <summary>
        /// Native consumer arm @0x74628C, byte-for-byte the id 151 sequence at
        /// 0x746247 MINUS the `fmul dword [esi+0x3F8]` factor multiply — which is
        /// why 154 carries no factor table, not because one is missing:
        ///   0074628C  66 83 BE E0 03 00 00 00  cmp word [esi+0x3E0],0
        ///   00746294  76 22                    jbe 0x7462B8        ; no strikes
        ///   00746296  81 7D F8 00 04 00 00     cmp [ebp-8],0x400   ; attack kind
        ///   0074629D  75 19                    jne 0x7462B8
        ///   0074629F  B2 01                    mov dl,1            ; flag = max
        ///   007462A1  8B C6                    mov eax,esi         ; Self
        ///   007462A3  E8 E4 6A 02 00           call 0x76CD8C       ; job max att
        ///   007462A8  8D 04 80                 lea eax,[eax+eax*4] ; * 5  (NO cap)
        ///   007462AB  89 45 E8                 mov [ebp-0x18],eax
        ///   007462AE  DB 45 E8                 fild dword [ebp-0x18]
        ///   007462B1  E8 CA D2 CB FF           call 0x403580       ; @TRUNC (chop)
        ///   007462B6  03 D8                     add ebx,eax        ; damage += bonus
        /// i.e. bonus = TRUNC(jobMaxAttack * 5). Unlike id 151 there is NO
        /// `mov edx,0x1388 / call 0x4C700C` cap step between the getter and the
        /// `* 5`, so 154 is NOT clamped to 5000. The fild/TRUNC of the integer
        /// product is the identity, so the bonus is simply that product.
        /// <para>
        /// The resolver arm does not decrement +0x3E0. The owning action-1024
        /// routine <c>sub_77136C</c> does that once in its tail:
        /// <c>test edi,edi / jle</c> @0x77143F, counter-positive test
        /// @0x771443, then <c>dec word [self+0x3E0]</c> @0x771453. The live
        /// action-1024 dispatcher therefore calls
        /// <see cref="ConsumeNativeSkill154StrikeAfterPositiveAttackPower"/>
        /// with the pre-delivery power held in EDI. Final applied damage is
        /// irrelevant to this gate.
        /// </para>
        /// </summary>
        internal int ApplyNativeSkill154BurstDamage(int damage, int attackKind)
        {
            // 0x74628C jbe / 0x746296 attack-kind gate. Native does NOT test
            // damage>0 and does NOT decrement the counter here.
            if (m_nNativeSkill154StrikeCount > 0 &&
                attackKind == NativeSkill154AttackKind)
            {
                // 0x76CD8C then `lea eax,[eax+eax*4]` — no 0x4C700C cap for 154.
                int jobMax = GetNativeBurstJobMaxAttack();
                int bonus = unchecked(jobMax * 5);
                return unchecked(damage + bonus);
            }
            return damage;
        }

        /// <summary>
        /// Native action-1024 tail @0x77143F..0x771459. This is deliberately
        /// separate from the damage resolver because EDI is the power rolled
        /// before either direct carrier. Even an immune target spends a charge
        /// when that rolled power is positive; the counter cannot underflow.
        /// </summary>
        internal void ConsumeNativeSkill154StrikeAfterPositiveAttackPower(
            int attackPower)
        {
            if (attackPower > 0 && m_nNativeSkill154StrikeCount > 0)
                m_nNativeSkill154StrikeCount--;
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
