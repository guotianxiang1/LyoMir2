namespace DBSvr
{
    /// <summary>
    /// 原生 <c>gamedata.YBConsume</c> 元宝消费门槛查询（NATIVE-ONLY 缺口，本次补齐）。
    ///
    /// 原版是排队系统的 <b>VIP 免排队闸</b>，函数 0x5C9A5C（eax=Self, edx=PTID）：
    ///   0x5C9A81  8b 40 4c        mov eax,[Self+0x4C]      ; 记忆化 hash 表
    ///   0x5C9A84  e8 …            call 0x49BAA8            ; TStringHash.ValueOf
    ///   0x5C9A89  83 e8 01        sub eax,1
    ///   0x5C9A8C  72 13           jb  0x5C9AA1             ; ★==0 未命中 -> 查库
    ///   0x5C9A8E  74 05           je  0x5C9A95             ; ==1 -> 返 true
    ///   0x5C9A90  48 / 74 08      dec eax / je 0x5C9A9B    ; ==2 -> 返 false
    ///   0x5C9AA1… 未命中分支，组格式参数：
    ///   0x5C9AA5  mov eax,[ebp-8] / 0x5C9AA8 mov [ebp-0x20],eax
    ///   0x5C9AAB  c6 45 e4 0b     mov byte [ebp-0x1C],0x0B ; 槽0 类型 = vtAnsiString
    ///                                                       ; -> %s = PTID
    ///   0x5C9AB2  8b 80 84 00 00 00  mov eax,[Self+0x84]   ; 槽1 -> %d = 门槛
    ///   0x5C9AC2  b9 01 00 00 00  mov ecx,1                ; 高位下标 1 = 2 个 TVarRec
    ///   0x5C9AC7  b8 3c 9b 5c 00  mov eax,0x5C9B3C         ; ★模板
    ///   0x5C9ACC  e8 …            call 0x40CF30            ; Format
    ///   0x5C9AD7  e8 04 83 ff ff  call 0x5C1DE0            ; 执行查询，返回行数
    ///   0x5C9ADC  85 c0 / 7e 19   test eax,eax / jle       ; ★>0 才算过闸
    ///   0x5C9AE0  mov ecx,1  … call 0x49B410               ; 记 1（true）
    ///   0x5C9AF9  mov ecx,2  … call 0x49B410               ; 记 2（false）
    ///
    /// 门槛 <c>%d</c> = <c>Self+0x84</c>，判据是配置回写点：
    ///   0x5C15E3  8b 80 84 00 00 00  mov eax,[Self+0x84]
    ///   0x5C15E9  50                 push eax               ; 值
    ///   0x5C15EA  b9 54 16 5c 00     mov ecx,0x5C1654       ; Key  = "VipYBConsume"
    ///   0x5C15EF  ba 6c 16 5c 00     mov edx,0x5C166C       ; Sect = "Setup"
    ///   0x5C15F9  ff 53 0c           call [ini_vmt+0x0C]    ; WriteInteger
    /// ⇒ 门槛就是 <c>DBShare.VipYBConsume</c>（DBService.ini [Setup] VipYBConsume）。
    /// 此前把该配置项判为"与该表无关的配置整数"是错的 —— 它正是这条 SQL 的 %d。
    ///
    /// 两个调用点（e8 rel32 普查，区间 0x401000..0x5D5000，共 2 处）：
    ///   0x5A157A  排队时给会话打 VIP 标记：
    ///             0x5A152E cmp byte [[0x5D9D48]],0 / je    ; ★排队系统总开关
    ///             0x5A1567 cmp byte [sess+9],0 / jne       ; 已有豁免则跳过查库
    ///             0x5A1577 mov edx,[sess+0x24]             ; PTID
    ///             0x5A1586 mov byte [sess+0x94],1          ; 过闸 -> 免排队
    ///   0x5CD5C6  登录准入：过闸失败时
    ///             0x5CD5CF mov word [ebp-0x10],7           ; 返回码 7 = 进排队
    /// 总开关 [0x5D9D48] 由 0x599C1D `setne byte [eax]` 写，回显串
    /// 0x599D6C「排队系统开启」/ 0x599D84「排队系统关闭」—— 与 C# 侧
    /// <see cref="DBSvr.Core.NativeUserAdmissionControl.SetQueueEnabled"/> 同一开关。
    ///
    /// ⚠️ 真库已核 <c>gamedata.ybconsume</c> 存在且有真数据，但**列结构未取到**
    /// （无查询凭据）。本侧只按 SQL 逐字用到 <c>PTID</c> 与 <c>YBConsume</c> 两列，
    /// 不假设其它列。缺表/列不符时按原版异常路径返回 null，见实现注释。
    /// </summary>
    public interface INativeYbConsumeService
    {
        /// <summary>
        /// 执行 0x5C9B3C 的查询：该 PTID 是否有一行 <c>YBConsume &gt;= threshold</c>。
        /// </summary>
        /// <returns>
        /// <c>true</c> = 行数 &gt; 0（过闸）；<c>false</c> = 行数 &lt;= 0；
        /// <c>null</c> = 查询失败（含缺表），对应原版 0x5C1DE0 的 -1。
        /// ⚠️ 原版把 -1 交给 <c>test eax,eax / jle</c> 处理 ⇒ 失败**等价于不过闸**，
        /// 且会被记忆化成 2（false）。极性由 <see cref="Core.NativeYbConsumeGate"/> 落实，
        /// 本接口保留三态以便调用方区分"查了但不够"与"查不了"。
        /// </returns>
        bool? IsOverThreshold(string ptid, int threshold);
    }
}
