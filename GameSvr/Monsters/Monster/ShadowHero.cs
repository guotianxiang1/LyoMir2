using SystemModule;

namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 129 = TShadowHero，classref 0x719F2C → VMT 0x719F78，
    //    size 0x60C，classname "TShadowHero"，parent = TAIMon（= C# AiMon）。
    //    工厂 sub_679F8C handler 0x67A831 全文（20 字节，无额外 case-body 逻辑）：
    //      67A831  B2 01              mov  dl,1
    //      67A833  A1 2C 9F 71 00     mov  eax,[0x719F2C]   ; classref -> TShadowHero
    //      67A838  E8 93 0C 0A 00     call 0x71B4D0         ; TShadowHero.Create
    //      67A83D  89 45 F8           mov  [ebp-8],eax
    //      67A840  E9 FA 04 00 00     jmp  0x67AD3F
    //
    // 构造器 sub_71B4D0 全文（Delphi 序幕/收尾略）：
    //   71B4EA  E8 2D FA FF FF                 call 0x71AF1C  ; = TAIMon.Create (= base() = AiMon())
    //   71B4EF  C6 87 AC 03 00 00 01           mov byte [edi+0x3AC],1        ; ← fail-closed（无 C# 字段）
    //   71B4F6  C7 87 F4 05 00 00 D0 07 00 00  mov dword[edi+0x5F4],0x7D0    ; ← 新字段（无 C# 落点）
    //   71B500  B2 01 / A1 8C 1E 42 00 / E8 …  mov dl,1 / eax=[0x421E8C] / call 0x404660  ; 造内部 TList
    //   71B50C  89 87 F8 05 00 00              mov [edi+0x5F8],eax           ; ← 新字段（TList，无 C# 落点）
    //   71B512  C6 87 55 01 00 00 93           mov byte [edi+0x155],0x93     ; m_btNameColor = 0x93(147)
    //   71B519  C6 87 E7 02 00 00 01           mov byte [edi+0x2E7],1        ; ← fail-closed（无 C# 字段）
    //   71B520  E8 1B CE CE FF                 call 0x408340  ; GetTickCount -> esi
    //   71B527  89 B7 00 06 00 00              mov [edi+0x600],esi           ; ← 新字段
    //   71B52D  89 B7 5C 03 00 00              mov [edi+0x35C],esi           ; m_dwHitTick = tick(新取值)
    //   71B539  89 87 F0 05 00 00              mov [edi+0x5F0],eax           ; ← 新字段 = tick
    //   71B53F  B8 08 00 00 00 / E8 … / 83 C0 05  eax = Random(8)+5
    //   71B54C  89 87 04 06 00 00              mov [edi+0x604],eax           ; ← 新字段 = Random(8)+5
    //   71B552  81 87 84 03 00 00 D0 07 00 00  add dword[edi+0x384],0x7D0    ; m_dwWalkTick += 2000
    //   偏移锚点：+0x155=m_btNameColor（TBaseObject.cs:250 / ChgNameClrCommand.cs:10）；
    //     +0x35C=m_dwHitTick、+0x384=m_dwWalkTick（AnimalObject.cs:48/52）。
    //   忠实落地：m_btNameColor=0x93；m_dwHitTick=GetTickCount()（覆盖 base 值）；m_dwWalkTick+=2000。
    //
    // ── fail-closed（有字节、C# 无落点，不臆造）─────────────────────────────────
    //  · [+0x3AC]=1 / [+0x2E7]=1 两个 bool：C# 侧无对应字段（+0x3AC 同 SkyArcher.cs:35 处置，仅记录）。
    //  · 新字段 [+0x5F0]/[+0x5F4]/[+0x5F8](TList)/[+0x600]/[+0x604]/[+0x608]：唯一消费者是下列
    //    fail-closed 覆写，故不落 C# 字段（避免死代码）。
    //  · VMT 差分（child 0x719F78 vs parent TAIMon 0x719CF4）共 11 项真实覆写；除 Die 外全 fail-closed：
    //     slot8  +0x020 IsAttackTarget -> 0x71B790：`mov ecx,[eax+0x608] / test / jne` —— 门控读新字段
    //        +0x608 → fail-closed（不同于 SkyArcher 的纯 race 判定）。
    //     slot25 +0x064 -> 0x71B6EC (parent 0x765CE0)   slot26 +0x068 -> 0x71C914 (parent 0x772F34)
    //     slot34 +0x088 Run -> 0x71B830：读 +0x484/+0x488(AI 闸)、+0x608(新字段) 并解引用 [+0x608]+0x330
    //        → fail-closed（退化为 AnimalObject.Run）。
    //     slot35 +0x08C RecalcAbilitys -> 0x71C910 (parent 0x71DF70=属性重算)   slot36 +0x090 -> 0x71B704
    //     slot37 +0x094 -> 0x71C354   slot45 +0x0B4 -> 0x71C8EC (parent 0x769910=主人链解析)
    //     slot101 +0x194 -> 0x71C37C (parent 0x769F8C)
    //     slot130 +0x208 Struck -> 0x71C1E4：`call base.Struck(0x71E208)` 后读 +0x608/+0x5F0/+0x5F4
    //        新字段并调 sub_71AEF0/sub_71C2B8/sub_767498/sub_76719C → fail-closed。
    //
    // 综上：本类忠实落地【类存在 + 构造器 + Die(slot33)】；其余 10 个覆写 fail-closed（附 VA+原因）。
    public class ShadowHero : AiMon
    {
        public ShadowHero() : base()
        {
            m_btNameColor = 0x93;                    // 71B512  mov byte [edi+0x155],0x93
            m_dwHitTick = HUtil32.GetTickCount();    // 71B520/71B52D  GetTickCount -> [+0x35C]
            m_dwWalkTick += 2000;                    // 71B552  add dword [edi+0x384],0x7D0
        }

        // 战神 VMT+0x084 (slot33) = Die = sub_71B5E0（23 字节，全文）：
        //   71B5E5  mov ebx,eax               ; ebx = Self
        //   71B5E7  mov eax,[ebx+0x12C] / push ; m_nCurrX
        //   71B5EE  mov eax,[ebx+0x130] / push ; m_nCurrY
        //   71B5F5  push 0 / push 0 / push 0
        //   71B5FB  xor ecx,ecx               ; wParam = 0
        //   71B5FD  mov dx,0x2970             ; wIdent = 10608
        //   71B605  call [self.vmt+0xD8]      ; = SendRefMsg
        //   71B60D  call 0x768060            ; = MakeGhost（sub_768060，VolumeSkins.cs:63 印证）
        //   71B615  ret
        // 本覆写【不调用】base.Die(0x71E2BC) —— 完整替换：只广播 RM 10608 再化为 ghost（英雄引擎
        // 死亡=就地消散，不走怪物掉落结算）。SendRefMsg 入参序按 AttackIceTower.cs:128-132 已验证
        // 范式（首个压栈值=nParam1）：nParam1=m_nCurrX、nParam2=m_nCurrY、nParam3=0、sMsg=""。
        // RM 10608(dx=0x2970) 无命名常量，用字节原值。
        public override void Die()
        {
            SendRefMsg(10608, 0, m_nCurrX, m_nCurrY, 0, "");
            MakeGhost();
        }
    }
}
