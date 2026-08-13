namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 175 = TStoneFoxBossMon，VMT 0x5F9634，size 1240(0x4D8)，
    //    parent = TAnimal(0x71D51C)，尺寸与父类【完全相同】=> 自身不新增任何字段。
    //    工厂跳表 sub_679F8C：索引表[175-0xB=0xA4]=0x5B=91 ; jt[91]=0x67AB51 ; case body 全文：
    //      67AB51  B2 01              mov  dl,1
    //      67AB53  A1 E8 95 5F 00     mov  eax,[0x5F95E8]   ; classref -> TStoneFoxBossMon
    //      67AB58  E8 CB 2C 0A 00     call 0x71D828         ; = TAnimal.Create（本类**无自有构造器**）
    //      67AB5D  89 45 F8           mov  [ebp-8],eax
    //      67AB60  E9 DA 01 00 00     jmp  0x67AD3F
    //    归属唯一性（穷尽扫描）：classref [0x5F95E8] 全镜像只有 1 个加载点 = 0x67AB53。
    //    ctor 直接就是 TAnimal.Create（= C# AnimalObject()），case 内无额外 RNG、无字段写。
    //
    // VMT 只有两处覆写（父 TAnimal 143→本类 131 槽，逐槽 diff）：
    //   槽 +0x078 Initialize     : 0x5FABA0（父 TAnimal 0x71D904）
    //   槽 +0x0B8 可推挤判定      : 0x5FABD0（父 TAnimal 0x768F50）
    //
    // ── 覆写 1：Initialize(+0x078) @0x5FABA0 全文 ──────────────────────────────
    //   5FABA0  55 8B EC 53           push ebp / mov ebp,esp / push ebx
    //   5FABA4  8B D8                 mov  ebx,eax
    //   5FABA6  8B C3                 mov  eax,ebx
    //   5FABA8  E8 57 2D 12 00        call 0x71D904        ; inherited = TAnimal.Initialize
    //                                 ;   0x71D904 本体：Random(100) < byte[+0x2C8] -> byte[+0x2C9]:=1
    //                                 ;   （= m_btCoolEye / m_boCoolEye 那一掷），再转 TCreature.Initialize
    //                                 ;   sub_7650D8。C# 把这一掷放在 UsrEngn.cs:3125 的
    //                                 ;   `Random(100) < Cert.m_btCoolEye` + 紧随其后的 Cert.Initialize()，
    //                                 ;   次序与原生一致，故此处 base.Initialize() 即对应 sub_7650D8。
    //   5FABAD  C6 83 E1 02 00 00 01  mov  byte [ebx+0x2E1],1   ; m_boSuperMan := true
    //                                 ;   +0x2E1 = m_boSuperMan：TBaseObject.cs:268 已钉；
    //                                 ;   同族 +0x2E0=m_boAdminMode / +0x2E2=m_boObMode /
    //                                 ;   +0x2E3=m_boFixedHideMode / +0x2E5=m_boStoneMode
    //                                 ;   (Envirnoment.cs:1479-1488 已逐条列出)。
    //   5FABB4  C6 43 75 01           mov  byte [ebx+0x75],1    ; m_boStickMode := true
    //                                 ;   +0x75 = m_boStickMode，且本站点 0x5FABB4 就在
    //                                 ;   TBaseObject.NativeSkill265.cs:136 列出的十三个写入者名单里。
    //   5FABB8  6A 01                 push 1                    ; -> [ebp+0xC] = value
    //   5FABBA  6A 00                 push 0                    ; -> [ebp+8]   = flag
    //   5FABBC  83 C9 FF              or   ecx,-1               ; durationMs = -1（永久）
    //   5FABBF  33 D2                 xor  edx,edx              ; stateId = 0
    //   5FABC1  8B C3 / 8B 18         mov  eax,ebx / mov ebx,[eax]
    //   5FABC5  FF 93 EC 01 00 00     call dword [ebx+0x1EC]    ; = AddState(sub_7730D0)
    //   5FABCB  5B 5D C3              pop ebx / pop ebp / ret
    //   形参归属按 sub_7730D0 本体核对（与 TimedAbility.cs:138-143 的 STATE-29 判读同源）：
    //     0x7730E0 `mov edi,[ebp+0xC]` -> value ; 0x77310C `mov dl,[ebp+8]` -> flag ;
    //     0x773121 `mov [eax+2],edx`(edx=[ebp-4]=ecx) -> durationMs ;
    //     0x773127 `mov [eax+0xA],edi` -> value。
    //   即 AddState(stateId=0, durationMs=-1, value=1, flag=0)，与 STATE-29 @0x7732B6
    //   的 `push 1 / push 0 / or ecx,-1` 形状逐字节同构（那处 C# 写作
    //   AddTimedAbilityInternal(19, 1, -1, 0)），故本处 = AddTimedAbilityInternal(0, 1, -1, 0)。
    //
    // ── 覆写 2：+0x0B8 @0x5FABD0 全文 ──────────────────────────────────────────
    //   5FABD0  33 C0                 xor  eax,eax
    //   5FABD2  C3                    ret                       ; 恒返回 false
    //   +0x0B8 是【被推挤方】的虚判定（调用点 0x774204 等 8 处 actor 站点）。
    //   父类实现 sub_768F50 的第二道门 0x768FAE `80 7E 75 00 / 75 12` 读的正是
    //   **自身** m_boStickMode（TBaseObject.NativeSkill265.cs:130-133 已逐条钉死）：
    //   置位即落到 0x768FC6 返回 [ebp-5]=0。本类 Initialize 永久置 m_boStickMode=true，
    //   所以父类实现在上图后的每一条路径上也恒为 false —— 覆写与继承**结果全等**。
    //   C# 侧该判定被移植成调用方私有谓词 CanNativeSkill265Shove(target)，其中
    //   `!target.m_boStickMode` 一项就承担了本覆写的全部效果，故不需要（也不应该）
    //   为本类单独造一个虚方法。fail-closed 备注：若日后把 +0x0B8 还原成 target 侧虚方法，
    //   本类应显式 override 成常量 false。
    //
    // 语义：一尊"石化狐王"——无敌(m_boSuperMan)、不可推动(m_boStickMode)、
    // 永久挂着 0 号身体状态(客户端据此出特效)。除此之外一切走 TAnimal 基类。
    // 原先 race 175 落工厂 default(0x67AE5E) -> 返回 nil，这个 Boss 根本不出现。
    // fail-closed：本类无自有字段、无 Run/AI 覆写，不臆造任何主动行为。
    public class StoneFoxBossMon : AnimalObject
    {
        public StoneFoxBossMon() : base()
        {
        }

        public override void Initialize()
        {
            base.Initialize();
            m_boSuperMan = true;
            m_boStickMode = true;
            AddTimedAbilityInternal(0, 1, -1, 0);
        }
    }
}
