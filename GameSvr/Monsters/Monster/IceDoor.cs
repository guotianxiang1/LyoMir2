using SystemModule;

namespace GameSvr
{
    // ✅ 战神字节证据 (Tier-1)：race 144 = TIceDoor，VMT 0x66E6AC，size 0x4D8，
    //    parent = TAnimal(0x71D51C)。VMT 与父类逐槽相同——**零覆写**（mvmt.py slots
    //    TIceDoor 输出无 diff 行），所以行为 = 纯 TAnimal（C# AnimalObject）+ 构造字段。
    //    工厂跳表 sub_679F8C：索引表[144-0xB=0x85]=0x3E=62 ; jt[62]=0x67A90D ; case 全文：
    //      67A90D  B2 01              mov  dl,1
    //      67A90F  A1 60 E6 66 00     mov  eax,[0x66E660]   ; classref -> TIceDoor
    //      67A914  E8 D7 A2 FF FF     call 0x674BF0         ; TIceDoor.Create
    //      67A919  89 45 F8           mov  [ebp-8],eax
    //      67A91C  E9 1E 04 00 00     jmp  0x67AD3F
    //    归属唯一性：classref [0x66E660] 全 CODE 段仅 1 处加载(0x67A90F)。case 内无额外 RNG。
    //
    // 构造器 sub_674BF0 全文（call 父 TAnimal.Create 后 4 个字段写）：
    //   674C09  E8 1A 8C 0A 00        call 0x71D828         ; = TAnimal.Create (= C# AnimalObject())
    //   674C0E  C6 46 75 01           mov  byte [esi+0x75],1      ; m_boStickMode = true
    //   674C12  66 C7 86 6C 02 00 00  mov  word [esi+0x26C],0xFA  ; m_wEffectResistance = 250
    //           FA 00
    //   674C1B  C6 86 54 01 00 00 00  mov  byte [esi+0x154],0     ; m_btDirection = 0
    //   674C22  33 C0 / 89 46 78      xor eax,eax / mov [esi+0x78],eax ; m_nViewRange = 0
    //   674C29..                       epilogue / ret
    //   偏移映射据 C# 既有注释：+0x75=m_boStickMode(TBaseObject.NativeSkill265.cs)、
    //   +0x26C=m_wEffectResistance(TBaseObject.Attack.cs)、+0x154=m_btDirection、
    //   +0x78=m_nViewRange(TBaseObject.cs:112)。
    //
    // 语义：viewRange=0 → 永不索敌；m_boStickMode=true → 不可推动/位移；高抗性 250。
    // 即一扇静止不可动、看不见敌人的"冰门"障碍物；一切 AI 走 AnimalObject 基类。
    // 原先 race 144 落工厂 default(0x67AE5E) → 返回 nil，冰门根本不出现。
    // fail-closed：无覆写、无额外字段，不臆造任何主动行为。
    public class IceDoor : AnimalObject
    {
        public IceDoor() : base()
        {
            m_boStickMode = true;
            m_wEffectResistance = 250;
            m_btDirection = 0;
            m_nViewRange = 0;
        }
    }
}
