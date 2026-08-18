using SystemModule;
using System.Runtime.CompilerServices;

namespace GameSvr
{
    public partial class TBaseObject
    {
        // Native sub_74633C @0x74633C-0x746455 — id 111 (0x6F) 冰眼巨魔 summon
        // Disassembly evidence from D:/loym2/staging/_reunpack_work/flat_image.bin:
        //   0074636D  2bb310050000  sub esi, dword ptr [ebx+0x510]  ; elapsed since last recall
        //   00746373  81fec0270900  cmp esi, 0x927C0                ; 600000 ms = 10 min cooldown
        //   00746379  0f8288000000  jb  0x746407                    ; reject if < 10 min
        //   0074637F  66baa128      mov dx, 0x28A1                  ; RM_10401 = CheckServerMakeSlave
        //   00746392  6a01          push 1                          ; nMaxMob = 1
        //   00746394  682c010000    push 0x12C                      ; royalty = 300 seconds (5 min)
        //   0074639B  6a0a          push 0xA                        ; hpAfterSlave = 10%
        //   0074639D  call 0x4C896C                               ; effective level
        //   007463B8  ba8c647400    mov edx, 0x74648C               ; "冰眼巨魔1"
        //   007463D7  ff96ec000000  call dword ptr [esi+0xEC]       ; MakeSlave
        //   007463E9  898310050000  mov dword ptr [ebx+0x510], eax  ; stamp recall tick at +0x510
        // Refusal arm @0x746407-0x746448:
        //   00746414  b8c0270900    mov eax, 0x927C0                ; 600000
        //   00746419  2bc6          sub eax, esi                    ; remaining ms
        //   0074641B  b9e8030000    mov ecx, 0x3E8                  ; 1000
        //   00746422  f7f1          div ecx                         ; remaining seconds
        //   00746448  66b9dbff      mov cx, 0xFFDB                  ; GREEN (0xFFDB)
        //   "召唤冰眼巨魔元气尚未回复，还需等待" + secs + "秒"

        internal const int NativeSkill111Id = 111;
        private const int NativeSkill111CooldownMilliseconds = 600000; // 10 minutes
        private const int NativeSkill111RoyaltySeconds = 300; // 5 minutes
        private const int NativeSkill111HpAfterSlave = 10;
        private const string NativeSkill111MonsterBaseName = "冰眼巨魔1";

        private int m_dwNativeSkill111LastRecallTick;

        internal bool TryActivateNativeSkill111IceEyeTrollSummon(
            TUserMagic userMagic,
            [CallerFilePath] string sourceFile = "")
        {
            return TryActivateNativeSkill111IceEyeTrollSummon(userMagic,
                HUtil32.GetTickCount(), sourceFile);
        }

        internal bool TryActivateNativeSkill111IceEyeTrollSummon(
            TUserMagic userMagic, int now,
            [CallerFilePath] string sourceFile = "")
        {
            // Native @0x74634B-0x746355: effective level calculation then
            // dec/sub/jae check (pass only when effLevel-1 < 3)
            byte effectiveLevel = GetNativeSkill111EffectiveLevel(userMagic);
            if (effectiveLevel < 1 || effectiveLevel > 3)
            {
                return false;
            }

            // Native @0x74636D-0x746379: cooldown check self+0x510
            int elapsed = unchecked(now - m_dwNativeSkill111LastRecallTick);
            if (elapsed < NativeSkill111CooldownMilliseconds)
            {
                int remainingMs = NativeSkill111CooldownMilliseconds - elapsed;
                uint seconds = unchecked((uint)remainingMs) / 1000u;
                SendNativeSkill111Hint(
                    $"召唤冰眼巨魔元气尚未回复，还需等待{seconds}秒");
                return false;
            }

            // Native @0x74637F-0x746392: CheckServerMakeSlave guard (RM_10401)
            if (!CheckServerMakeSlave())
            {
                // CheckServerMakeSlave sends its own refusal message
                return false;
            }

            // Native @0x746394-0x7463BD: build level-suffixed name and summon
            // Name = "冰眼巨魔1" + IntToStr(effectiveLevel)
            string monsterName = NativeSkill111MonsterBaseName +
                effectiveLevel.ToString();
            int nMakeLevel = effectiveLevel;
            int nMaxMob = 1;
            int dwRoyaltySec = NativeSkill111RoyaltySeconds;

            // Native @0x7463D7: call [esi+0xEC] = MakeSlave
            var slave = MakeNativeSlave(monsterName, nMakeLevel, nMaxMob,
                dwRoyaltySec, fromHero: false,
                hpAfterSlave: NativeSkill111HpAfterSlave);
            if (slave != null)
            {
                // Native @0x7463E9: stamp the recall tick at +0x510
                m_dwNativeSkill111LastRecallTick = now;
                // Native @0x746400: call sub_675AF4 (unknown post-summon hook)
                // Omitted: unknown semantics, likely non-critical display effect
                return true;
            }
            else
            {
                // MakeSlave returned null; native does not explicitly handle
                // this but implicitly fails (no tick stamp, no success)
                return false;
            }
        }

        internal static byte GetNativeSkill111EffectiveLevel(
            TUserMagic magic)
        {
            if (magic?.MagicInfo == null)
                return 0;

            // Native sub_4C896C: effective = min(btLevel + bonus, btTrainLv)
            return (byte)Math.Min(
                unchecked((byte)(magic.btLevel + magic.NativeLevelBonus)),
                magic.MagicInfo.btTrainLv);
        }

        private void SendNativeSkill111Hint(string text)
        {
            // Native @0x746448: cx=0xFFDB = GREEN
            // 0xFFDB unpacks as FColor=0xDB, BColor=0xFF
            if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                    0xDB, 0xFF, 0, text);
            }
        }

        private void ClearNativeSkill111StateOnExit()
        {
            m_dwNativeSkill111LastRecallTick = 0;
        }
    }
}
