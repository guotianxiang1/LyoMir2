namespace GameSvr
{
    // 眼神 S(1,1..150) 一次性登录灌种 —— 战神 dll 0x100CE4EA 起的复刻。
    //
    // 这段原生代码在玩家登录时跑一次，把 S 银行铺成一张「已初始化」的连续表，
    // 并在 S(1,49) 写下哨兵值 1314。所有下游眼神特性（B3 切割 / 永久属性 / 施毒术
    // 范围等）都直读 [player+0x804] 裸银行，并且在动手之前先核对 bank+0x184==1314
    // （即 S(1,49)==1314）。没有这次灌种，哨兵为空（GetS miss = -1），所有裸银行
    // 消费者的第二道守卫全部失配、静默什么都不做。
    //
    // 逐条反汇编（转储 yanshen2_0_8_dll.memory.bin，PE 首选基址 0x10000000；
    // 文件偏移=RVA，与运行时重定位基址 0x57C40000 无关）：
    //
    //   0x100CE4EA  6A 31                 push 0x31            ; index 49
    //   0x100CE4EC  BA 01 00 00 00        mov  edx, 1          ; bank/group 1
    //   0x100CE4F1  8B CF                 mov  ecx, edi        ; this = player
    //   0x100CE4F3  E8 ..                 call GetS 桩(0x10056040 -> M2Server 0x6DF1B4)
    //   0x100CE4FB  3D 22 05 00 00        cmp  eax, 0x522      ; 0x522 = 1314
    //   0x100CE500  74 60                 je   0x100CE562      ; 已播种 -> 整段跳过（幂等守卫）
    //   0x100CE502  BE 01 00 00 00        mov  esi, 1          ; i = 1
    //   0x100CE50D  81 FE 96 00 00 00     cmp  esi, 0x96       ; 0x96 = 150
    //   0x100CE513  7F 4D                 jg   0x100CE562      ; i > 150 -> 收尾
    //   0x100CE517  83 FE 31              cmp  esi, 0x31       ; i == 49 ?
    //   0x100CE51A  75 11                 jne  0x100CE52D
    //   0x100CE51C  68 22 05 00 00        push 0x522           ;   i==49 -> 无条件 SetS(1,49,1314)
    //   0x100CE521  56                    push esi
    //   0x100CE522  E8 ..                 call SetS 桩(0x100CE200 -> M2Server 0x6DF240)
    //   0x100CE52D  56                    push esi             ;   否则先读 S(1,i)
    //   0x100CE52E  BA 01 00 00 00        mov  edx, 1
    //   0x100CE533  E8 ..                 call GetS 桩
    //   0x100CE53B  85 C0                 test eax, eax
    //   0x100CE53D  79 20                 jns  0x100CE55F      ;   有符号：值 >= 0 -> 保留不动
    //   0x100CE541  83 FE 09              cmp  esi, 9          ;   值 < 0 时：i >= 9 ?
    //   0x100CE544  7D 0E                 jge  0x100CE554
    //   0x100CE546  6A FF                 push -1              ;     i in 1..8 -> 写 -1
    //   0x100CE548  56                    push esi
    //   0x100CE549  E8 ..                 call SetS 桩
    //   0x100CE554  6A 00                 push 0               ;     i in 9..150(除 49) -> 写 0
    //   0x100CE556  56                    push esi
    //   0x100CE557  E8 ..                 call SetS 桩
    //
    // GetS 桩 0x10056040 以 edx=group / ecx=this / stack=index 调 M2Server 0x6DF1B4，
    // 未命中返回 -1（0x6DF1BB or esi,-1）。SetS 桩 0x100CE200 把 group 硬编码为 1
    // （0x100CE20C mov [ebp-4],1 -> edx），以 stack=index / stack=value 调 0x6DF240，
    // 对 value 无任何检查（0x6DF251/0x6DF255 只在 group<=0 或 index<=0 时拒绝），
    // 所以「写 -1 / 写 0」照样建键 -> 键 1001..1150 连续占满 150 个槽。
    //
    // C# 侧存储原语是 TPlayObject.TryGetScriptVar / SetScriptVar（键=group*1000+index，
    // 见 TPlayObject.Base.cs）。miss 在 C# 是 (false, 0)，原生是 -1；两处比较都按原生的
    // 「miss 视作 -1」语义处理（见下）。AuditTools/NativeSBankLayoutCheck 已把本规则钉死。
    public partial class TPlayObject
    {
        // 0x100CE50D cmp esi,0x96
        private const int YanshenSeedCount = 150;
        // 0x100CE517 cmp esi,0x31
        private const int YanshenSeedMarkerIndex = 49;
        // 0x100CE4FB / 0x100CE51C  0x522
        private const int YanshenSeedMarkerValue = 1314;
        // 0x100CE541 cmp esi,9 / jge
        private const int YanshenSeedNegativeBelow = 9;

        /// <summary>
        /// 复刻 0x100CE4EA 的一次性登录灌种。幂等：S(1,49) 已经是 1314 就整段跳过
        /// （原生 0x100CE4FB cmp eax,0x522 / 0x100CE500 je）。
        ///
        /// 登录时机的接线交给插桩点（主代理）：原生在玩家对象登录初始化路径上调用
        /// 本函数一次即可；这里只负责忠实还原「灌种」本身，不触碰 UsrEngn / Grobal2 /
        /// TPlayObject.Message 的登录序列。
        /// </summary>
        public void YanshenSeedLoginSVars()
        {
            // 幂等守卫：GetS(1,49)==1314 -> 已播种，跳过。
            // 原生 miss 返回 -1（!=1314）会继续播种；C# miss 是 (false,_)，同样继续。
            if (TryGetScriptVar('S', 1, YanshenSeedMarkerIndex, out var marker)
                && marker == YanshenSeedMarkerValue)
            {
                return;
            }

            for (var i = 1; i <= YanshenSeedCount; i++)
            {
                if (i == YanshenSeedMarkerIndex)
                {
                    // 0x100CE51C：i==49 无条件写哨兵 1314（不读、不看正负）。
                    SetScriptVar('S', 1, i, YanshenSeedMarkerValue);
                    continue;
                }

                // 0x100CE533 读 S(1,i)；原生 miss = -1，故 C# miss 也当作 -1（负）。
                var current = TryGetScriptVar('S', 1, i, out var v) ? v : -1;
                // 0x100CE53B test / 0x100CE53D jns：值 >= 0 保留不动，仅负值重置。
                if (current >= 0)
                {
                    continue;
                }

                // 0x100CE541 cmp esi,9 / jge：i in 1..8 -> -1；i in 9..150(除49) -> 0。
                SetScriptVar('S', 1, i, i < YanshenSeedNegativeBelow ? -1 : 0);
            }
        }
    }
}
