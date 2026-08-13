namespace GameSvr
{
    // MINE-49 — 战斗：HIT 派发器缺失的骑乘态(51/52)攻击门。
    //
    // 原生 HIT 族派发器 sub_6D9EAF（= C# TPlayObject.Operate 的出队处理，见
    // TPlayObject.Message.cs 的 switch(ProcessMsg.wIdent)）在把消息交给
    // ClientHitXY(sub_6EC078) 之前，先调 sub_6BBEB8 测「骑乘态 / 坐骑受阻」，
    // 命中则放弃整个 HIT case（禁止上马 / 坐骑受阻时攻击）。C# 侧无此门，且
    // 上马 SetNativeActiveState(51) 也不置 m_boCanHit=false，故 MINE-49 缺陷成立。
    //
    // == 谓词本体 sub_6BBEB8（ImageBase=0x400000，flat_image.bin 逐字节）==
    //   0x6BBEB8  55                 push ebp
    //   0x6BBEB9  8B EC              mov  ebp,esp
    //   0x6BBEBB  53                 push ebx
    //   0x6BBEBC  8B D8              mov  ebx,eax           ; ebx = Self
    //   0x6BBEBE  B2 33              mov  dl,0x33           ; state 0x33 = 51
    //   0x6BBEC0  8B C3              mov  eax,ebx
    //   0x6BBEC2  E8 99 6A 0B 00     call 0x772960          ; HasState(Self,51) -> al
    //   0x6BBEC7  84 C0              test al,al
    //   0x6BBEC9  75 12             jne  0x6BBEDD          ; 命中 → 返回 true
    //   0x6BBECB  B2 34              mov  dl,0x34           ; state 0x34 = 52
    //   0x6BBECD  8B C3              mov  eax,ebx
    //   0x6BBECF  E8 8C 6A 0B 00     call 0x772960          ; HasState(Self,52) -> al
    //   0x6BBED4  84 C0              test al,al
    //   0x6BBED6  75 05             jne  0x6BBEDD          ; 命中 → 返回 true
    //   0x6BBED8  33 C0              xor  eax,eax           ; 皆未置位 → 返回 false
    //   0x6BBEDA  5B 5D C3           pop ebx; pop ebp; ret
    //   0x6BBEDD  B0 01              mov  al,1              ; 返回 true
    //   0x6BBEDF  5B 5D C3           pop ebx; pop ebp; ret
    // 即 sub_6BBEB8 == (HasState(51) || HasState(52))，测序：先 51 后 52。
    //
    // 被调的状态访问器 sub_772960(Self, dl=index) -> Boolean：
    //   0x772960  80 FA 6F           cmp dl,0x6F            ; index > 111 → 越界
    //   0x772963  77 0A             ja  0x77296F
    //   0x772965  83 E2 7F           and edx,0x7F
    //   0x772968  0F A3 90 68 01 00 00  bt [eax+0x168],edx  ; 状态位图（Obj+0x168）
    //   0x77296F  0F 92 C0           setb al
    //   0x772972  C3                 ret
    // 与 C# TBaseObject.HasNativeActiveState(int) 语义逐位一致（越界返回 false +
    // 位图 bit test），本谓词直接复用它。
    //
    // == 调用点 & 放弃跳转（sub_6D9EAF，CASE1 @0x6D9EAF）==
    //   0x6D9EB4  E8 8F 8E 01 00     call 0x6F2D48          ; 先于门（无条件）
    //   0x6D9EBC  E8 F7 1F FE FF     call 0x6BBEB8          ; ★ 骑乘态门
    //   0x6D9EC1  84 C0              test al,al
    //   0x6D9EC3  0F 85 63 1D 00 00  jne  0x6DBC2C          ; ★ 命中 → 跳公共出口，
    //                                                       ;   不调 ClientHitXY、不发任何包
    //   0x6D9F06  E8 6D 21 01 00     call 0x6EC078          ; ClientHitXY（门通过后才到）
    // CASE2 @0x6D9F4B（Ident 0xBD3=3027=CM_3037）门在 0x6D9F58，命中改跳 0x6D9FE7，
    // 发一次 0x276 更正包后再汇聚 0x6DBC2C（与 CASE1 的“静默”略有差异，见报告）。
    //
    // == 放弃范围（全镜像 xref：sub_6BBEB8 调用者=0x6D9EBC/0x6D9F58/0x6EE180；
    //    ClientHitXY(0x6EC078) 调用者仅 0x6D9F06/0x6D9FA2，两处均被前置本门）==
    // 经 0x6D8501 起的 Ident 分发（jmp [eax*4+0x6D8592] + 直接 cmp）落到
    // CASE1(0x6D9EAF)/CASE2(0x6D9F4B) 的 HIT 族 Ident，全部经本门：
    //   CM_SWORD_HIT 0xBBA(3002)  CM_HIT     0xBC6(3014)  CM_HEAVYHIT 0xBC7(3015)
    //   CM_BIGHIT    0xBC8(3016)  CM_POWERHIT0xBCA(3018)  CM_LONGHIT  0xBCB(3019)
    //   CM_WIDEHIT   0xBD0(3024)  CM_FIREHIT 0xBD1(3025)  CM_CRSHIT   0xBD2(3026)
    //   CM_TWINHIT   0xBD4(3028, 0x6D85F5 je 0x6D9EAF)    CM_3037     0xBD3(3027, CASE2)
    // 即 C# TPlayObject.Message.cs 的整个 HIT arm（CM_HIT..CM_3037）逐一对应被门 case，
    // 门整段 arm 属精确 1:1（无过度 / 无遗漏）。CM_SPELL(3017=0xBC9) 走独立 handler
    // 0x6DA04A，**不**经此 ClientHitXY 门（第三处门 0x6EE180 属坐骑召唤 sub_6EE174，
    // 另一契约），故本门不含 spell。
    public partial class TPlayObject
    {
        // 骑乘态攻击门谓词。命中即“此刻处于坐骑态(51)或坐骑受阻(52)”，调用方须
        // 在 ClientHitXY 之前放弃整个 HIT case（对齐 0x6DBC2C：静默、不发包）。
        // NativeHorseMountedState(51)/NativeHorseBlockedState(52) 常量定义在同一
        // partial class 的 TPlayObject.NativeRun3Horse.cs，此处复用不重复声明。
        internal bool IsNativeHitBlockedByMountState()
        {
            // 0x6BBEC2 HasState(51) || 0x6BBECF HasState(52)（测序：先 51 后 52）。
            return HasNativeActiveState(NativeHorseMountedState) ||
                   HasNativeActiveState(NativeHorseBlockedState);
        }
    }
}
