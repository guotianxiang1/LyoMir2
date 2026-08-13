namespace GameSvr
{
<<<<<<< HEAD
    // ✅ 战神字节证据 (Tier-1)：race 181 = TStoneMonster，VMT 0x65E2BC，size 0x4E8(1256)，
    //    parent = TMonster(0x65E030，同为 0x4E8) —— 子类【零新增字段】。
    //    工厂跳表 sub_679F8C：索引表[181-0xB=0xAA]=0x61=97 ; jt[97]=0x67ABC9 ; case 全文：
    //      67ABC9  B2 01              mov  dl,1
    //      67ABCB  A1 70 E2 65 00     mov  eax,[0x65E270]   ; classref(=VMT-0x4C) -> TStoneMonster
    //      67ABD0  E8 0B 25 FF FF     call 0x66D0E0         ; TStoneMonster.Create
    //      67ABD5  89 45 F8           mov  [ebp-8],eax
    //      67ABD8  E9 62 01 00 00     jmp  0x67AD3F
    //    case 内无额外 RNG。
    //
    // 构造器 sub_66D0E0 全文（父 ctor + 一个字段写）：
    //   66D0F5  33 D2              xor  edx,edx
    //   66D0F7  8B C6              mov  eax,esi
    //   66D0F9  E8 0E 90 FF FF     call 0x66610C            ; = TMonster.Create
    //   66D0FE  C6 86 E4 04 00 00 01
    //                              mov  byte [esi+0x4E4],1  ; <== 唯一自定义写
    //   66D105..66D11F             epilogue / ret
    //   [+0x4E4] 是 TMonster 自有字段：TMonster.Create 0x66612A 写 0，本类写 1，全镜像
    //   再无第三个写入点；唯一读取点是 TMonster.Run sub_66622C @0x666302
    //   `cmp byte [edx+0x4E4],0 / jne 0x6666E0`（见 Monster.cs 的 m_boNativeStaticMode）。
    //
    // 唯一 VMT 覆写：slot[34] +0x088 = Run = 0x66D120（父 TMonster 处为 0x66622C），全文：
    //   66D120  55 8B EC           push ebp / mov ebp,esp
    //   66D123  E8 04 91 FF FF     call 0x66622C            ; inherited TMonster.Run
    //   66D128  5D C3              pop ebp / ret
    //   —— 空覆写，只调 inherited，C# 不需要写这个 override（写了反而多一层）。
    //
    // 语义：石头怪把 TMonster.Run 的"行走/攻击/跟随/召回/游荡"整块跳过，只剩 inherited
    //   TAnimal.Run（状态计时、死亡处理、被打反应仍走基类）。也就是一尊会挨打、会死、
    //   但永远不动不打人的石像。
    // fail-closed：VMT 差异集只有那一个空覆写，构造器只有那一条字段写，不臆造任何额外行为。
=======
    // ✅ 战神字节证据 (Tier-1)：race 181 = TStoneMonster，VMT 0x65E2BC，size 1256(0x4E8)，
    //    parent = TMonster(0x65E030)，尺寸与父类【完全相同】=> 自身不新增任何字段。
    //    工厂跳表 sub_679F8C：索引表[181-0xB=0xAA]=0x61=97 ; jt[97]=0x67ABC9 ; case body 全文：
    //      67ABC9  B2 01              mov  dl,1
    //      67ABCB  A1 70 E2 65 00     mov  eax,[0x65E270]   ; classref -> TStoneMonster
    //      67ABD0  E8 0B 25 FF FF     call 0x66D0E0         ; TStoneMonster.Create
    //      67ABD5  89 45 F8           mov  [ebp-8],eax
    //      67ABD8  E9 62 01 00 00     jmp  0x67AD3F         ; 汇入工厂公共尾部
    //    归属唯一性（穷尽扫描，非推测）：
    //      · classref 全局 [0x65E270] 在整个 CODE 段只有 1 个加载点 = 0x67ABCB
    //      · ctor sub_66D0E0 的 E8 rel32 调用者全扫 = 1 个 = 0x67ABD0
    //    case 内无额外 RNG、无额外字段写。
    //
    // 构造器 sub_66D0E0 全文（唯一自定义写就是一条置位）：
    //   66D0E0  55 8B EC 53 56        push ebp / mov ebp,esp / push ebx / push esi
    //   66D0E5  84 D2 / 74 08         test dl,dl / je  0x66D0F1      ; Delphi 分配壳
    //   66D0E9  83 C4 F0              add  esp,-0x10
    //   66D0EC  E8 17 79 D9 FF        call 0x404A08
    //   66D0F1  8B DA / 8B F0         mov  ebx,edx / mov esi,eax
    //   66D0F5  33 D2                 xor  edx,edx
    //   66D0F7  8B C6                 mov  eax,esi
    //   66D0F9  E8 0E 90 FF FF        call 0x66610C                  ; = TMonster.Create (inherited)
    //   66D0FE  C6 86 E4 04 00 00 01  mov  byte [esi+0x4E4],1        ; <== 全部差异只有这一条
    //   66D105..66D11F                epilogue / ret
    //
    // 唯一 VMT 覆写是 Run(+0x088)=0x66D120，全文只有 5 条：
    //   66D120  55 8B EC              push ebp / mov ebp,esp
    //   66D123  E8 04 91 FF FF        call 0x66622C                  ; = TMonster.Run(+0x088)
    //   66D128  5D C3                 pop ebp / ret
    //   —— 这是 Delphi 的 `override Run; begin inherited; end;`，无任何附加行为，
    //      故 C# 侧不需要写 Run 覆写（与 ParalyzationMon.cs 的空覆写处理一致）。
    //
    // [+0x4E4] 的语义（三站穷尽，TMonster 布局下只有这三处碰它）：
    //   · 0x66612A  TMonster.Create      `mov byte [esi+0x4E4],0`   —— 默认 false
    //   · 0x66D0FE  TStoneMonster.Create `mov byte [esi+0x4E4],1`   —— 本类置 true
    //   · 0x666302  TMonster.Run         `cmp byte [edx+0x4E4],0 / jne 0x6666E0`
    //   TMonster.Run 里它的位置在【等待步冷却到期判定之后、m_boWalkWaitLocked 判定之前】：
    //      6662CE  E8 6D 20 DA FF        call 0x408340              ; now = GetTickCount
    //      6662D6  80 BA D8 04 00 00 00  cmp byte [edx+0x4D8],0     ; m_boWalkWaitLocked
    //      6662DD  74 20                 je   0x6662FF
    //      6662E4  2B 8A DC 04 00 00     sub  ecx,[edx+0x4DC]       ; now - m_dwWalkWaitTick
    //      6662ED  3B 8A C0 02 00 00     cmp  ecx,[edx+0x2C0]       ; m_dwWalkWait
    //      6662F3  76 0A                 jbe  0x6662FF
    //      6662F8  C6 82 D8 04 00 00 00  mov  byte [edx+0x4D8],0    ; 到期解锁
    //      6662FF  80 BA E4 04 00 00 00  cmp  byte [edx+0x4E4],0    ; <== 本闸
    //      666309  0F 85 D1 03 00 00     jne  0x6666E0              ; 真 -> 直接跳公共尾部
    //      66630F  80 BA D8 04 00 00 00  cmp  byte [edx+0x4D8],0
    //      666319  0F 85 C1 03 00 00     jne  0x6666E0
    //      66631F  2B 8A 84 03 00 00     sub  ecx,[edx+0x384]       ; now - m_dwWalkTick
    //      66632D  3B 8A 24 03 00 00     cmp  ecx,[edx+0x324]       ; m_nWalkSpeed
    //      666333  0F 8E A7 03 00 00     jle  0x6666E0
    //   尾部 0x6666E0 只有 `call 0x71E50C`(= TAnimal.Run) 然后返回。
    //   即：Think(+0x20C 虚派发, @0x6662A1) 照常跑；一旦 Think 之后进到走位段，
    //   本闸把【走位/索敌走向/攻击目标/游荡/主人召回】整段全部跳过，只剩 inherited TAnimal.Run。
    //   语义 = 一只永远不移动、不主动出手，但仍在场、仍会被打、仍走 TAnimal 基础帧的"石怪"。
    //   原先 race 181 落工厂 default(0x67AE5E) -> 返回 nil，这个怪根本不出现。
    //
    // fail-closed：TStoneMonster 无自有字段、无自有方法体，除上述一位一闸外不臆造任何行为。
>>>>>>> w/race-c
    public class StoneMonster : Monster
    {
        public StoneMonster() : base()
        {
<<<<<<< HEAD
            m_boNativeStaticMode = true;
=======
            bo4E4 = true;
>>>>>>> w/race-c
        }
    }
}
