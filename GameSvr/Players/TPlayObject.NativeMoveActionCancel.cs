namespace GameSvr
{
    // MOVE-11 —— 原生 sub_6BCE2C(Self, wIdent)。它是"玩家发起动作即取消挂起通道"的公共钩子，
    // 由 8 个 handler 在各自可行性门之前无条件调用：
    //   0x6D98DF(CM_HERO_POWERUP 1108) / 0x6D9BEC(CM_WALK 3011) / 0x6D9D08(CM_RUN 3013) /
    //   0x6D9ED3(HIT 族 CASE1) / 0x6D9F7D(CM_3037 CASE2) / 0x6DA017(CM 4105) /
    //   0x6EC635(sub_6EC5D8，唯一入口 CM 3344) / 0x6EE201(sub_6EE174 召唤坐骑，由 CM 4105 进入)。
    // 注：0x6DA017 不是 CM_SPELL。跳表 0x6D8592(基 ident 3010) idx 7 = 3017 -> 0x6DA04A，
    // 该臂走的是 0x6DA054 call 0x6F2D48，通篇不调 0x6BCE2C。
    //
    // 函数体只有三条调用（0x6BCE2C..0x6BCE52 单 ret）：
    //   0x6BCE37  call 0x6EE128
    //       word[self+0xA24] != 0 → dword[+0xA20]=0 / word[+0xA24]=0 / word[+0xA26]=0，
    //       再经 vmt+0xE0 发 ident 0x4D0(1232)，nParam1 = 旧 word[+0xA24]。
    //   0x6BCE3E  call 0x6EF5D0
    //       byte[self+0x18E1] = 0（**无条件**，先于任何判断）；
    //       word[+0xA4C] != 0 → 清 [+0xA28..+0xA4C] 共 0x26 字节，
    //       再经 vmt+0xE0 发 ident 0x4D2(1234)，nParam1 = 旧 word[+0xA4C]。
    //   0x6BCE47  call [vmt+0x1D8]
    //       TPlayer VMT 0x6AC8C8 + 0x1D8 = 0x6EE2AC（THumanKind VMT 0x73BC34 同槽 = 0x772A98 `ret` 空实现）：
    //       byte[self+0x1914] != 0 → byte[+0x1914]=0 / dword[+0x1918]=0 / word[+0x191C]=0，
    //       发 ident 0xD57(3415)。该三元组即"召唤坐骑挂起"（读点 0x6EE321 判 [+0x1914]、
    //       0x6EE36F 判 GetTickCount-[+0x1918] >= word[+0x191C]）。
    //
    // 三个被调例程本端早已逐条落地在 TPlayObject.NativeTimedAbility.cs，并且已在
    // CM_HERO_POWERUP 臂按同一顺序连用（TPlayObject.Message.cs:3011-3013，对应 0x6EE201），
    // 所以本条不是"未建模子系统"，只是 walk/run 两臂漏接了这个前置钩子。
    public partial class TPlayObject
    {
        internal void CancelNativeActionChannels()
        {
            CancelNativeChannelMagic();
            CancelNativeLocationChannelMagic();
            CancelNativeType51PendingForTimedAbility();
        }
    }
}
