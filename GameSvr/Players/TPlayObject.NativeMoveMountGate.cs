namespace GameSvr
{
    // MOVE-10 —— 移动路径唯一的"静默丢包"闸。
    //
    // == 原生字节 ==
    // Ident 跳表 0x6D8592（基 ident 3010）：3010→0x6D9B65 / 3011→0x6D9BD0 /
    // 3012→0x6D9C7D / 3013→0x6D9CE4。四个移动 handler 里**只有 walk(3011) 与
    // run(3013)** 带这道闸，turn(3010)/pose(3012) 没有：
    //   walk 0x6D9BD0  B2 34              mov  dl,0x34
    //        0x6D9BD5  E8 86 8D 09 00     call 0x772960        ; InBodyState
    //        0x6D9BDC  0F 85 4A 20 00 00  jne  0x6DBC2C        ; 命中 -> 整个 case 落地
    //   run  0x6D9CEC  B2 34              mov  dl,0x34
    //        0x6D9CF1  E8 6A 8C 09 00     call 0x772960
    //        0x6D9CF8  0F 85 2E 1F 00 00  jne  0x6DBC2C
    // 0x6DBC2C 是 Operate 的公共出口：不发 0x275(SM_ACT_GOOD)、不发 0x276(SM_ACT_FAIL)、
    // 不广播、不记 tick —— 与其余所有拒绝分支（都要经 [vmt+0x250] 发 0x276）不同。
    //
    // == state 0x34 是什么（分片 20 未定性，此处补证）==
    // 置位唯一点 0x6EE8AF `mov dl,0x34` / 0x6EE8B3 `call 0x772974`(bts [+0x168])，
    // 就在 0x6EE8A0 `mov [esi+0x3C0],ebx`（写双人坐骑同伴指针）与 0x6EE8DC
    // `call sub_6BBEE4`（把自己搬到驾驶者格）之间；清位点 0x6EEBC2 `call 0x7729A8`(btr)。
    // 即 **0x34 = 双人坐骑的乘客态**（0x33 = 驾驶者/单人坐骑态）。
    // 交叉印证：sub_6BBE84（组队门）读 `(0x33 && [+0x3C0]) || 0x34`；
    // sub_6BBEB8（HIT 门）读 `0x33 || 0x34`；sub_6E9BAC 前置门 0x6DAE01 读 0x34。
    // 本端已把它落为 NativeHorseBlockedState(52)，常量在 TPlayObject.NativeRun3Horse.cs。
    //
    // == 可达性（本端）==
    // CM_INVITE_HORSE → ClientNativeHorseInviteResponse（TPlayObject.NativeHorsePair.cs:117）
    // `SetNativeActiveState(NativeHorseBlockedState)`，随后 MoveToNativeHorseDriver 把乘客
    // 钉在驾驶者格上。所以 state 52 是真实可达的玩家态，不是死码。
    // 分片 20 排除的两个替身也已复证不成立：POISON_STONE=5 → bodyState 0x1A(26)；
    // m_boCanWalk/m_boCanRun 是 boLockWalkAction/boLockRunAction 登录锁。
    //
    // == 现状偏差 ==
    // ClientWalkXY/ClientRunXY 均未测 52，乘客能自己走跑离开驾驶者的格子，并收到
    // 0x275 成功应答 —— 原生是原地不动且一个字节都不回。
    //
    // == 为何本轮不接线 ==
    // "静默"必须在派发层实现：ClientWalkXY 返回 false 会让 TPlayObject.Message.cs:1491
    // 的 else 臂发 SM_ACT_FAIL(0x276)，返回 true 则发 0x275，两条都不是静默。而
    // TPlayObject.Message.cs 属本轮禁改文件，故本文件只落谓词，接线点上报主代理。
    // 形状与既有先例完全一致：MINE-49 的 IsNativeHitBlockedByMountState
    // （TPlayObject.NativeHitMountGate.cs）就是"谓词在独立 partial，Message.cs 里
    // `if (谓词) break;`"。本闸只测 52（不含 51）——原生 walk/run 用的是裸
    // InBodyState(0x34)，不是 HIT 那个 51||52 的 sub_6BBEB8。
    public partial class TPlayObject
    {
        /// <summary>
        /// 0x6D9BD0(walk 3011) / 0x6D9CEC(run 3013) 的入口闸。命中即"此刻是双人坐骑乘客"，
        /// 调用方须在 ClientWalkXY / ClientRunXY 之前放弃整个 case（对齐 0x6DBC2C：
        /// 不发 0x275、不发 0x276、不记 m_dwActionTick）。
        /// </summary>
        internal bool IsNativeMoveBlockedByPassengerState()
        {
            return HasNativeActiveState(NativeHorseBlockedState);
        }
    }
}
