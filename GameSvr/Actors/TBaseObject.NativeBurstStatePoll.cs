using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        // sub_7441EC @0x7441EC — burst-state expiry poll (vtable wrapper 0x690B60).
        // Called from THumanKind.Run on player/hero. For each armed burst counter
        // the native checks coldTime elapsed since activation:
        //   elapsed = 0x7530 - GetNativeColdTimeRemaining(key)
        //   if elapsed > 0x2710 (10s) -> clear counter + SysMsg (GREEN 0xFFDB).
        //
        // Legs modeled here (151/154 already have fields in NativeSkill151/154):
        //   +0x3F4 word  key 0x97  "破釜沉舟状态消失" @0x744324
        //   +0x3E0 word  key 0x9A  "背水一战状态消失" @0x744374
        //
        // Also present in native but unmodeled (no C# strike counters yet):
        //   +0x3F0 dword key 0x98  "绝杀之意状态消失"
        //   +0x3FC word  key 0x99  "无极盾状态消失" (+ Recalc calls)

        private const int NativeBurstStateCooldownTotalMs = 0x7530;   // 30000
        private const int NativeBurstStateExpireElapsedMs = 0x2710; // 10000

        internal void PollNativeBurstStateExpiry()
        {
            PollNativeBurst151Expiry();
            PollNativeBurst154Expiry();
        }

        private void PollNativeBurst151Expiry()
        {
            // 0x7441FA cmp word [+0x3F4],0 / jbe skip
            if (m_nNativeSkill151StrikeCount == 0)
                return;

            // 0x744204 key 0x97; 0x744213..0x744220 elapsed gate
            if (!NativeBurstStateElapsedPastGate(NativeSkill151ColdTimeKey))
                return;

            m_nNativeSkill151StrikeCount = 0;
            m_fNativeSkill151DamageFactor = 0f;
            SendNativeBurstStateExpireHint("破釜沉舟状态消失");
        }

        private void PollNativeBurst154Expiry()
        {
            // 0x7442D4 cmp word [+0x3E0],0 / jbe skip
            if (m_nNativeSkill154StrikeCount == 0)
                return;

            // 0x7442DE key 0x9A; same elapsed gate
            if (!NativeBurstStateElapsedPastGate(NativeSkill154ColdTimeKey))
                return;

            m_nNativeSkill154StrikeCount = 0;
            SendNativeBurstStateExpireHint("背水一战状态消失");
        }

        private bool NativeBurstStateElapsedPastGate(int coldTimeKey)
        {
            int remaining = GetNativeColdTimeRemaining(coldTimeKey);
            int elapsed = NativeBurstStateCooldownTotalMs - remaining;
            return elapsed > NativeBurstStateExpireElapsedMs;
        }

        private void SendNativeBurstStateExpireHint(string text)
        {
            if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xDB, 0xFF, 0, text);
            }
        }
    }
}
