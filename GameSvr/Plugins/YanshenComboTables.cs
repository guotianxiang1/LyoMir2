namespace GameSvr.Plugins
{
    /// <summary>
    /// 盘古3 页的 <c>战士合击</c> / <c>法道合击</c>：合击伤害系数表的插件覆盖。
    ///
    /// 宿主有 <b>两张</b> 独立的 11 槽 f64 系数表，各由一个专属的合击结算例程读取：
    /// <code>
    ///   0068FF2C  sub_68FF2C(eax=hero, edx=participant, ecx=power, [esp+4]=spread)
    ///     0068FF4F  mov  eax,[esi+0x6D4]        ; hero 的合击 UserMagic
    ///     0068FF55  call 0x4C896C               ; 有效等级 -> ebx
    ///     0068FF5C  cmp  bl,0x0A / jbe / xor ebx,ebx
    ///     0068FF6D  fmul qword [eax*8 + 0x7D33FC]   ; <<== 战士合击 系数表
    ///     0068FF74  call 0x403574                   ; fistp = round-half-to-even
    ///     0068FF7F  add  esi,dword [eax*4 + 0x7D32D0] ; 共用的加法表
    ///     0068FF8D  call [participant.vmt+0x274]
    ///
    ///   0068EEDC  同形，唯一差别是
    ///     0068EF1D  fmul qword [eax*8 + 0x7D3278]   ; <<== 法道合击 系数表
    /// </code>
    /// 两张表的出厂值（<c>flat_image.bin</c> 直读，base 0x400000）：
    /// <code>
    ///   0x7D33FC 00 00 00 00 00 00 F8 3F = 1.5      0x7D3278 CD CC .. FC 3F = 1.8
    ///   0x7D3404 00 00 00 00 00 00 00 40 = 2.0      0x7D3280 00 00 .. 04 40 = 2.5
    ///   0x7D340C 33 33 33 33 33 33 03 40 = 2.4      0x7D3288 66 66 .. 0A 40 = 3.3
    ///   0x7D3414 CD CC CC CC CC CC 04 40 = 2.6      0x7D3290 CD CC .. 0C 40 = 3.6
    ///   0x7D341C 66 66 66 66 66 66 06 40 = 2.8      0x7D3298 33 33 .. 0F 40 = 3.9
    ///   0x7D3424..0x7D3453 恒 2.8                   0x7D32A0..0x7D32CF 恒 3.9
    /// </code>
    ///
    /// <b>哪一路走哪张表</b>——把 <c>sub_68FF2C</c> 的 5 个与 <c>sub_68EEDC</c> 的 10 个
    /// rel32 调用点全部反汇编，看调用前读的是哪一对能力字段
    /// （字段号见 <c>NativeTimedAbilityCombatConsumer</c>：DC +0x28C/+0x290、
    /// MC +0x294/+0x298、SC +0x29C/+0x2A0、CC +0x2A4/+0x2A8）：
    /// <code>
    ///   sub_68FF2C  0x68EA31 DC(+0x264+0x28/0x2C)  0x690D52 CC  0x690ECA DC
    ///               0x690F9C CC                    0x69181A CC
    ///   sub_68EEDC  0x690C4E SC  0x69116C SC  0x6914E0 MC  0x691716 MC
    ///               0x691993 MC  0x6919C2 MC/SC  0x691D29 SC  0x691D58 SC
    ///               0x69208B MC  0x6920B9 MC
    /// </code>
    /// 划分是干净的：<b>DC / CC → 战士合击表，MC / SC → 法道合击表</b>，没有任何一个
    /// 调用点跨界。因此 C# 侧 <c>HeroObject.GetNativeUnionMagicParticipantDamage</c>
    /// （读 MC/SC）走法道表、<c>CalculateNativeUnionPhysicalDamage</c>（读 DC）走战士表。
    ///
    /// <b>插件怎么改</b>——不是 detour，是把 5 个 f64 直写进宿主 .data
    /// （apply 函数 <c>sub_100B7F40</c>，插件转储 base 0x10000000）：
    /// <code>
    ///   战士合击  门 0x100B890A cmp [edi+0x308],0 / je；缓存 0x100B8917 cmp [eax+0x814],0
    ///     0x100B8924 push [edi+0x30C] / call 0x10234345(atof) -> 数值1 .. 数值5([edi+0x31C])
    ///     0x100B89D5 mov [0x7D33FC],eax / 0x100B89E1 mov [0x7D3400],ecx
    ///     0x100B8A4F [0x7D3404]  0x100B8AC9 [0x7D340C]
    ///     0x100B8B43 [0x7D3414]  0x100B8BBD [0x7D341C]
    ///   法道合击  门 0x100B8D2E cmp [edi+0x3A0],0 / je；缓存 0x100B8D3B cmp [eax+0x818],0
    ///     0x100B8D48 push [edi+0x3A4] .. [edi+0x3B4]  ->  0x100B8DF9 mov [0x7D3278],eax …
    /// </code>
    /// 只覆盖前 5 槽；6..11 槽保持出厂值。加法表 <c>0x7D32D0</c> 两路共用，插件不动它。
    ///
    /// <b>取值语义</b>——写进去的是 <c>atof(页面对象字符串)</c>，<b>没有</b> 盘古4 那种
    /// <c>test eax,eax / jle 跳过</c> 的非正数闸门。但页面对象构造函数已经把每一槽
    /// 初始化成出厂值，配置缺项/空串时不会被改写：
    /// <code>
    ///   0x100B6F53 lea esi,[edi+0x30C] / 0x100B6F85 push "1.5"   ; 战士合击_数值1
    ///   0x100B70E3 lea esi,[edi+0x3A4] / 0x100B7115 push "1.8"   ; 法道合击_数值1
    /// </code>
    /// 所以缺省值 = 出厂系数，这里的 <c>ParamF(key, stock)</c> 与之等价
    /// （<c>GetParam</c> 对空串 / 非数值一律回落 default）。
    /// 生产 config：<c>战士合击=1</c> 且五值 1.5/2/2.4/2.6/2.8 == 出厂；
    /// <c>法道合击=1</c> 但五值全空 → 全部回落出厂。两键在这台服务器上都是零差异。
    /// </summary>
    internal static class YanshenComboTables
    {
        /// <summary>宿主 0x7D33FC 起 11 个 f64，逐字节直读。</summary>
        internal static readonly double[] WarriorStock =
        {
            1.5, 2.0, 2.4, 2.6, 2.8, 2.8, 2.8, 2.8, 2.8, 2.8, 2.8
        };

        /// <summary>宿主 0x7D3278 起 11 个 f64，逐字节直读。</summary>
        internal static readonly double[] WizTaoStock =
        {
            1.8, 2.5, 3.3, 3.6, 3.9, 3.9, 3.9, 3.9, 3.9, 3.9, 3.9
        };

        /// <summary>插件只改写前 5 槽（0x100B89D5/0x100B8A4F/0x100B8AC9/0x100B8B43/0x100B8BBD）。</summary>
        private const int PatchedSlots = 5;

        /// <summary>DC / CC 路：战士合击表。</summary>
        internal static double Warrior(int level) => Slot(level, WarriorStock, warrior: true);

        /// <summary>MC / SC 路：法道合击表。</summary>
        internal static double WizTao(int level) => Slot(level, WizTaoStock, warrior: false);

        private static double Slot(int level, double[] stock, bool warrior)
        {
            if (level < 0) level = 0;
            if (level >= stock.Length) level = stock.Length - 1;
            var value = stock[level];
            if (level >= PatchedSlots || M2Share.PluginManager == null)
                return value;
            var api = new YanshenApi(null, null, M2Share.PluginManager);
            if (warrior)
                return api.IsWarriorCombo() ? api.WarriorComboMultiplier(level, value) : value;
            return api.IsWizTaoCombo() ? api.WizTaoComboMultiplier(level, value) : value;
        }
    }
}
