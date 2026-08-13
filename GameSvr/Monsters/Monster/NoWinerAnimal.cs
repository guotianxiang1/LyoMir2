namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 152 = TNoWinerAnimal，VMT 0x664F58，size 1256 (0x4E8)，
    //    parent = TATMonster(0x65E7E4，size 亦为 1256) —— **自身零新增字段**。
    //
    // 工厂 sub_679F8C 两级分派：索引表[152-0xB=0x8D] = 0x46 = 70 ; jt[70] = 0x67A9AD。
    // case body 0x67A9AD..0x67A9C0 全文 (20 字节)：
    //   67A9AD  B2 01              mov  dl,1
    //   67A9AF  A1 0C 4F 66 00     mov  eax,[0x664F0C]   ; classref -> TNoWinerAnimal
    //   67A9B4  E8 83 1F FF FF     call 0x66C93C         ; TNoWinerAnimal.Create
    //   67A9B9  89 45 F8           mov  [ebp-8],eax
    //   67A9BC  E9 7E 03 00 00     jmp  0x67AD3F         ; 汇入公共尾部
    // case 内【无】任何额外 RNG / 字段写。
    // 归属唯一性（穷尽判据）：
    //   · classref [0x664F0C] 全 CODE 段加载点 = 1 处 (0x67A9B0)
    //   · ctor 0x66C93C 的 E8 rel32 调用者全扫 = 1 处 (0x67A9B4)
    //
    // 构造器 sub_66C93C 全文 (64 字节)：
    //   66C93C  55 8B EC 53 56                push ebp / mov ebp,esp / push ebx / push esi
    //   66C941  84 D2 / 74 08 / 83 C4 F0 / E8 BB 80 D9 FF   Delphi ctor 序幕 (dl=1 → 分配)
    //   66C94D  8B DA / 8B F0                 ebx=dl 标志 / esi=Self
    //   66C951  33 D2                         xor edx,edx
    //   66C953  8B C6                         mov eax,esi
    //   66C955  E8 3E A1 FF FF                call 0x666A98   ; = TATMonster.Create
    //                                         ;   (C# AtMonster()：m_dwSearchTime = Random(1500)+1500)
    //   66C95A  C6 86 78 01 00 00 98          mov byte [esi+0x178],0x98   ; = 152
    //   66C961..66C97B                        ctor 收尾 / ret
    // [+0x178] == C# m_btRaceServer：同一惯用法见 GameSvr/Monsters/Monster.cs:16
    // (TMonster.Create 0x666162 写 0x50 = RC_MONSTER) 与 TAnimal.Create 0x71D851 写 0x32。
    // 与那两处一样，MonInitialize 之后会被 DB 记录覆盖，这里只是出生默认值。
    //
    // VMT 差分 (全 132 槽 + 8 个负偏移标准槽逐槽比对 parent TATMonster)：只有两项，
    // 且两项都落在 C# 侧【没有对应虚方法】的槽上，故 fail-closed，不臆造：
    //
    //   +0x1B4 -> 0x66C994 (parent TCreature 0x76C35C = 受击伤害 AC/MAC 选择器)
    //     0066C994  55 8B EC 56 57        push ebp/mov ebp,esp/push esi/push edi
    //     0066C999  8B F0                 mov esi,eax          ; Self
    //     0066C99B  33 FF                 xor edi,edi          ; result = 0
    //     0066C99D  8B C1                 mov eax,ecx          ; ecx = 技能/伤害 kind (word)
    //     0066C99F  66 83 E8 16 / 74 17   sub ax,0x16  / je  -> 直接返回 0
    //     0066C9A5  66 83 E8 69 / 74 11   sub ax,0x69  / je  -> 直接返回 0   (0x16+0x69 = 0x7F)
    //     0066C9AB  8B 45 0C / 50         push [ebp+0xC]
    //     0066C9AE  8B 45 08 / 50         push [ebp+8]
    //     0066C9B3  8B C6 / E8 A2 F9 0F 00 call 0x76C35C       ; 其余 kind 走基类
    //     0066C9BA  8B F8 / 8B C7         edi=eax / eax=edi
    //     0066C9BE  5F 5E 5D C2 08 00     ret 8
    //     语义 = kind 22 与 kind 127 这两种伤害对本怪【恒为 0】。
    //     fail-closed 原因：C# 把 +0x1B4 拆成了两个 **非虚** 的具体函数
    //     (TBaseObject.GetHitStruckDamage / GetMagStruckDamage)，没有对应可覆写的入口；
    //     新造一个虚方法会改动 TBaseObject 公共签名，超出本轮范围。
    //
    //   +0x1FC -> 0x66C97C (parent TAnimal 0x71F46C = 死亡结算/散落转发器)
    //     0066C97C  55 8B EC              push ebp / mov ebp,esp
    //     0066C97F  6A 00                 push 0                ; 第二个栈参 强制为 0
    //     0066C981  8A 55 08 / 52         mov dl,[ebp+8] / push edx   ; 第一个栈参 = 原样透传(取低字节)
    //     0066C985  8B C8 / 8B D0         mov ecx,eax / mov edx,eax   ; edx=ecx=Self
    //     0066C989  E8 DE 2A 0B 00        call 0x71F46C
    //     0066C98E  5D C2 08 00           ret 8
    //     基类 0x71F46C 尾部 @0x71F491 call 0x71FA20 (怪物掉落结算)，两个栈参见
    //     GameSvr/Actors/TBaseObject.cs:5143 的注释（怪物死亡路径压 (1,0)）。
    //     fail-closed 原因：C# 侧 ScatterGolds(...) 不是虚方法，+0x1FC 无可覆写入口。
    //
    // 综上：本类忠实落地的是【类的存在 + 构造器】。原先 race 152 落工厂 default
    // (0x67AE5E `xor eax,eax`) → 返回 nil，该怪根本不出现。
    public class NoWinerAnimal : AtMonster
    {
        public NoWinerAnimal() : base()
        {
            m_btRaceServer = 152;
        }
    }
}
