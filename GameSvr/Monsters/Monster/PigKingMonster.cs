namespace GameSvr
{
    // 战神字节证据 (Tier-1)：race 128 = TPigKingMonster，classref 0x663D08 -> VMT 0x663D54，
    //   size 0x4F4(1268)，parent = TMonster(VMT 0x65E030，= C# Monster)。
    //   工厂 sub_679F8C：race128-0xB=0x75 -> idx 0x32=50 -> jt[50]=0x67A81D，case body：
    //     67A81D  B2 01              mov  dl,1
    //     67A81F  A1 08 3D 66 00     mov  eax,[0x663D08]   ; classref -> TPigKingMonster
    //     67A824  E8 BF 07 00 00     call 0x66AFE8         ; TPigKingMonster.Create
    //     67A829  89 45 F8           mov  [ebp-8],eax
    //     67A82C  E9 0E 05 00 00     jmp  0x67AD3F         ; case 内无额外 RNG/字段写
    //
    // 构造器 sub_66AFE8 全文（父 Create 之后）：
    //   66B001  E8 06 B1 FF FF          call 0x66610C            ; TMonster.Create（= base()）
    //   66B006  C6 86 58 01 00 00 01    mov  byte [esi+0x158],1  ; ← 见 fail-closed
    //   66B00F  A1 8C 1E 42 00 / E8..   mov  eax,[0x421E8C]/call 0x404660 ; TList.Create
    //   66B019  89 86 F0 04 00 00       mov  [esi+0x4F0],eax     ; ← 新字段 = TList（爪牙列表）
    //
    // ⛔ fail-closed（原生证据齐全，C# 无落点，不臆造）：
    //  (1) [+0x158] 这个 bool（ctor 置 1）在 C# 侧无确认字段，仅记录。
    //  (2) 新字段 [+0x4E8]/[+0x4EC]/[+0x4F0]（父 size 0x4E8 之后）：+0x4F0 是 TList（爪牙实例表），
    //      +0x4E8=当前爪牙目标数、+0x4EC=上次补员 tick。C# Monster 无这 3 个字段。
    //  (3) VMT 槽 +0x088（slot34 = Run，父 TMonster sub_66622C）覆写 sub_66B070：猪王 AI——
    //      搜敌([+0x88]/[+0x35C] 计时 + SearchTarget 0x71DA70)、位移攻击(置 [+0x451]、目标坐标
    //      [+0x454]/[+0x458]、call 0x778BE8)，并按 [+0x2AC]/1000 维护爪牙列表 [+0x4F0]（每 15000ms
    //      重算，减少则 call 0x66AF34 补员，并清理列表内死亡项）。通篇依赖 (2) 的新字段与 0x66AF34、
    //      0x778BE8 等未定 helper，忠实移植会臆造，故 Run 不覆写（暂用 Monster.Run）。原生起点 0x66B070。
    //  (4) VMT 槽 +0x1B4（slot109，父 TCreature sub_76C35C）覆写 sub_66B210：某带类别参数的取值虚方法，
    //      cx==0xC8 时返回 arg*2，否则转父。身份未定，fail-closed。
    //  (5) VMT 槽 +0x208（slot130，父 TAnimal sub_71E208）覆写 sub_66B234：先转父 0x71E208，
    //      再 [+0x344]=target（= m_TargetCret）。身份未定，fail-closed。
    //
    // 结论：C# 侧行为 = Monster（父类 AI）。具名类型使 race 128 脱离 default sink，并为猪王爪牙机制
    //   留下确定挂载点（新字段 +0x4E8/+0x4EC/+0x4F0 + Run/slot109/slot130）。
    public class PigKingMonster : Monster
    {
        public PigKingMonster() : base()
        {
        }
    }
}
