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

                default:
                    return false;
            }
            return true;
        }
    }
}
