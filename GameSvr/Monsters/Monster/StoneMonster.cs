namespace GameSvr
{
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
    public class StoneMonster : Monster
    {
        public StoneMonster() : base()
        {
            m_boNativeStaticMode = true;
        }
    }
}
