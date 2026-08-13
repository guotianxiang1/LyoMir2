using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// MOVE-11 — native sub_7742C0(eax = Self), the "acting reveals you" hook.
    /// Full body, every offset from its own instruction:
    ///
    ///   0x7742D6  B2 40                 mov dl,0x40
    ///   0x7742DA  E8 81 E6 FF FF        call 0x772960      ; InBodyState(0x40)
    ///   0x7742DF  84 C0 / 74 42         test al,al / je 0x774325   ; not hidden -> no-op
    ///   0x7742E3  B2 40                 mov dl,0x40
    ///   0x7742E7  E8 D4 EE FF FF        call 0x7731C0      ; clear state 0x40 + unlink node
    ///   0x7742EC  8B 83 2C 01 00 00     mov eax,[Self+0x12C]   ; push nParam1 = X
    ///   0x7742F3  8B 83 30 01 00 00     mov eax,[Self+0x130]   ; push nParam2 = Y
    ///   0x7742FA  6A 00                 push 0                 ; nParam3
    ///   0x774303  FF 91 90 00 00 00     call [vmt+0x90]        ; GetShowName -> sMsg
    ///   0x77430D  6A 01                 push 1                 ; boFlag: include Self
    ///   0x774311  8A 8B 54 01 00 00     mov cl,[Self+0x154]    ; wParam = direction
    ///   0x774317  66 BA 11 27           mov dx,0x2711          ; RM_TURN (10001)
    ///   0x77431F  FF 93 D8 00 00 00     call [vmt+0xD8]        ; SendRefMsg
    ///
    /// State 0x40 is the 隐身术 (magic 261) stealth flag: sub_774288 hides its
    /// holder from any viewer more than 2 cells away (0x774291 `B2 40` +
    /// 0x772960, 0x7742AC Chebyshev, 0x7742B1 `83 F8 02 / 77 04`), and both
    /// broadcast slots consult it per viewer (0x6DC247 in VMT+0xE0, 0x6DC6F1 in
    /// VMT+0xD8). So this hook is "step out of stealth and re-show yourself at
    /// your current cell", which is why it re-sends the whole RM_TURN identity
    /// tuple (direction / X / Y / show-name) with boFlag = 1 so the actor's own
    /// client re-renders too.
    ///
    /// The state is applied only by TryActivateNativeSkill261
    /// (AddTimedAbilityInternal(0x40, ...)) and nothing in this port ever took
    /// it back down early, so before this hook a stealthed player stayed
    /// invisible for the full skill duration no matter what he did.
    ///
    /// sub_7731C0 and RemoveTimedAbilityInternal are the same routine term for
    /// term: `InBodyState` guard (0x7731D8) / btr via 0x7729A8 (0x7731F2) /
    /// walk [Self+0xDC] matching `byte[node+1]` (0x7731FE) / unlink through the
    /// saved predecessor or the head (0x773209, 0x773216) / state-lost virtual
    /// [vmt+0x5C] = 0x741578 (0x773227) / free the node (0x77322C).
    ///
    /// rel32 census of sub_7742C0 — exactly 4 call sites:
    ///   0x6D9CE7  CM_RUN(3013), the handler's very first instruction pair
    ///             (0x6D9CE4 `mov eax,[ebp-4]`), i.e. ahead of even the state
    ///             0x34 passenger gate at 0x6D9CEC. This one is ours.
    ///   0x6DA008  CM 4105 worker 1 — whole leaf is fail-closed
    ///             (NativeCmQ3FailClosed / TPlayObject.HeroNotify.cs).
    ///   0x6F2D62  inside sub_6F2D48(Self, Ident): `call 0x772E24` then
    ///             `cmp esi,0x10B / je` skips the reveal for that one ident.
    ///             sub_6F2D48 is entered from 0x6D9EB4 (HIT family) and
    ///             0x6DA04A (CM_SPELL 3017) — not move arms, left alone.
    ///   0x768CFB  sub_768CEC, not a CM leaf; left alone.
    ///
    /// CM_WALK(3011) deliberately does NOT get this: its handler starts at
    /// 0x6D9BD0 with `mov dl,0x34` and never calls sub_7742C0. Walking keeps
    /// stealth, running drops it.
    /// </summary>
    public partial class TBaseObject
    {
        internal bool BreakNativeStealthOnAction()
        {
            // 0x7742D6..0x7742E1
            if (!HasNativeActiveState(NativeSkill261State))
            {
                return false;
            }

            // 0x7742E3 / 0x7742E7
            RemoveTimedAbilityInternal(NativeSkill261State);

            // 0x7742EC..0x77431F. nParam3 is the literal `6A 00` at 0x7742FA;
            // boFlag = 1 (0x77430D) is the "do not skip Self" term that
            // 0x6DC238 `or al,byte[ebp+8]` reads, which is what C# SendRefMsg
            // already does since Self sits in its own scan cell.
            SendRefMsg(Grobal2.RM_TURN, m_btDirection, m_nCurrX, m_nCurrY, 0,
                GetShowName());
            return true;
        }
    }
}
