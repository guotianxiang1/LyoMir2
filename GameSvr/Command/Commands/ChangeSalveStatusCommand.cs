using GameSvr.CommandSystem;
using GameSvr.Plugins;
using SystemModule;

namespace GameSvr
{
    // ------------------------------------------------------------------------------------------
    // @Rest — the slave (pet) rest/attack toggle.
    //
    // Command-table record @0x7B6394 (stride 0x120 registry; name ShortString at +0,
    // dispatch index dword at +0x18, required permission dword at +0x1C, help ShortString at +0x20):
    //   0x7B6394  04 52 65 73 74 00 ... 1B 00 00 00 | 00 00 00 00 | 1A C9 E8 D6 ...
    //             len 4 'Rest'          +0x18 = 27    +0x1C = 0     help '设置宠物休息或攻击？'
    //
    // Reached from the single @-command switch sub_622820:
    //   0x622AAE  E8 75 F4 FF FF        call 0x621F28              ; name -> index (perm-gated)
    //   0x622B09  81 FE EE 02 00 00     cmp esi, 0x2EE
    //   0x622B0F  0F 87 33 8B 00 00     ja  0x62B648               ; index > 750 -> default
    //   0x622B15  FF 24 B5 1C 2B 62 00  jmp dword [esi*4 + 0x622B1C]
    //   table slot 0x622B1C + 27*4 = 0x622B88 -> 0x623A42 (this arm)
    //
    // Arm body, in native order:
    //   0x623A42  8B 45 F8              mov eax,[ebp-8]            ; the commanding player
    //   0x623A45  8B 80 FC 04 00 00     mov eax,[eax+0x4FC]        ; slave TList
    //   0x623A4B  83 78 08 00           cmp dword [eax+8], 0       ; TList.FCount
    //   0x623A4F  7F 10                 jg  0x623A61               ; has slaves -> proceed
    //   0x623A54  83 B8 A8 18 00 00 00  cmp dword [eax+0x18A8], 0
    //   0x623A5B  0F 84 EB 7B 00 00     je  0x62B64C               ; neither -> silent no-op
    //   0x623A64  8B 80 28 01 00 00     mov eax,[eax+0x128]        ; map flag record
    //   0x623A6A  80 78 05 00           cmp byte [eax+5], 0        ; the DARE flag
    //   0x623A6E  75 48                 jne 0x623AB8               ; DARE set -> refuse
    //   0x623A73  80 B0 C7 04 00 00 01  xor byte [eax+0x4C7], 1    ; toggle, 1 = resting
    //   0x623A7D  80 B8 C7 04 00 00 00  cmp byte [eax+0x4C7], 0
    //   0x623A84  74 19                 je  0x623A9F
    //   0x623A8A  BA D8 B8 62 00        mov edx, 0x62B8D8          ; '下属行动: 休息'
    //   0x623AA3  BA F0 B8 62 00        mov edx, 0x62B8F0          ; '下属行动: 攻击'
    //   0x623ABC  BA 08 B9 62 00        mov edx, 0x62B908          ; '该地图无法使用'
    // All three receipts go out as `mov cx,0xFFDB` + `call dword [ebx+0xD4]`.
    //
    // [map+5] is the DARE map flag. The flag record base is the same pointer the arm loads from
    // [player+0x128]: sub_690300 does `0x69030B 8B 90 28 01 00 00 mov edx,[eax+0x128]` and then
    // `0x690315 66 83 BA C0 00 00 00 00 cmp word [edx+0xC0],0` on the already-modelled
    // LIMITHEROLEVEL field, so offsets quoted as [flag+n] and [map+n] are the same base.
    // Writers of byte 5, both on the DARE arm of a map-flag parser:
    //   parser A sub_774D98: 0x774F55 BA 94 5C 77 00 mov edx,0x775C94 ('DARE')
    //                        0x774F6A C6 43 05 01    mov byte [ebx+5],1
    //                        0x774F7B C6 43 05 00    mov byte [ebx+5],0   (GM toggle-off arm)
    //   parser B sub_776008: 0x77616B BA B8 6B 77 00 mov edx,0x776BB8 ('DARE')
    //                        0x77617C C6 43 05 01    mov byte [ebx+5],1
    // No other writer of [flag+5] exists in the image.
    //
    // Note: @RestHero (dispatch index 28, arm 0x623AD1) is a DIFFERENT command operating on the
    // hero object [player+0xBB0] through sub_688650. It does not touch [player+0x4C7].
    // ------------------------------------------------------------------------------------------
    [GameCommand("Rest", "调整当前玩家属下状态", 0)]
    public class ChangeSalveStatusCommand : BaseCommond
    {
        [DefaultCommand]
        public void ChangeSalveStatus(TPlayObject PlayObject)
        {
            // 0x623A4B / 0x623A4F: slave TList count must be > 0 (signed jg), otherwise the
            // 0x623A54 fallback is consulted. [player+0x18A8] has no non-zero writer anywhere in
            // the image — the sole store is 0x6B3BC6 `mov [eax+0x18A8],edx` with edx just zeroed
            // by 0x6B3BC4 `xor edx,edx` — so that disjunct is provably always false and the gate
            // reduces to the slave count. Failing it is a silent no-op (0x623A5B je 0x62B64C).
            if (PlayObject.m_SlaveList.Count <= 0)
            {
                return;
            }

            // 0x623A6A / 0x623A6E: the DARE map flag refuses the toggle before it happens.
            if (PlayObject.m_PEnvir.Flag.boDARE)
            {
                PlayObject.SysMsg(M2Share.sPetRestMapForbidden, MsgColor.Green, MsgType.Hint);
                return;
            }

            // 0x623A73 xor byte [eax+0x4C7],1 — 1 = resting, 0 = attacking.
            //
            // 眼神「禁止宝宝休息」替换的**只有这一条指令**。安装点 0x100AABB6
            // call 0x10032FD0，17 字节桩，续跑点 0x00623A7A：
            //   80 B8 15 01 00 00 0F   cmp byte [player+0x115],0x0F
            //   74 07                  je  skip
            //   80 B0 C7 04 00 00 01   xor byte [player+0x4C7],1
            //   E9 <rel32>             jmp 0x00623A7A
            // 两个后果都来自「被换掉的是 xor 而不是 set」：拦的是**两个**切换方向，
            // 不只是「开启休息」；而且续跑点在 0x623A7D 的回执之前，所以下面两条
            // SysMsg 照常发出，播报的是**未被改动**的旧状态。
            if (!new YanshenApi(PlayObject, null, M2Share.PluginManager).IsPetRestBlocked())
            {
                PlayObject.m_boSlaveRelax = !PlayObject.m_boSlaveRelax;
            }

            // 0x623A7D..0x623AB3: the receipt is chosen by the POST-toggle value and is sent
            // unconditionally; the slave-count test already happened in the pre-gate above.
            if (PlayObject.m_boSlaveRelax)
            {
                PlayObject.SysMsg(M2Share.sPetRest, MsgColor.Green, MsgType.Hint);
            }
            else
            {
                PlayObject.SysMsg(M2Share.sPetAttack, MsgColor.Green, MsgType.Hint);
            }
        }
    }
}
