using GameSvr.PasEngine;

namespace GameSvr.Plugins
{
    /// <summary>
    /// 眼神「触发」族的派发层。
    ///
    /// <para><b>原生形状</b>（脱壳转储 <c>yanshen2_0_8_dll.memory.bin</c>，基址 0x10000000）。
    /// 这一族开关一个补丁站点都没有，它们全部走 REPLICATION_RULES §5.1.0.6 的第三种手段
    /// —— trampoline。安装器 <c>0x10032CC0</c>(71 站点) / <c>0x10032FD0</c>(30 站点)，桩体从
    /// <c>.rdata</c> 的 dword 模板拼装（模板是纯数据，没被 Themida 虚拟化）。每个桩体的尾巴
    /// 都是同一段：</para>
    /// <code>
    ///   A1 20 5D 7D 00     mov eax,[0x7D5D20]     ; -&gt; 0x7DC4A4 -&gt; TTaskAdmin 实例
    ///   8B 00              mov eax,[eax]
    ///   8B F0              mov esi,eax
    ///   8B 7E 08           mov edi,[esi+8]        ; TTaskAdmin+8 = 全局标准脚本 TSTDScript
    ///   68 &lt;imm32&gt;          push '@Label'          ; 装载时被 0x10033450 回填成真串指针
    ///   6A 00              push 0
    ///   33 C9              xor ecx,ecx            ; This_Item = nil
    ///   8B C7              mov eax,edi
    ///   8B 18              mov ebx,[eax]
    ///   FF 53 44           call [ebx+0x44]        ; 或 FF 53 48
    /// </code>
    /// <para>这与宿主自己发 <c>@PlayerActiveValidate</c> 的 <c>sub_69B22C</c>
    /// （<c>0x69B231 mov esi,[eax+8]</c> / <c>0x69B238 push 0x69B254</c> /
    /// <c>0x69B245 call [ebx+0x44]</c>）**逐字节同形**，只是标签换成了插件自己的名字。
    /// <c>[[0x7D5D20]]</c> 的类由 <c>0x792A08 mov eax,[0x69861C]</c>(classref) +
    /// <c>0x792A0D call 0x69870C</c>(ctor) + <c>0x792A12 mov edx,[0x7D5D20] / 0x792A18 mov [edx],eax</c>
    /// 定案为 <b>TTaskAdmin</b>；<c>+8</c> 由 <c>0x69A7EF mov eax,[0x728640]</c> +
    /// <c>0x69A7F4 call 0x7295B0</c> + <c>0x69A7FE mov [eax+8],ebx</c> 定案为 <b>TSTDScript</b>
    /// （VMT 0x72868C，classref [0x728640]）。</para>
    ///
    /// <para><b>两个派发槽</b>（TSTDScript VMT 0x72868C）：</para>
    /// <list type="bullet">
    /// <item><c>+0x44 = sub_733D84</c> —— 绑定 <c>This_DB</c>(0x733FCC) / <c>This_Item</c>(0x733FDC, ecx) /
    /// <c>This_Player</c>(0x733FF0, edx)，然后 <c>0x733DED cmp 标签,'@Main'</c> 与
    /// <c>0x733E01 cmp 标签,'@_Main'</c> 两道自递归门，<c>0x733E1F cmp byte [edi],0x40</c>
    /// 强制首字符为 <c>'@'</c>，并按 <c>'~'</c>(0x734024) 切参数。无参。</item>
    /// <item><c>+0x48 = sub_733B98</c> —— 同样三绑定（0x733D30/0x733D40/0x733D54），
    /// 外加一个 <b>open array of Variant</b>：<c>[ebp+0x14]</c>=标签、<c>[ebp+0x10]</c>=数组指针、
    /// <c>[ebp+0x0C]</c>=High、<c>[ebp+8]</c>=@Result；<c>0x733BB2 shl esi,2 / add esi,3</c>
    /// 证明元素宽度是 <b>16 字节</b>（TVarData），<c>0x733BE1 call 0x4062C4</c> 是开放数组
    /// 转动态数组的 RTL 助手。</item>
    /// </list>
    ///
    /// <para><b>C# 对应物</b>：<c>M2Share.g_FunctionNPC</c> —— 仓库里
    /// <c>@PlayerActiveValidate</c> / <c>@GroupCreate</c> 等宿主自身标签走的就是它，
    /// 所以插件标签必须走同一条路，否则就是两套权威。</para>
    ///
    /// <para><b>惰性</b>：<see cref="Armed"/> 在插件缺席时只读两个字段就返回 false，
    /// 不分配、不查配置、不碰脚本引擎。所有 Fire* 入口的第一条语句都是这道门。</para>
    /// </summary>
    public static class YanshenTriggerDispatch
    {
        /// <summary>桩体末尾的虚方法槽。数值就是原生 <c>call [ebx+0xNN]</c> 里的 NN。</summary>
        public enum Slot
        {
            /// <summary>TSTDScript VMT+0x44 = sub_733D84，无参标签调用。</summary>
            Plain = 0x44,

            /// <summary>TSTDScript VMT+0x48 = sub_733B98，带 <c>array of Variant</c>。</summary>
            WithParams = 0x48,
        }

        /// <summary>桩体是否重放了被覆盖的宿主指令。</summary>
        public enum HostAction
        {
            /// <summary>桩体重放被覆盖的字节后 <c>jmp resume</c>，宿主行为不变，纯通知。</summary>
            Notify,

            /// <summary>桩体**不**重放被覆盖的调用，直接 <c>jmp resume</c>：原生动作被顶掉。</summary>
            Replace,
        }

        /// <summary>
        /// 一个触发点的全部原生事实。这张表是纯数据，插件不在时永远不会被读到，
        /// 存在的意义是让审计工具能把「挂在哪、叫什么、几个参数、能不能取消」钉死。
        /// </summary>
        public sealed class Descriptor
        {
            public string ConfigKey { get; init; }
            public string ScriptLabel { get; init; }

            /// <summary>安装器 VA：0x10032CC0 或 0x10032FD0。</summary>
            public uint Builder { get; init; }

            /// <summary>插件里发起安装的调用点 VA。</summary>
            public uint[] BuilderSites { get; init; }

            /// <summary>被改写的宿主 VA（jmp 桩体）。</summary>
            public uint[] HostTargets { get; init; }

            /// <summary>桩体末尾 <c>jmp</c> 回宿主的续跑点 VA。</summary>
            public uint[] HostResumes { get; init; }

            public Slot DispatchSlot { get; init; }

            /// <summary>Variant 参数个数（原生 High+1）。Plain 槽恒为 0。</summary>
            public int ParamCount { get; init; }

            public HostAction Action { get; init; }

            /// <summary>C# 侧是否已经接通。未接通的只作为证据留档，运行期不做任何事。</summary>
            public bool Wired { get; init; }

            public string Note { get; init; }
        }

        // ── 全族清单 ────────────────────────────────────────────────────────────
        // 22 个 builder 站点、25 个 0x10033450 标签注册点，逐个解出来的。标签串不是
        // .rdata 明文：插件用 `mov [ebp-X],imm32` 把 Delphi 长串记录（refcnt=-1 / len /
        // 字符）现搭在栈上，再由 0x10033450 拷进 VirtualAlloc 池并把字符指针回填到桩体
        // `push imm32` 的立即数里（回填地址 = 代码游标 - backoff - 4，实测正好落在 push 的
        // imm32 上）。所以 .rdata 模板里看到的 push 值是占位符，不是事件号。
        //
        // 配置键全部在生产 D:/光头卧龙/mud2.0/Mir200/Gs1/config.json（380 键）里实测存在。
        private static readonly Descriptor[] _registry =
        {
            new()
            {
                ConfigKey = "召唤神兽触发", ScriptLabel = "@SummonShinsu",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AE51F },
                HostTargets = new uint[] { 0x006EDC5E }, HostResumes = new uint[] { 0x006EDC63 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Replace, Wired = true,
                Note = "被覆盖的 5 字节是 0x6EDC5E E8 19 12 08 00 call 0x76EE7C（神兽造宠本体）。"
                     + "桩体 35 字节全部是 pushal/取 TSTDScript/派发/popal/jmp 0x6EDC63，"
                     + "**没有重放那条 call** —— 开关打开时原生神兽不再产生，改由脚本接管。",
            },
            new()
            {
                ConfigKey = "召唤骷髅触发", ScriptLabel = "@SummonSkele",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AE2F6 },
                HostTargets = new uint[] { 0x006EDB44 }, HostResumes = new uint[] { 0x006EDB49 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Replace, Wired = true,
                Note = "被覆盖的 5 字节是 0x6EDB44 E8 B3 12 08 00 call 0x76EDFC（骷髅造宠本体），"
                     + "同样不重放。标签串由 0x100AE325..0x100AE353 现搭："
                     + "len=0xC / '@Sum'(0x6D755340) 'monS'(0x536E6F6D) 'kele'(0x656C656B)。",
            },
            new()
            {
                ConfigKey = "BB杀怪触发", ScriptLabel = "@BBupr",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D4814 },
                HostTargets = new uint[] { 0x0071F467 }, HostResumes = new uint[] { 0x0071F46C },
                DispatchSlot = Slot.WithParams, ParamCount = 2, Action = HostAction.Notify, Wired = true,
                Note = "挂 sub_71F3D0(GainSlaveExp) 的收尾 0x71F467 `5E 5B 59 5D C3`，桩体开头重放 "
                     + "`pop esi/pop ebx` 结尾重放 `pop ecx/pop ebp/ret`。门：ebx>=0x410000 且 "
                     + "[ebx]==0x6AC8C8(TPlayer)。参数 0 = movzx byte [ebp-4+0x482]（宠物 "
                     + "m_btSlaveExpLevel）；参数 1 = 该宠物在 [ebx+0x4FC]（主人 m_SlaveList）里"
                     + "自顶向下搜到的下标 +1。",
            },
            new()
            {
                ConfigKey = "BB死亡触发", ScriptLabel = "@BBKill",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D4B75 },
                HostTargets = new uint[] { 0x0076631C }, HostResumes = new uint[] { 0x00766321 },
                DispatchSlot = Slot.WithParams, ParamCount = 1, Action = HostAction.Notify, Wired = true,
                Note = "挂 TBaseObject.Die 的序言 0x76631C `55 8B EC 53 56`（桩体原样重放）。"
                     + "四道门：返回地址 [ebp+4]==0x71E2EF（只认 0x71E2EA 那条 TAnimal 死亡路径）、"
                     + "eax>=0x400000、[eax]!=0x6AC8C8（死者不是玩家）、[eax+0x38C]>=0x400000 且"
                     + "为 TPlayer（主人）。This_Player = 主人；参数 0 = 死者 +0x106 处的"
                     + "ShortString（0x405774 转长串 → 0x41B238 转 Variant）= m_sCharName。"
                     + "派发在死亡处理**之前**。",
            },
            new()
            {
                ConfigKey = "英雄穿戴触发", ScriptLabel = "@HeroEquiepchange",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D4417, 0x100D4469 },
                HostTargets = new uint[] { 0x0075F08C, 0x0075EA31 },
                HostResumes = new uint[] { 0x0075F093, 0x0075EA37 },
                DispatchSlot = Slot.WithParams, ParamCount = 6, Action = HostAction.Notify, Wired = false,
                Note = "两个桩体挂 TEquipContainer 的穿/脱两条路径。0x75EA31 那支门为"
                     + "[ebp+4]==0x75EF64 且 [[ebp+0x10]]!=0x6AC8C8（对象不是玩家 = 英雄），"
                     + "This_Player = [hero+0x68C]（主人）。六个 Variant 依次为："
                     + "①保存的 EDX = 装备位置；②dword [item+0x28]；③dword [item+0x2C]；"
                     + "④dword [[item+0x1C]+0x14]；⑤[item+0x1C]+4 处的 ShortString；⑥VType=2 值 0。"
                     + "【BLOCKED】②③是 208 字节物品记录 blob 偏移 0x08/0x0C 处的**整 dword**"
                     + "（跨 DuraMax 与 btValue 字段），④落在 TStdItem+0x14 —— 这三项的字段切分"
                     + "没有逐字节证据，凑数就是臆造，故本条只留档不发射。",
            },
            new()
            {
                ConfigKey = "新穿戴触发", ScriptLabel = "@MyEquiepchange",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D3509, 0x100D355B },
                HostTargets = new uint[] { 0x0075F085, 0x0075EA37 },
                HostResumes = new uint[] { 0x0075F08C, 0x0075EA3C },
                DispatchSlot = Slot.WithParams, ParamCount = 6, Action = HostAction.Notify, Wired = false,
                Note = "英雄穿戴的玩家版，桩体逐字节同形，只有两处不同："
                     + "门是 [[ebp+0x10]]==0x6AC8C8（必须是玩家），且 This_Player 直接用 esi "
                     + "而不是 [esi+0x68C]。参数向量与 @HeroEquiepchange 同，同样 BLOCKED。",
            },
            new()
            {
                ConfigKey = "上线触发", ScriptLabel = "@initys",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D5968 },
                HostTargets = new uint[] { 0x006548BD }, HostResumes = new uint[] { 0x006548C2 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = false,
                Note = "门为 [ebp+4]==0x6542D2（sub_654748 被 0x6542CD 调用那一次）、"
                     + "ebx>=0x410000、[ebx]==0x6AC8C8。这解开了 REPLICATION_RULES §5.2 里"
                     + "「initys 在生产树 3326 个文件 0 命中」那条：initys 不是文件名也不是 API，"
                     + "是**上线触发**发出的脚本标签 @initys。"
                     + "【故意不接】§5.2 明令回收系统必须按门①→门②→配置快照→开关→产出的顺序推进，"
                     + "在这条链修好之前放开 @initys 会直接打开删装备的闸门。",
            },
            new()
            {
                ConfigKey = "死亡触发", ScriptLabel = "@OnDie",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AD427 },
                HostTargets = new uint[] { 0x006C09B5 }, HostResumes = new uint[] { 0x006C09BA },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = false,
                Note = "This_Player = [ebp-4]。桩体尾部重放 `pop edi/pop esi/pop ebx/pop ecx/pop ecx`。",
            },
            new()
            {
                ConfigKey = "回城按钮触发", ScriptLabel = "@OnBackButton",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AD628 },
                HostTargets = new uint[] { 0x006DBB80 }, HostResumes = new uint[] { 0x006DBB85 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = false,
                Note = "This_Player = eax（`8B D0 mov edx,eax`）。",
            },
            new()
            {
                ConfigKey = "挖矿触发", ScriptLabel = "@OnDig",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AE0E1 },
                HostTargets = new uint[] { 0x006EC111 }, HostResumes = new uint[] { 0x006EC116 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = false,
                Note = "This_Player = ebx。桩体尾部重放被覆盖的 `66 83 7E 26 00 cmp word [esi+0x26],0`。",
            },
            new()
            {
                ConfigKey = "心灵启示触发", ScriptLabel = "@Revelation",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AE7F5 },
                HostTargets = new uint[] { 0x006EDC2B }, HostResumes = new uint[] { 0x006EDC30 },
                DispatchSlot = Slot.WithParams, ParamCount = 2, Action = HostAction.Replace, Wired = false,
                Note = "与两个召唤一样落在魔法分发臂上且不重放被覆盖的 call。",
            },
            new()
            {
                ConfigKey = "复活触发脚本", ScriptLabel = "@OnDia",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D1DE6 },
                HostTargets = new uint[] { 0x0073C484 }, HostResumes = new uint[] { 0x0073C48A },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = false,
                Note = "门 [ebx]==0x6AC8C8；This_Player = ebx。",
            },
            new()
            {
                ConfigKey = "被击杀触发", ScriptLabel = "@MyKill",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D26FD },
                HostTargets = new uint[] { 0x00766624 }, HostResumes = new uint[] { 0x00766629 },
                DispatchSlot = Slot.WithParams, ParamCount = 2, Action = HostAction.Notify, Wired = false,
            },
            new()
            {
                ConfigKey = "捡物触发", ScriptLabel = "@pickpre",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D2BA2 },
                HostTargets = new uint[] { 0x006B770C }, HostResumes = new uint[] { 0x006B7711 },
                DispatchSlot = Slot.WithParams, ParamCount = 2, Action = HostAction.Notify, Wired = false,
            },
            new()
            {
                ConfigKey = "攻击触发", ScriptLabel = "@MyAttack",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D2D50 },
                HostTargets = new uint[] { 0x0076E35D }, HostResumes = new uint[] { 0x0076E362 },
                DispatchSlot = Slot.WithParams, ParamCount = 4, Action = HostAction.Notify, Wired = false,
            },
            new()
            {
                ConfigKey = "魔法攻击触发", ScriptLabel = "@MyMagicAttack",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D2F0B },
                HostTargets = new uint[] { 0x0076DE84 }, HostResumes = new uint[] { 0x0076DE8A },
                DispatchSlot = Slot.WithParams, ParamCount = 5, Action = HostAction.Notify, Wired = false,
            },
            new()
            {
                ConfigKey = "盘古穿戴触发", ScriptLabel = "@ChangeEquip",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AD8D1, 0x100AD91D },
                HostTargets = new uint[] { 0x006D8E35, 0x006D8E4D },
                HostResumes = new uint[] { 0x006D8E3A, 0x006D8E52 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = false,
            },
            new()
            {
                ConfigKey = "盘古魔法攻击触发", ScriptLabel = "@MagicAttack",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100ADDBF, 0x100ADE0E },
                HostTargets = new uint[] { 0x0076E1AF, 0x0076DEC0 },
                HostResumes = new uint[] { 0x0076E1B6, 0x0076DEC7 },
                DispatchSlot = Slot.WithParams, ParamCount = 3, Action = HostAction.Notify, Wired = false,
            },
            new()
            {
                ConfigKey = "刀刀切割", ScriptLabel = "@Cutting",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100CF36E },
                HostTargets = new uint[] { 0x00767BAE }, HostResumes = new uint[] { 0x00767BB4 },
                DispatchSlot = Slot.WithParams, ParamCount = 0, Action = HostAction.Notify, Wired = false,
                Note = "703 dword 的大桩体，主体是就地算伤害，标签注册点 0x100CF401。",
            },
            new()
            {
                ConfigKey = "新倍攻和暴击", ScriptLabel = "@baoji",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D3BC4 },
                HostTargets = new uint[] { 0x0076C88B }, HostResumes = new uint[] { 0x0076C890 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = false,
            },
            new()
            {
                ConfigKey = "英雄倍攻和暴击", ScriptLabel = "@Herobaoji",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D49B4 },
                HostTargets = new uint[] { 0x0076C816 }, HostResumes = new uint[] { 0x0076C81D },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = false,
            },
        };

        /// <summary>全族清单（只读）。审计工具与诊断面板用。</summary>
        public static IReadOnlyList<Descriptor> Registry => _registry;

        public static Descriptor Find(string configKey)
        {
            foreach (var d in _registry)
            {
                if (string.Equals(d.ConfigKey, configKey, StringComparison.Ordinal))
                    return d;
            }
            return null;
        }

        // ── 惰性门 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 插件在不在。插件缺席时这里只读两个字段就返回 false：不分配对象、不查配置、
        /// 不碰脚本引擎。原生等价物是「trampoline 根本没被安装」。
        /// </summary>
        public static bool Armed
        {
            get
            {
                var manager = M2Share.PluginManager;
                if (manager == null) return false;
                var plugin = manager.GetPlugin("YanshenCompat");
                return plugin != null && plugin.State == PluginState.Running;
            }
        }

        /// <summary>
        /// 单个触发点的开关。原生这道门在**安装期**：0x100AE3DD
        /// <c>cmp [singleton+0x794],0x64</c>（§4.22 的「已打标记=100」），配置为 0 时
        /// 那段安装代码整个跳过，桩体从不存在。C# 没有自改代码，只能每次入口再问一次；
        /// 语义等价（json 值 != 0）。
        /// </summary>
        private static bool Enabled(string configKey)
        {
            if (!Armed) return false;
            return new YanshenApi(null, null, M2Share.PluginManager).PatchToggleOn(configKey);
        }

        // ── 派发原语 ────────────────────────────────────────────────────────────

        private static long _dispatchCount;

        /// <summary>
        /// 诊断计数器：每次真正发起一次脚本派发时 +1。产品逻辑从不读它，它存在的唯一
        /// 目的是让审计工具能把「插件缺席时这一层一次都没跑」变成可断言的事实。
        /// </summary>
        public static long DispatchCount => Interlocked.Read(ref _dispatchCount);

        /// <summary>最近一次派发的标签，同样只用于审计。</summary>
        public static string LastDispatchedLabel { get; private set; }

        /// <summary>TSTDScript VMT+0x44：无参标签调用。</summary>
        private static void DispatchPlain(TPlayObject thisPlayer, string label)
        {
            Interlocked.Increment(ref _dispatchCount);
            LastDispatchedLabel = label;
            M2Share.g_FunctionNPC?.GotoLable(thisPlayer, label, false);
        }

        /// <summary>
        /// TSTDScript VMT+0x48：带 <c>array of Variant</c> 的标签调用。
        /// 原生把标签的 <c>'@'</c> 剥掉后当过程名执行（0x733C3F <c>cmp byte [esi],0x40</c>），
        /// C# 侧 <c>NormNpc.TryCallPascalCallback</c> 走的是同一套 <c>_名字 / 名字</c> 解析，
        /// 与 <c>PasScriptHost.TryCallNpcLabel</c> 对 <c>@标签</c> 的解析一致。
        /// </summary>
        private static void DispatchWithParams(TPlayObject thisPlayer, string label,
            params PasValue[] args)
        {
            Interlocked.Increment(ref _dispatchCount);
            LastDispatchedLabel = label;
            var npc = M2Share.g_FunctionNPC;
            if (npc == null) return;
            var name = label.Length > 0 && label[0] == '@' ? label.Substring(1) : label;
            npc.TryCallPascalCallback(thisPlayer, name, args);
        }

        // ── 已接通的触发点 ───────────────────────────────────────────────────────

        /// <summary>
        /// 召唤神兽触发（<c>@SummonShinsu</c>，宿主 0x6EDC5E）。
        /// <para>返回 true 表示原生造宠被顶掉 —— 因为桩体没有重放
        /// <c>call 0x76EE7C</c>，开关打开后神兽完全交给脚本。</para>
        /// </summary>
        public static bool FireSummonShinsu(TPlayObject caster)
        {
            if (!Armed || caster == null) return false;
            if (!Enabled("召唤神兽触发")) return false;
            DispatchPlain(caster, "@SummonShinsu");
            return true;
        }

        /// <summary>
        /// 召唤骷髅触发（<c>@SummonSkele</c>，宿主 0x6EDB44）。返回 true 表示原生
        /// <c>call 0x76EDFC</c> 被顶掉。
        /// </summary>
        public static bool FireSummonSkele(TPlayObject caster)
        {
            if (!Armed || caster == null) return false;
            if (!Enabled("召唤骷髅触发")) return false;
            DispatchPlain(caster, "@SummonSkele");
            return true;
        }

        /// <summary>
        /// BB杀怪触发（<c>@BBupr</c>，宿主 sub_71F3D0 收尾 0x71F467）。
        /// 纯通知，不改变宿主行为。
        /// </summary>
        public static void FireSlaveGainExp(TBaseObject slave)
        {
            if (!Armed || slave == null) return;
            // 0x71F40E cmp [ebx],0x6AC8C8 —— This_Player 必须是 TPlayer。
            if (slave.m_Master is not TPlayObject master) return;
            if (!Enabled("BB杀怪触发")) return;

            var index = ResolveNativeSlaveOrdinal(master, slave);
            // 0x71F075 `cmp ecx,0 / jl bail`：空表（ecx 减到 -1）整条跳过。
            if (index < 0) return;

            DispatchWithParams(master, "@BBupr",
                PasValue.FromInt(slave.m_btSlaveExpLevel),
                PasValue.FromInt(index + 1));
        }

        /// <summary>
        /// 复刻 0x71F058..0x71F07B 的下标搜索，包括它的两个边角：
        /// <list type="bullet">
        /// <item><c>mov ecx,[list+8]</c> 先取 Count 再 <c>dec</c>，所以 Count==0 时
        /// ecx 变成 -1，<c>jl</c> 生效 → 整条事件跳过（返回 -1）。</item>
        /// <item>自顶向下扫到 ecx==0 仍没命中时循环靠 <c>jg</c> 落空退出，ecx 停在 0，
        /// <c>jl</c> 不成立 → 事件照发，序号按 0 算（返回 0）。**没找到不等于不发**。</item>
        /// </list>
        /// </summary>
        public static int ResolveNativeSlaveOrdinal(TPlayObject master, TBaseObject slave)
        {
            var list = master?.m_SlaveList;
            if (list == null || list.Count == 0) return -1;
            for (var i = list.Count - 1; i > 0; i--)
            {
                if (ReferenceEquals(list[i], slave)) return i;
            }
            return 0;
        }

        /// <summary>
        /// BB死亡触发（<c>@BBKill</c>，宿主 TBaseObject.Die 序言 0x76631C）。
        /// 必须在死亡处理**之前**调用：原生改写的是序言，桩体先跑完派发才重放
        /// <c>push ebp / mov ebp,esp / push ebx / push esi</c> 并 jmp 回 0x766321。
        /// </summary>
        public static void FireSlaveDie(TBaseObject dying)
        {
            if (!Armed || dying == null) return;
            // 0x76631D cmp [eax],0x6AC8C8 / je bail —— 死者不能是玩家。
            if (dying is TPlayObject) return;
            // 0x766329 cmp [eax+0x38C],0x400000 / jb bail，随后 0x766341 cmp [ebx],0x6AC8C8。
            // 英雄的 +0x38C 恒为 NULL（0x690B0E），所以这道门天然把英雄排除在外。
            if (dying.m_Master is not TPlayObject master) return;
            if (!Enabled("BB死亡触发")) return;

            DispatchWithParams(master, "@BBKill",
                PasValue.FromString(dying.m_sCharName ?? string.Empty));
        }
    }
}
