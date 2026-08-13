namespace GameSvr
{
    // ============================================================================
    // race-a 批：怪物 race 子类工厂映射（race 值升序低段）。
    //
    // 依据：AddCreature 工厂 sub_679F8C（VA 0x679F8C）——
    //   movzx eax,byte[monRec+0x14]      ; race
    //   add   eax,-0x0B                   ; race - 11
    //   cmp   eax,0xEE / ja 0x67AE5E      ; 越界 -> default sink
    //   mov   al,byte[eax+0x67A026]       ; 索引表 -> group
    //   jmp   dword[eax*4+0x67A115]       ; 跳表 -> handler
    // 每个 handler 形如 `mov dl,1 / mov eax,[classref] / call ctor / mov [ebp-8],eax
    //   / jmp 0x67AD3F`。race->classref->ctor 见 staging/_gp3/wd1_races.txt、q6_factory.txt，
    //   本仓 _tools/race_map.py 复算一致（每条 case 的 handler/classref/ctor 注明在下方）。
    //
    // 防冲突：本文件是 race-a 独占的新文件；不改动 UsrEngn.cs 的热点 switch。
    //   合并者在 UserEngine.AddBaseObject 的 `switch (nMonRace)` 之前（或 default 之前）插入：
    //       if (TryCreateRaceA(nMonRace, out var certA)) { Cert = certA; break; }
    //   即可把本批 race 接入工厂。
    //
    // 说明：刻意写成普通方法（不加 partial 关键字）。UserEngine 本就是 partial class，
    //   普通方法放入分部文件可独立编译，避免 C# 9 “扩展 partial 方法需 defining+implementing
    //   成对声明” 的要求（否则缺少配套声明会编译失败）。行为与工厂内联等价。
    // ============================================================================
    public partial class UserEngine
    {
        // 返回 true 表示本批认领并已构造该 race 的实例（含原生 case body 的 ctor 后逻辑）。
        private bool TryCreateRaceA(int nMonRace, out TBaseObject cert)
        {
            cert = null;
            switch (nMonRace)
            {
                // race 70  group 8  handler 0x67A45B  classref 0x66593C  ctor 0x66D12C  TFriendAnimal
                //   case body 无 ctor 后逻辑（无 RNG、无字段写）。构造器 sub_66D12C 已核验（见 FriendAnimal.cs）。
                case 70:
                    cert = new FriendAnimal();
                    break;

                // race 98  group 27 handler 0x67A62B  classref 0x67EF94  ctor 0x68130C  TWalkMon
                //   case body 无 ctor 后逻辑。ctor 已核验（见 WalkMon.cs）。
                case 98:
                    cert = new WalkMon();
                    break;

                // race 99  group 28 handler 0x67A63F  classref 0x67F21C  ctor 0x681958  TSkyArcher
                //   case body 无 ctor 后逻辑。ctor + IsAttackTarget 已核验（见 SkyArcher.cs）。
                case 99:
                    cert = new SkyArcher();
                    break;

                // race 121 group 49 handler 0x67A809  classref 0x662858  ctor 0x66ADC4  TArmLightGuard
                //   case body 无 ctor 后逻辑。ctor 已核验（见 ArmLightGuard.cs）。
                case 121:
                    cert = new ArmLightGuard();
                    break;

                // race 128 group 50 handler 0x67A81D  classref 0x663D08  ctor 0x66AFE8  TPigKingMonster
                //   case body 无 ctor 后逻辑（爪牙 AI 见 PigKingMonster.cs，fail-closed）。
                case 128:
                    cert = new PigKingMonster();
                    break;

                // race 135 group 53 handler 0x67A859  classref 0x663F9C  ctor 0x66B288  TFireDragon
                //   case body 无 ctor 后逻辑。构造器 sub_66B288 已核验（见 FireDragon.cs）。
                case 135:
                    cert = new FireDragon();
                    break;

                // race 136 group 54 handler 0x67A86D  classref 0x664224  ctor 0x66B658  TKingFireDragon
                //   case body 无 ctor 后逻辑。ctor 已核验（见 KingFireDragon.cs）。
                case 136:
                    cert = new KingFireDragon();
                    break;

                // race 137 group 55 handler 0x67A881  classref 0x66475C  ctor 0x66BA24  TSuicideBat
                //   case body 无 ctor 后逻辑。ctor 已核验（见 SuicideBat.cs）。
                case 137:
                    cert = new SuicideBat();
                    break;

                default:
                    return false;
            }
            return true;
        }

        // ========================================================================
        // ⛔ race-a 段内 fail-closed（父类未在 C# 移植；建错父类或改共享基类均会臆造/引冲突，
        //    故不建 .cs、不接线，只留证据供后续专项移植）：
        //
        //  race 129 TShadowHero  handler 0x67A831 classref 0x719F2C ctor 0x71B4D0
        //  race 130 TTaoistEngine handler 0x67A845 classref 0x71A1B8 ctor 0x71C558
        //    —— 二者 parent = TAIMon(VMT 0x719CF4, size 0x5F0, parent TAnimal)。C# 无 TAIMon
        //       （grep 无 class AIMon/TAIMon）。TAIMon 覆写 slot102-105 属性虚槽，是 AI/英雄引擎基类。
        //
        //  race 138 TWolfStickIceMon   handler 0x67A895 classref 0x66D594 ctor 0x6735D4
        //  race 139 TLongKnifeIceMon   handler 0x67A8A9 classref 0x66D860 ctor 0x6735D4
        //  race 140 TGreenPoisonIceMon handler 0x67A8BD classref 0x66DB2C ctor 0x6735D4
        //  race 141 TRedPoisonIceMon   handler 0x67A8D1 classref 0x66DDFC ctor 0x6735D4
        //  race 142 TBluePoisonIceMon  handler 0x67A8E5 classref 0x66E0C8 ctor 0x6735D4
        //  race 143 TQuickKnifeIceMon  handler 0x67A8F9 classref 0x66E394 ctor 0x6748CC
        //    —— parent = TSearchMon(VMT 0x66D320, size 0x55C, parent TAnimal, 143 slots：
        //       比 TAnimal 多出 slot131-142 共 12 个新虚槽)。C# 无 TSearchMon。
        //       共享 ctor sub_6735D4 全文：TAnimal.Create 后写 [+0x2EE]=0 / [+0x78]=4(viewrange) /
        //       [+0x540]=0x320 / [+0x544]=0x3E8 / [+0x548]=GetTickCount / [+0x54C]=0 / [+0x550]=0 /
        //       [+0x4D8]=1 / [+0x558]=0（+0x540..+0x558 皆 TSearchMon 新字段，C# 无落点）。
        //       138-142 共用 sub_6735D4，仅 VMT(身份)与 slot136-140 覆写不同；143 另有 ctor 0x6748CC。
        //
        //  处置：需先把 TAIMon / TSearchMon（含各自新字段与新虚槽）移植为 C# 基类后，再补这 8 个子类。
        // ========================================================================
    }
}
