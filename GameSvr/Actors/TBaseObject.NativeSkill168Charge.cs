using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 168 冲锋陷阵. Outer arm 0x6BCA99
    /// `E8 D2 25 03 00 call 0x6EF07C`. Same mounted-state colour as 167
    /// (`66 B9 FF FC`) plus a fifth 0x2D gate. Chebyshev (sub_76B4A4)
    /// `83 F8 06 / 0F 87` unsigned, so range 0..6 inclusive.
    /// </summary>
    public partial class TBaseObject
    {
        private const int NativeSkill168ColdTimeKey = 0xA8;
        private const int NativeSkill168CooldownMilliseconds = 0x493E0;
        private const int NativeSkill168MaxChebyshev = 6;
        private const byte NativeSkill168RequiredState = 0x33;
        private const int NativeSkill168HintColorLow = 0xFF;
        private const int NativeSkill168HintColorHigh = 0xFC;

        internal bool TryActivateNativeSkill168Charge(int targetX, int targetY)
        {
            return TryActivateNativeSkill168Charge(targetX, targetY,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill168Charge(int targetX, int targetY,
            int now)
        {
            if (!HasNativeActiveState(NativeSkill168RequiredState))
            {
                SendNativeSkill168Hint("当前未骑马，无法施放冲锋陷阵技能");
                return false;
            }
            if (HasNativeActiveState(0x3E))
            {
                SendNativeSkill168Hint("当前处于凝冰状态，无法施放冲锋陷阵技能");
                return false;
            }
            if (HasNativeActiveState(0x1A))
            {
                SendNativeSkill168Hint("当前处于麻痹状态，无法施放冲锋陷阵技能");
                return false;
            }
            if (HasNativeActiveState(0x18))
            {
                SendNativeSkill168Hint("当前处于蛛网状态，无法施放冲锋陷阵技能");
                return false;
            }
            if (HasNativeActiveState(0x2D))
            {
                SendNativeSkill168Hint("当前处于定身状态，无法施放冲锋陷阵技能");
                return false;
            }

            if (GetNativeColdTimeRemaining(NativeSkill168ColdTimeKey) != 0)
            {
                return false;
            }

            int dx = m_nCurrX - targetX;
            if (dx < 0)
                dx = -dx;
            int dy = m_nCurrY - targetY;
            if (dy < 0)
                dy = -dy;
            int chebyshev = dx >= dy ? dx : dy;
            if (unchecked((uint)chebyshev) > NativeSkill168MaxChebyshev)
            {
                SendNativeSkill168UnreachableHint();
                return false;
            }

            var envir = m_PEnvir;
            if (envir == null || !envir.CanWalk(targetX, targetY, true))
            {
                SendNativeSkill168UnreachableHint();
                return false;
            }

            // 0x6EF193 `E8 C0 96 08 00 call 0x778858` / `85 C0 / 7F 72` —
            // sub_778858 names itself TEnvironment.GetMovObjCount in its own
            // exception text at 0x778A7C, and any live mover on the cell
            // refuses the charge.
            if (envir.GetNativeMovObjCount(targetX, targetY) > 0)
            {
                SendNativeSkill168UnreachableHint();
                return false;
            }

            // 0x7797CC with the literal 1 (`6A 01` at 0x6EF1A3) = boFlag true,
            // so occupancy is not re-checked inside the mover; the call above
            // is what guards it.
            if (envir.MoveToMovingObject(m_nCurrX, m_nCurrY, this,
                    targetX, targetY, true) <= 0)
            {
                SendNativeSkill168UnreachableHint();
                return false;
            }

            SetNativeColdTime(NativeSkill168ColdTimeKey,
                NativeSkill168CooldownMilliseconds, now);
            m_nCurrX = (short)targetX;
            m_nCurrY = (short)targetY;
            // 0x6EF1FE `66 BA E5 0D` + VMT+0xE0 with boSendSelf = 1. Series is
            // the literal `68 0A 01 00 00` = 0x10A pushed at 0x6EF1F1, i.e.
            // magic 266's id and not 168's — that is what the bytes say.
            SendRefMsg(Grobal2.RM_NATIVE_BLINK_MOVE,
                NativeSkill266ColdTimeKey, targetX, targetY, 0, "");
            return true;
        }

        private void SendNativeSkill168Hint(string text)
        {
            if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                    NativeSkill168HintColorLow, NativeSkill168HintColorHigh,
                    0, text);
            }
        }

        /// <summary>0x6EF20E `66 B9 FF 38` — MsgColor.Red.</summary>
        private void SendNativeSkill168UnreachableHint()
        {
            if (this is TPlayObject)
            {
                SysMsg("目标位置不可达，技能使用失败", MsgColor.Red, MsgType.Hint);
            }
        }
    }
}
