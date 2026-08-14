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
    public static partial class YanshenTriggerDispatch
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
                     + "没有逐字节证据，凑数就是臆造，故本条只留档不发射。"
                     + "本轮补上建桩坐标以便后续解码：站点①0x100D4417 count=0x134 缓冲 [ebp-0x75EC]、"
                     + "站点②0x100D4469 count=0x140 缓冲 [ebp-0x4028]，两者都是 `movaps/movups` 逐 16 字节"
                     + "拼装（不是 `rep movsd` 单模板），即审计 §5 C2 里点名的「8 个未回收模板」之二。",
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
                     + "而不是 [esi+0x68C]。参数向量与 @HeroEquiepchange 同，同样 BLOCKED。"
                     + "建桩坐标：站点①0x100D3509（host 0x75F085→0x75F08C）、"
                     + "站点②0x100D355B count=0x12E 缓冲 [ebp-0x3844]（host 0x75EA37→0x75EA3C），"
                     + "同为 `movaps` 拼装模板。",
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
                     + "在这条链修好之前放开 @initys 会直接打开删装备的闸门。"
                     + "本轮补齐桩体证据：77 dword 拼进 [ebp-0xC30]（`movaps` 拼装 + 尾 `mov dword "
                     + "[ebp-0xB00],0xE9`）→ 81 字节，三道门后 `pushal/pushfd` → 派发 → `popfd/popal` → "
                     + "尾部重放被覆盖的 5 字节 `5F 5E 5B 8B E5`(pop edi/esi/ebx + mov esp,ebp) → jmp 0x6548C2"
                     + "(`5D C2 04 00` pop ebp / ret 4) → Notify。门①的 `[ebp+4]==0x6542D2` 已亲验："
                     + "0x6542CD `E8 76 04 00 00 call 0x654748`，返回地址正是 0x6542D2，而 sub_654748 的"
                     + "序言 0x65475A..0x65475F `mov [ebp-4],ecx / mov ebx,edx / mov esi,eax` 说明门②③"
                     + "校验的 ebx 就是它的 edx 形参。所以这不是「函数级」通知，是「只认那一个调用点」的通知。",
            },
            new()
            {
                ConfigKey = "死亡触发", ScriptLabel = "@OnDie",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AD427 },
                HostTargets = new uint[] { 0x006C09B5 }, HostResumes = new uint[] { 0x006C09BA },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = true,
                Note = "This_Player = [ebp-4]。桩体尾部重放 `pop edi/pop esi/pop ebx/pop ecx/pop ecx`。"
                     + "宿主是 TPlayer.Die（起始 0x6C03F8，异常串 0x6C09C4/0x6C0A0C `[Exception]: "
                     + "TPlayer.Die -2/-5`），钩子挂在其唯一 epilogue 0x6C09B5，即所有 SEH finally "
                     + "汇合之后。[ebp-4] 就是死者自己（0x6C0883 `mov [[ebp-4]+0x354],凶手`）。"
                     + "C# 落点：TPlayObject.Die override 末尾（base.Die() 之后）。",
            },
            new()
            {
                ConfigKey = "回城按钮触发", ScriptLabel = "@OnBackButton",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AD628 },
                HostTargets = new uint[] { 0x006DBB80 }, HostResumes = new uint[] { 0x006DBB85 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Replace, Wired = true,
                Note = "This_Player = eax（`8B D0 mov edx,eax`）。**动作语义已由本轮亲验更正：Replace(顶掉型)，"
                     + "不是 Notify**——33 字节桩体（q06 site 0x100AD628）是 `pushal / mov edx,eax / 取 "
                     + "TSTDScript / push 标签 / push 0 / call [ebx+0x44] / popal / jmp 0x6DBB85`，**没有重放"
                     + "被覆盖的 5 字节 `E8 E7 D6 01 00 call 0x6F926C`**。0x6F926C 是 SEH 包裹的真实回城处理器"
                     + "（0x6F926C push ebp…检查 [player+0x128] 标志位 + call 0x772960 门 + 传送），"
                     + "开关打开后原生回城被脚本 @OnBackButton 顶替。宿主 0x6DBB80 是 TPlayObject 客户端命令"
                     + "分发器的一条臂（尾 `jmp 0x6DBC2C`，与盘古穿戴同一分发器=TPlayObject.Message.cs，禁改文件）。"
                     + "安装参数在 0x100AD5BB..0x100AD628 逐条可读：push 0x21(=33 个模板元素) / "
                     + "push lea[ebp-0x7F4](模板) / push 0x6DBB85(resume) / push 0x6DBB80 ×2(patch+target) / "
                     + "push lea[ebp-0xC4](出参)。模板由 8 条 movaps 常量(0x102D1520/0x102D37A0/0x102D2830/"
                     + "0x102D18D0/0x102D3450/0x102D1E40/0x102D2780 + 一次 xorps 清零)加一条 "
                     + "`mov dword[ebp-0x774],0xE9` 拼成，33 个元素按「一 dword 装一字节」展开成 37 字节："
                     + "`60 pushal / 8B D0 mov edx,eax / A1 20 5D 7D 00 / 8B 00 / 8B F0 / 8B 7E 08 / "
                     + "68 <@OnBackButton> / 6A 00 / 33 C9 / 8B C7 / 8B 18 / FF 53 44 / 61 popal / "
                     + "E9 -> 0x6DBB85`（末元素 0xE9 的 rel32 由后段用 [ebp+0x14] 补）。"
                     + "C# 插桩点不在分发器：sub_6F926C 全镜像**只有 1 个 rel32 调用者**（就是被顶掉的 "
                     + "0x6DBB80），且作为绝对 dword 出现 0 次（不在任何虚表里）。故「在唯一调用点跳过这次 "
                     + "call」与「进 ClientClickBackHome 就返回」严格同义，门放在 "
                     + "TPlayObject.ClientClickBackHome 的第一条语句即可，无需改禁改的 TPlayObject.Message.cs。"
                     + "宿主 sub_6F926C 与 C# ClientClickBackHome 的对应由三道门互证："
                     + "[map+0x7C]/[map+0x6C] 两个 bool、状态 0x33 配 [player+0x3C0]、状态 0x34。",
            },
            new()
            {
                ConfigKey = "挖矿触发", ScriptLabel = "@OnDig",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AE0E1 },
                HostTargets = new uint[] { 0x006EC111 }, HostResumes = new uint[] { 0x006EC116 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = true,
                Note = "This_Player = ebx。桩体尾部重放被覆盖的 `66 83 7E 26 00 cmp word [esi+0x26],0`。"
                     + "宿主是 ClientHitXY(sub_6EC078) 的挖矿臂：0x6EC0F1 `cmp di,0xBC7`(CM_HEAVYHIT) → "
                     + "0x6EC0FE `cmp [map+0x6A],0`(boMINE) → 0x6EC104 武器非空 → 0x6EC10B "
                     + "`cmp [std+0x15],0x13`(Shape==19) → 【钩子】0x6EC111 `cmp word[weapon+0x26],0`(Dura>0)。"
                     + "所以 @OnDig 在「重击落在自身格 + boMINE + 手持 shape-19 镐」时发射，早于 Dura 门与 "
                     + "GetFrontPosition。C# 落点：TPlayObject.ClientHitXY 挖矿检测处，惰性门 Armed 打头。",
            },
            new()
            {
                ConfigKey = "心灵启示触发", ScriptLabel = "@Revelation",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AE7F5 },
                HostTargets = new uint[] { 0x006EDC2B }, HostResumes = new uint[] { 0x006EDC30 },
                DispatchSlot = Slot.WithParams, ParamCount = 2, Action = HostAction.Replace, Wired = false,
                Note = "与两个召唤一样落在魔法分发臂上且不重放被覆盖的 call。本轮把桩体解出来了："
                     + "88 dword 由 0x100AE6xx 一串 `movaps/movups` 拼进 [ebp-0xC34]（不是 rep movsd 单模板）"
                     + "→ 92 字节。确认 **Replace**：不重放 0x6EDC2B 的 `E8 F4 67 08 00 call 0x774424`，"
                     + "直接 jmp 0x6EDC30（`jmp 0x6EE04B` = DEFAULT 汇聚）。0x774424 全镜像**只有 0x6EDC2B "
                     + "一个调用者**，故「拦生产函数入口」与「拦调用点」等价（同 0x76EE7C / 0x76EDFC 的先例）。"
                     + "This_Player = ebx。两个 Variant 由桩体**就地手搓**（不走 0x41AFE4）："
                     + "+0x017 `mov [ebp-0x6C],3`(varInteger) / +0x01E 值 = 宿主帧的 `[ebp-4]`；"
                     + "+0x024 `mov [ebp-0x5C],3` / +0x02B 值 = 宿主帧的 `[ebp-0xC]`。"
                     + "本轮把分发器定名了：sub_6ED62C（全镜像唯一 rel32 调用者 0x6BCCB3），序言 "
                     + "0x6ED635 `mov [ebp-4],ecx` / 0x6ED638 `mov esi,edx`(TUserMagic) / 0x6ED63A "
                     + "`mov ebx,eax`(施法者)；0x6ED676 `call 0x78FE88(eax=[ebp-4], edx=[ebp+0xC], "
                     + "ecx=[ebx+0x12C], push [ebx+0x130])` ⇒ **[ebp-4] = 目标 X、[ebp+0xC] = 目标 Y**。"
                     + "0x6ED6FF `jmp dword [eax*4+0x6ED706]` 是按 wMagicID 的跳转表，表项 28 = 0x6EDC24 "
                     + "正是本挂载点所在臂，而 C# 的 `SpellsDef.SKILL_SHOWHP == 28` —— 键名「心灵启示」对得上。"
                     + "0x774424 的语义也解开了：`Random(100) < (magicLevel+1)*5+10` 命中则对目标 "
                     + "`call 0x76B4D0(dl=0x1D)` 挂 state 29。"
                     + "【仍缺口，但理由已收窄成一条硬事实】第二个 Variant 取的 `[ebp-0xC]` 在通往臂 28 的"
                     + "整条路径上**从未被写过**：序言 0x6ED62C..0x6ED6FF 只写 [ebp-4] 与 [ebp-8] 那个 "
                     + "dword 的四个字节（[ebp-5]/[ebp-6]/[ebp-7]/[ebp-8]），而 [ebp-0xC] 的唯一写点是 "
                     + "0x6ED956（臂 6 的 `call 0x75EC20` 取物品），与臂 28 互斥。也就是说原生这里读的是"
                     + "**未初始化的栈残值**，C# 侧不存在等价物，任何取值都是臆造 ⇒ 保持不发射。",
            },
            new()
            {
                ConfigKey = "复活触发脚本", ScriptLabel = "@OnDia",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D1DE6 },
                HostTargets = new uint[] { 0x0073C484 }, HostResumes = new uint[] { 0x0073C48A },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = false,
                Note = "门 [ebx]==0x6AC8C8；This_Player = ebx。本轮把桩体解出来了：59 dword 拼进 "
                     + "[ebp-0x3D0]（`movaps` 拼装 + 三条 `mov dword` 尾巴 0x44/0x61/0xE9）→ 63 字节。"
                     + "**开头就重放**被覆盖的 6 字节 `33 D2 52 50 8B C6`（xor edx,edx / push edx / push eax / "
                     + "mov eax,esi），再 `test ebx,ebx / je` + `cmp [ebx],0x6AC8C8 / jne` 两道门，"
                     + "`pushal`（**无 pushfd**）→ 派发 → `popal` → jmp 0x73C48A。"
                     + "宿主 sub_73C208 = THumanKind.Run（VMT 槽由 0x73BCBC 落在 THumanKind VMT 0x73BC34+0x88 定案；"
                     + "调用者 0x68A45A/0x68A5B8 英雄 Run、0x6B2DE0 玩家 Run）。钩子落在「复活机会倒计时」段："
                     + "0x73C47A 引用 0x73C784「您将在 」、0x73C48A `sub eax,[ebx+0x450]` / `fild` / "
                     + "`fdiv [0x73C78C]` 算剩余秒、0x73C4BC 引用 0x73C798「 秒后获得一次复活机会」。"
                     + "【缺口】C# **没有移植这段倒计时**：全仓 grep「复活机会」「神龙附体状态结束」零命中，"
                     + "THumanKind.Run 的这一臂在托管端不存在，没有等价落点可挂。"
                     + "补齐所需：先把 sub_73C208 的复活倒计时臂移植过来，再在算剩余秒之前接一次 @OnDia。",
            },
            new()
            {
                ConfigKey = "被击杀触发", ScriptLabel = "@MyKill",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D26FD },
                HostTargets = new uint[] { 0x00766624 }, HostResumes = new uint[] { 0x00766629 },
                DispatchSlot = Slot.WithParams, ParamCount = 2, Action = HostAction.Notify, Wired = true,
                Note = "桩体 = .rdata 0x102CF4E8 x196 模板（`rep movsd` @0x100D26E9）→ 209 字节。"
                     + "开头 `8B 45 FC 8B 10` 原样重放被覆盖的 5 字节 → Notify。三道门："
                     + "+0x006 `cmp edx,0x6AC8C8`（[ebp-4] 必须是 TPlayer）、+0x018 "
                     + "`cmp ebx,0x400000`、+0x024 `cmp [ebx],0x6AC8C8`，其中 "
                     + "ebx = [victim+0x34C] = **m_ExpHitter**（+0x34C/+0x354/+0x344 = "
                     + "ExpHitter/LastHiter/TargetCret，见 0x71E305 与 0x71E310 死亡时成对清零）。"
                     + "This_Player = [ebp-4] = 死者本人；两个 Variant 都取自**凶手**："
                     + "①+0x04A `lea edx,[ebx+0x106]` ShortString → m_sCharName；"
                     + "②+0x07A `mov edx,[ebx+0x128]` / `mov edx,[edx+0x48]` = m_PEnvir.sMapDesc"
                     + "（+0x128=m_PEnvir 全仓已定；+0x48 由 0x6EA471 的 Format 实参向量定案 —— "
                     + "格式串 0x6EA584 `%s在%s[%d,%d]施放%s，请大家前往观看.` 的第二个 %s，"
                     + "C# 既有端口 TPlayObject.NativeFireworkText.cs:59 用的正是 m_PEnvir.sMapDesc）。"
                     + "宿主 sub_7663BC = TCreature.Run（异常串 0x766848 `[Exception]: TCreature.Run - `），"
                     + "钩子在 `[self+0x74]==0`(未死) → `[self+0x2AC]<=0`(HP) → `call [vmt+8]`(复活尝试) "
                     + "返回 false 之后、`call [vmt+0x84]`(Die) **之前**。"
                     + "C# 落点：TBaseObject.Base.cs 的 Run —— `TryNativeRevive()` 后的 "
                     + "`if (m_WAbil.HP == 0) { Die(); }` 里，紧挨 Die() 之前。",
            },
            new()
            {
                ConfigKey = "捡物触发", ScriptLabel = "@pickpre",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D2BA2 },
                HostTargets = new uint[] { 0x006B770C }, HostResumes = new uint[] { 0x006B7711 },
                DispatchSlot = Slot.WithParams, ParamCount = 2, Action = HostAction.Notify, Wired = true,
                Note = "桩体 = .rdata 0x102CCCE0 x140 模板（`rep movsd` @0x100D2B8E）→ 153 字节。"
                     + "开头 `8B 55 FC 8B C3` 原样重放被覆盖的 5 字节 → Notify。**无门**。"
                     + "This_Player = eax = ebx = self（+0x017 `push eax` … +0x06D `pop edx`）。"
                     + "两个 Variant：①+0x01B `mov edx,[[ebp-4]+0x1C]` / `lea edx,[edx+4]` 处的 "
                     + "ShortString = TStdItem 名（TStdItem +4 起是 ShortString，与 @HeroEquiepchange "
                     + "第 5 参同源）；②+0x048 `mov edx,[ebx+0x128]` / `[edx+0x48]` = self.m_PEnvir.sMapDesc。"
                     + "宿主 sub_6B74D8 = ClientPickUpItem（串 0x6B7800「一定时间范围内，不能拾取。」/ "
                     + "0x6B7868「无法再拾取更多物品。」），钩子在 0x6B7708 `push 4` / 0x6B770A `mov cl,1` "
                     + "之后、0x6B7713 `call [vmt+0x248]`(AddItemToBag) **之前**，且已过 DeleteFromMap。"
                     + "C# 落点：TPlayObject.ClientPickUpItem，DeleteFromMap 成功且发过 RM_ITEMHIDE 之后、"
                     + "AddItemToBag 之前。金币臂在此之前就 return，故不发。",
            },
            new()
            {
                ConfigKey = "攻击触发", ScriptLabel = "@MyAttack",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D2D50 },
                HostTargets = new uint[] { 0x0076E35D }, HostResumes = new uint[] { 0x0076E362 },
                DispatchSlot = Slot.WithParams, ParamCount = 4, Action = HostAction.Notify, Wired = true,
                Note = "桩体 = .rdata 0x102CC6F8 x206 模板 → 222 字节，尾部 +0x0D4 重放被覆盖的 5 字节 "
                     + "`68 C8 00 00 00 push 0xC8` 后 jmp 0x76E362 → Notify，**无门**。This_Player = ebx。"
                     + "四个 Variant：①`[ebp-8]` 经 0x41AFE4(cl=0xFC) → 伤害值；②`[esi+0x106]` ShortString "
                     + "→ 被打者 m_sCharName；③`[[ebx+0x128]+0x48]` = 攻击者 m_PEnvir.sMapDesc；"
                     + "④VType=2(varSmallint)，`cmp [esi],0x6AC8C8` 命中写 1、否则留 0 = 「被打者是玩家」。"
                     + "宿主 sub_76E268（29 个调用者：怪物 0x66CBxx、英雄 0x690Dxx、魔法特效 0x771xxx…），"
                     + "钩子在 0x76E357 `cmp [ebp-8],0 / jle` 之后、0x76E369 `call 0x76B4F8`（对 esi 落伤害）之前。"
                     + "序言 0x76E271 `mov [ebp-4],ecx` / 0x76E274 `mov esi,edx` / 0x76E276 `mov ebx,eax` ⇒ "
                     + "ebx=攻击者、esi=被打者、[ebp-8]=0x76E2AF 存下的 `call [tgt_vmt+0x104]` 返回值=伤害。"
                     + "sub_76E268 的 C# 端口就是 `TBaseObject.ApplyNativeDirectMagicEffect`（该方法本就按 "
                     + "0x76E284/0x76E2B2/0x76E357/0x76E35D 逐行注释移植，只是没接触发），"
                     + "落点 = `if (damage > 0)` 内、`SendDelayMsg` 之前。已接线（Wave3）。",
            },
            new()
            {
                ConfigKey = "魔法攻击触发", ScriptLabel = "@MyMagicAttack",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D2F0B },
                HostTargets = new uint[] { 0x0076DE84 }, HostResumes = new uint[] { 0x0076DE8A },
                DispatchSlot = Slot.WithParams, ParamCount = 5, Action = HostAction.Notify, Wired = true,
                Note = "桩体 = .rdata 0x102CD3D0 x255 模板 → 277 字节。被覆盖的 6 字节是 "
                     + "`8B F0 85 F6 7E 2C`，桩体**拆成两半重放**：开头 +0x000 `mov esi,eax`，"
                     + "结尾 +0x0FE `test esi,esi` 后用两条 jmp 复现 `jle 0x76DEB6` / 直落 0x76DE8A → Notify。"
                     + "This_Player = ebx。五个 Variant：①esi(=返回的伤害) 经 0x41AFE4；"
                     + "②`[[ebp-4]+0x106]` ShortString = 被打者名；③`[[ebx+0x128]+0x48]` = 施法者 "
                     + "m_PEnvir.sMapDesc；④varSmallint，`[[ebp-4]]==0x6AC8C8` 则 1 = 「被打者是玩家」；"
                     + "⑤`[ebp-8]` 经 0x41AFE4。宿主钩子紧跟 0x76DE7E `call [target_vmt+0x104]`（算魔法伤害）。"
                     + "本轮定名：宿主函数 = sub_76DE1C（`55 8B EC 83` 序言，全镜像唯一 rel32 调用者 "
                     + "0x766E06，位于 TCreature.GetAndExecMsg sub_766878 内），C# 端口 = "
                     + "`TBaseObject.ApplyNativeSingleMagicEffect`。帧槽：0x76DE27 `mov [ebp-4],edx` = 被打者；"
                     + "0x76DE24 `mov [ebp-8],ecx`，其身份由 0x76DEAB `mov dx,word [ebp-8]` → `call 0x772468` "
                     + "钉死 —— 该 call 的 C# 端口是 `ConsumeNativeOneShotMagicDamage(payload.SkillId)`，"
                     + "故 **[ebp-8] = SkillId**。落点 = `ResolveFullMagicDamage` 之后、`if (damage > 0)` 之前"
                     + "（伤害非正也发）。已接线（Wave3）。",
            },
            new()
            {
                ConfigKey = "盘古穿戴触发", ScriptLabel = "@ChangeEquip",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100AD8D1, 0x100AD91D },
                HostTargets = new uint[] { 0x006D8E35, 0x006D8E4D },
                HostResumes = new uint[] { 0x006D8E3A, 0x006D8E52 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = true,
                Note = "This_Player = [ebp-4] = self。两个桩体挂在 TPlayObject 客户端命令分发器"
                     + "（起始 0x6D7794）的两条臂的尾 jmp 上：0x6D8E30 `call 0x6B7E9C`(ClientTakeOnItems，"
                     + "唯一调用者) 之后的 0x6D8E35；0x6D8E48 `call 0x6B8188`(ClientTakeOffItems，唯一调用者)"
                     + "之后的 0x6D8E4D。两桩体都不重放（被覆盖的原指令本就是 `jmp 0x6DBC2C` 默认标签），"
                     + "所以是「原生穿/脱执行完 → 发 @ChangeEquip → 回分发器默认流」。因两处理器各仅一个调用者，"
                     + "C# 等价落点 = CM_TAKEONITEM / CM_TAKEOFFITEM 两个 case 调用之后（TPlayObject.Message.cs）。",
            },
            new()
            {
                ConfigKey = "盘古魔法攻击触发", ScriptLabel = "@MagicAttack",
                Builder = 0x10032FD0, BuilderSites = new uint[] { 0x100ADDBF, 0x100ADE0E },
                HostTargets = new uint[] { 0x0076E1AF, 0x0076DEC0 },
                HostResumes = new uint[] { 0x0076E1B6, 0x0076DEC7 },
                DispatchSlot = Slot.WithParams, ParamCount = 3, Action = HostAction.Notify, Wired = true,
                Note = "第二个站点（0x76DEC0→0x76DEC7）本轮解出：140 dword 拼进 [ebp-0xE64] → 147 字节，"
                     + "尾部重放被覆盖的 7 字节 `80 BB B6 01 00 00 00 cmp byte [ebx+0x1B6],0` → Notify。"
                     + "This_Player = ebx。三个 Variant 全部**就地手搓**："
                     + "①VType=0x100(varString)，值 = `[[ebp-4]+0x106]` ShortString 经 0x405774 转出的 AnsiString；"
                     + "②VType=2(varSmallint)，`[[ebp-4]]==0x6AC8C8` 则 1；③VType=3(varInteger)，值 = `[ebp-8]`。"
                     + "两个站点是同一条魔法链的前后两处（0x76E1AF 那支同形，只是判 `[esi+0x1B6]` 而非 `[ebx+0x1B6]`，"
                     + "模板 0x8F dword、缓冲另开），所以同一个开关要在两处各发一次。"
                     + "本轮把第一个站点也解出来了（builder 0x100ADDBF，模板 [ebp-0x10A0] x143 → 150 字节）："
                     + "This_Player = esi，被打者 = ebx，三个 Variant 同为 varString([ebx+0x106]) / "
                     + "varSmallint([ebx]==0x6AC8C8) / varInteger([ebp+0x14])，尾部重放 "
                     + "`80 BE B6 01 00 00 00 cmp byte [esi+0x1B6],0`。**两处参数向量确认完全一致**："
                     + "调用者 0x766E01 把 `[ebx+0x24]` 装进 ecx 交给 sub_76DE1C（→ 其 [ebp-8]），"
                     + "0x766E4F 把同一个 `[ebx+0x24]` 压成 sub_76E0B4 的 [ebp+0x14]，两者都是 SkillId。"
                     + "宿主定名：sub_76E0B4（唯一 rel32 调用者 0x766E6A）= "
                     + "`TBaseObject.ApplyNativeAreaMagicEffect`，其 [ebp-4]/[ebp-8] 是 X/Y（0x76E14B/0x76E156 "
                     + "与 [tgt+0x12C]/[tgt+0x130] 比对 = isCenter）；sub_76DE1C = "
                     + "`ApplyNativeSingleMagicEffect`。两个落点分别在 `if (isCenter)` 与 `if (payload.Arg0)` "
                     + "内、`TryApplyNativeState26Single` 之前。已接线（Wave3）。",
            },
            new()
            {
                ConfigKey = "刀刀切割", ScriptLabel = "@Cutting",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100CF36E },
                HostTargets = new uint[] { 0x00767BAE }, HostResumes = new uint[] { 0x00767BB4 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = false,
                Note = "703 dword 的大桩体（.rdata 0x102CA2D8 x703），主体是就地算刀刀切割加成，"
                     + "标签注册点 0x100CF401。**槽已由本轮亲验更正：Plain(0x44)，不是 0x48**——"
                     + "整个 703 字节桩体只有一处派发 `FF 53 44`（偏移 0x2B1），前置 `push 标签 / "
                     + "push 0 / xor ecx,ecx`（Plain 模板，无 array-of-Variant 构造），This_Player=edx="
                     + "攻击者，门为攻击者 S 银行 [edx+0x804+0x200]==0x429(键 S(1,65)) 且 [+0x204]==100。"
                     + "尾部重放被覆盖的 6 字节 `53 56 57 89 4D F8`(push ebx/esi/edi + mov [ebp-8],ecx) → "
                     + "Notify（但**改写了 ecx**，是伤害修改器）。"
                     + "**本轮更正 C# 落点**：0x767BAE 不是「YanshenApi 切割实现」处，而是 **sub_767BA8 的"
                     + "函数入口**——序言 `55 8B EC 83 C4 EC` 占 0x767BA8..0x767BAD，钩子紧接其后，续跑点 "
                     + "0x767BB4 就是 `mov [ebp-4],ecx`，所以被改写的 ecx 同时落进 [ebp-8] 与 [ebp-4]。"
                     + "sub_767BA8 已由 `YanshenApi.cs:1083` 定名为「致命一击调制」，其 C# 端口就是 "
                     + "`TBaseObject.ApplyNativePhysicalCritical(source, damage)`（逐槽吻合：[edx+0x194]="
                     + "source.m_sNativeCriticalChance、[eax+0x19C]=m_sNativeAntiCriticalChance、"
                     + "[eax+0x1A0]=m_sNativeCriticalDamageReduction、[edx+0x198]="
                     + "source.m_nNativeCriticalDamageIncrease，常量 0x767CA4=100.0 / 0x767CA8=10000.0 / "
                     + "0x767CAC=1.5 一一对上）。⇒ 切割加成加在**致命一击倍率之前**。"
                     + "银行槽全部解出（银行布局：槽 i-1 在 (i-1)*8，key 在 +0、value 在 +4）："
                     + "PvE 支 [+0x4C]=S(1,10) 千分比×[target+0x2B0](MaxHP)、[+0x19C]=S(1,52) 概率门、"
                     + "[+0x54]=S(1,11) 定值、[+0x1EC]=S(1,62) 概率门；PvP 支 [+0x44]=S(1,9)（==100 则免疫）、"
                     + "[+0x18C]=S(1,50) 千分比、[+0x1A4]=S(1,53) 概率门、[+0x194]=S(1,51) 定值、"
                     + "[+0x1F4]=S(1,63) 概率门；派发门 key [+0x200]==0x429 且 value [+0x204]==100 = S(1,65)。"
                     + "【仍缺口】①概率门用的是 `((([atk+0x18] & 0xFFF) + [atk+0x470]) & 0xFFF)` 这个"
                     + "**由对象字段合成的伪随机数**（不是 Random），+0x18 与 +0x470 两个字段在 C# 无对应模型；"
                     + "②整条链的前置是 S(1,65)==100，而 S(1,65) 消费者与 +0x18/+0x470 伪随机仍未接线；"
                     + "播种本身已由 <see cref=\"TPlayObject.YanshenSeedLoginSVars\"/>（0x100CE4EA）"
                     + "在 PasScriptHost.TryInitializeYanshen 登录路径落地。两条任一未解就会把每刀伤害算错，故 fail-closed。",
            },
            new()
            {
                ConfigKey = "新倍攻和暴击", ScriptLabel = "@baoji",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D3BC4 },
                HostTargets = new uint[] { 0x0076C88B }, HostResumes = new uint[] { 0x0076C890 },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = true,
                Note = "宿主 sub_76C804 = TBaseObject.GetAttackPower（幸运掷点）：0x76C80A `mov esi,ecx`"
                     + "(nPower) / 0x76C80C `mov edi,edx`(nBasePower) / 0x76C80E `mov ebx,eax`(self)，"
                     + "0x76C816 起按 [ebx+0x84](m_nLuck) 分三路掷点。三个直接调用者 0x767F13 / "
                     + "0x76F491(魔法) / 0x770B6A(物理)；VMT+0xCC 槽（TPlayer/THumanKind/TCreature 同值）"
                     + "是 0x767F10，全身只有 `push ebp / mov ebp,esp / call 0x76C804 / pop ebp / ret 8` "
                     + "五条，是纯 thunk —— 所以 sub_76C804 的返回值就是 GetAttackPower 的返回值。"
                     + "桩体覆盖 0x76C88B 的 `8B C6 5F 5E 5B`(mov eax,esi + pop edi/esi/ebx)，在 +0x1CA "
                     + "原样重放这五字节后 jmp 0x76C890(`5D C3` pop ebp/ret)。"
                     + "464 字节桩体由 .rdata 0x102CE160 处 461 个 dword 模板拼装（0x100D3B9A "
                     + "`mov ecx,0x1CD` + 0x100D3BB0 `rep movsd`；模板纯数据，唯一 >0xFF 的 dword 是"
                     + "第 289 项 0x403B4C，即 Random 的 rel32 目标）。标签串在 0x100D3BF3 起现搭："
                     + "refcnt=-1 / len=6 / 0x6F616240 '@bao' + 0x696A 'ji'。"
                     + "桩体语义：①+0x000 `cmp ebx,0x400000` 与 +0x00C `cmp [ebx],0x6AC8C8` 只对 TPlayer "
                     + "攻击者生效（英雄走 @Herobaoji）；②读 [ebx+0x804] —— 该字段落在 TPlayer 实例内"
                     + "（vmtInstanceSize 0x1948=6472），是**原生** S 变量银行 TScriptTagArr（RTTI "
                     + "[0x78D908]→0x78D90C，elSize 8，{Key,Value} 升序，Key=group*1000+index 由 "
                     + "sub_6E42CC `imul eax,edx,0x3E8 / add eax,ecx` 合成），故裸偏移 0x1F8/0x210/0x218 "
                     + "= 槽 63/66/67 = S(1,64)/S(1,67)/S(1,68)，与桩体内嵌的期望键 0x428/0x42B/0x42C "
                     + "(1064/1067/1068) 逐一相符；③S(1,64)>0 时按百分比缩放 esi（三分支防溢出 + "
                     + "INT_MAX 上钳）；④S(1,67)>0 且 S(1,68)>0 且 Random(100)<=S(1,68) 时先 "
                     + "`call [ebx+0x44]` 发 @baoji，再按 S(1,67) 缩放；⑤改写后的 esi 经重放的 "
                     + "`mov eax,esi` 成为返回值。三个索引由服务端脚本经标准 SetS 写入"
                     + "（眼神 SetS 包装器 0x10065F40 的三个调用点只写 S(1,110)/S(6,1)/S(6,2)，"
                     + "不碰 64/67/68；插件在 0x100CE4EA 起把 S(1,1..150) 播种建键）。",
            },
            new()
            {
                ConfigKey = "英雄倍攻和暴击", ScriptLabel = "@Herobaoji",
                Builder = 0x10032CC0, BuilderSites = new uint[] { 0x100D49B4 },
                HostTargets = new uint[] { 0x0076C816 }, HostResumes = new uint[] { 0x0076C81D },
                DispatchSlot = Slot.Plain, ParamCount = 0, Action = HostAction.Notify, Wired = true,
                Note = "桩体 = .rdata 0x102C8DA0 x354 模板（0x100D4985 `mov ecx,0x162` + 0x100D49A0 "
                     + "`rep movsd`）→ 361 字节；标签串在 0x100D49E3 起现搭（refcnt=-1 / len=0xA / "
                     + "'@Her'+'obao'+'ji'）。尾部 +0x15D 原样重放被覆盖的 7 字节 "
                     + "`83 BB 84 00 00 00 00 cmp [ebx+0x84],0` 并 jmp 0x76C81D → 归类 Notify，"
                     + "**但它在重放前改写了 edi**，所以与 @baoji 一样是掷点的倍率修改器，不是纯通知。"
                     + "与 @baoji 的分工：@baoji 挂 0x76C88B 改 **esi=返回值**（只认 TPlayer）；"
                     + "@Herobaoji 挂 0x76C816 改 **edi=nBasePower**（掷点之前），且类门是三个具体英雄类 "
                     + "`cmp [ebx],0x685CA0/0x685968/0x685FD8` = TTaosHero/TWarHero/TMagHero"
                     + "（THeroAct 0x685630 的全部直接子类，逐个从 VMT parent 槽枚举得出），"
                     + "并显式排除 TPlayer(0x6AC8C8) 与 TWhiteSkeleton(0x660E80)。"
                     + "取值链：主人 = [hero+0x68C]（须 >=0x410000 且为 TPlayer）→ 银行 [主人+0x804] → "
                     + "**先验槽 48：key [bank+0x180]==0x419 且 value [bank+0x184]==0x522**"
                     + "（S(1,49)==1314，就是插件 0x100CE4EA 播种留下的印记），随后按裸偏移直读 "
                     + "0x15C/0x164/0x16C = 槽 43/44/45 的 value = S(1,44)/S(1,45)/S(1,46)，**不再逐个校验 key**。"
                     + "语义：S(1,44)>0 → nBasePower 按百分比缩放；S(1,45)>0 且 S(1,46)>0 且 "
                     + "Random(100)<=S(1,46) → 发 @Herobaoji 再按 S(1,45) 二次缩放。"
                     + "两次缩放**不同形**，必须分别复刻：第一次 +0x0BC..+0x0DD 的溢出保护是**死代码**"
                     + "（+0x0C1 `E9 0B 00 00 00 jmp +0x0D1` 直接跳过了 +0x0C6 的 `jo` 与 +0x0CC 的 "
                     + "`mov eax,0x7FFFFFFF`）；第二次 +0x111..+0x130 的 `jo` 可达，且饱和值 0x7FFFFFFF "
                     + "**仍要过那次 div 100**（落 21474836），与 @baoji 的 ScaleByPercentNative 直接返回 "
                     + "int.MaxValue 不同；两次都没有 @baoji 的 `cmp …,0x3E8` 预压缩门。"
                     + "C# 落点：TBaseObject.GetAttackPower —— nPower 钳零之后（0x76C810..0x76C814）、"
                     + "幸运掷点之前，作用于 nBasePower。",
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

        /// <summary>
        /// 死亡触发（<c>@OnDie</c>，宿主 TPlayer.Die 的 epilogue 0x6C09B5）。
        /// <para>纯通知。原生桩体重放被覆盖的 <c>pop edi/esi/ebx/ecx/ecx</c> 后 jmp 0x6C09BA，
        /// 即在玩家 Die 的唯一 epilogue（所有 SEH finally 汇合之后）无条件发一次。
        /// This_Player = [ebp-4] = 死者自己。C# 落点：<c>TPlayObject.Die</c> override 末尾。</para>
        /// </summary>
        public static void FireOnDie(TPlayObject player)
        {
            if (!Armed || player == null) return;
            if (!Enabled("死亡触发")) return;
            DispatchPlain(player, "@OnDie");
        }

        /// <summary>
        /// 回城按钮触发（<c>@OnBackButton</c>，宿主分发器臂 0x6DBB80）。
        /// <para><b>顶掉型</b>：37 字节桩体不重放被覆盖的 <c>E8 E7 D6 01 00 call 0x6F926C</c>，
        /// 续跑点 0x6DBB85 就是分发器的默认 <c>jmp 0x6DBC2C</c>。返回 true 表示原生回城被顶掉，
        /// 调用方必须立刻返回。</para>
        /// </summary>
        public static bool FireOnBackButton(TPlayObject player)
        {
            if (!Armed || player == null) return false;
            if (!Enabled("回城按钮触发")) return false;
            // 桩体 +0x001 `8B D0 mov edx,eax`：This_Player = 分发器 [ebp-4] = self。
            DispatchPlain(player, "@OnBackButton");
            return true;
        }

        /// <summary>
        /// 挖矿触发（<c>@OnDig</c>，宿主 ClientHitXY sub_6EC078 的挖矿臂 0x6EC111）。
        /// <para>纯通知。原生在「重击 CM_HEAVYHIT + boMINE 图 + 手持 shape-19 镐」确认后、
        /// 耐久门(<c>cmp word[weapon+0x26],0</c>)之前发射。前四道门（ident/boMINE/武器/Shape）
        /// 属宿主上下文，由调用点判定；这里只做惰性门 + 开关 + 派发。This_Player = ebx = self。</para>
        /// </summary>
        public static void FireOnDig(TPlayObject player)
        {
            if (!Armed || player == null) return;
            if (!Enabled("挖矿触发")) return;
            DispatchPlain(player, "@OnDig");
        }

        /// <summary>
        /// 盘古穿戴触发（<c>@ChangeEquip</c>，宿主分发器两条臂 0x6D8E35 / 0x6D8E4D）。
        /// <para>纯通知。原生在 <c>ClientTakeOnItems</c>(0x6B7E9C) 与 <c>ClientTakeOffItems</c>(0x6B8188)
        /// 执行完毕后无条件各发一次（不论穿/脱成功与否），随后回到分发器默认标签。两个原生
        /// 处理器各只有一个调用者，故 C# 在 CM_TAKEONITEM / CM_TAKEOFFITEM 两个 case 调用之后
        /// 各接一次。This_Player = [ebp-4] = self。</para>
        /// </summary>
        public static void FireChangeEquip(TPlayObject player)
        {
            if (!Armed || player == null) return;
            if (!Enabled("盘古穿戴触发")) return;
            DispatchPlain(player, "@ChangeEquip");
        }

        /// <summary>
        /// 新倍攻和暴击（<c>@baoji</c>，宿主 sub_76C804 的返回点 0x76C88B）。
        /// <para>桩体重放被覆盖的 <c>mov eax,esi</c>，但在此之前会改写 <c>esi</c>，
        /// 所以这条不是纯通知：它同时是幸运掷点结果的倍率修改器。返回值即改写后的
        /// 攻击力，未命中任何一道门时原样返回入参。</para>
        /// <para>三个取值来自原生 S 变量银行 <c>[player+0x804]</c>：桩体按裸偏移
        /// 0x1F8/0x210/0x218 直读并校验键 0x428/0x42B/0x42C，等价于键查
        /// S(1,64)/S(1,67)/S(1,68)（键 = group*1000+index，sub_6E42CC）。</para>
        /// </summary>
        public static int FireBaoji(TPlayObject player, int power)
        {
            // +0x000 `cmp ebx,0x400000` / +0x00C `cmp [ebx],0x6AC8C8`。
            if (!Armed || player == null) return power;
            if (!Enabled("新倍攻和暴击")) return power;

            // +0x02D `mov edi,[bank+0x1F8]` / +0x033 `cmp edi,0x428` / jne 全放弃：
            // 键对不上（含银行为空，+0x021 `cmp edx,0x400000` / jb）连暴击都不判。
            if (!player.TryGetScriptVar('S', 1, 64, out var powerRate)) return power;
            // +0x045 `cmp edx,0 / jle +0x0C7`：值非正只跳过倍攻，仍继续暴击判定。
            if (powerRate > 0) power = ScaleByPercentNative(power, powerRate);

            // +0x0D8..+0x114：暴击的两把键各自要求 tag 命中且值 > 0。
            if (!player.TryGetScriptVar('S', 1, 67, out var critRate) || critRate <= 0) return power;
            if (!player.TryGetScriptVar('S', 1, 68, out var critChance) || critChance <= 0) return power;

            // +0x11A `mov eax,0x64` / +0x120 `call 0x403B4C` / +0x126 `cmp eax,ecx` / jg。
            // Random(100) 取 [0,99]，比较是 <=，所以概率 100 恒中、概率 1 实为 2%。
            if (M2Share.RandomNumber.Random(100) > critChance) return power;

            // +0x12E pushal / +0x12F `mov edx,ebx`(This_Player) / +0x14A `call [ebx+0x44]`。
            DispatchPlain(player, "@baoji");
            // +0x14E `mov eax,esi`，edx 跨 pushal/popal 仍是 critRate。
            return ScaleByPercentNative(power, critRate);
        }

        /// <summary>
        /// 桩体里出现两次（+0x04E 与 +0x14E）的同一段百分比缩放。先判被乘数再判
        /// 百分比，两道 <c>cmp …,0x3E8</c> 门都是为了让 32 位 <c>imul</c> 不溢出；
        /// 命中任一门就先做除法，精度按原生截断。
        /// </summary>
        private static int ScaleByPercentNative(int value, int percent)
        {
            // +0x0BD `mov esi,0x7FFFFFFF` —— 三条臂的 `jo` 共用的上钳出口。
            const int saturated = int.MaxValue;

            // +0x050 `cmp eax,0x3E8 / jg +0x0A1`：先把被乘数压缩，乘完不再除。
            if (value > 1000)
                return TryMul(DivHundredNative(value), percent, out var byValue) ? byValue : saturated;

            // +0x05B `cmp edx,0x3E8 / jg +0x081`：改压缩百分比，同样乘完不再除。
            if (percent > 1000)
                return TryMul(value, DivHundredNative(percent), out var byPercent) ? byPercent : saturated;

            // +0x067 `imul eax,edx` / +0x06A `jo` —— 溢出时跳过除法直接上钳。
            return TryMul(value, percent, out var product) ? DivHundredNative(product) : saturated;
        }

        /// <summary>
        /// 2 操作数 <c>imul r32,r32</c> 配 <c>jo</c>：乘积放不下 32 位有符号就置 OF。
        /// </summary>
        private static bool TryMul(int a, int b, out int result)
        {
            var wide = (long)a * b;
            result = unchecked((int)wide);
            return wide == result;
        }

        /// <summary>
        /// <c>99 cdq</c> + <c>F7 F3 div ebx</c>（ebx=100）。modrm 的 reg 域是 6，
        /// 是**无符号** DIV 而非 IDIV：被除数非负时 cdq 写出的 edx 为 0，等价于
        /// value/100；为负时 edx:eax 撑爆 32 位商，原生在这里是 #DE。可达输入里
        /// 被除数恒非负 —— sub_76C804 入口 0x76C812 把 nPower 钳到 &gt;=0，
        /// nBasePower 来自能力值字，且乘积已过 <c>jo</c>。
        /// </summary>
        private static int DivHundredNative(int value) => unchecked((int)((uint)value / 100u));
    }
}
