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
    }
}
