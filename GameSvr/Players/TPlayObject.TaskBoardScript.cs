using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 任务发布板 @Main 脚本接线 —— CM 4417（脚本命令）/ CM 4651（文本命令）的**忠实实现**，
    /// 把 <c>TPlayObject.TaskBoard.cs</c>（taskboard 代理）与 cm-4 Tail 分片此前对这两条 ident 的
    /// fail-closed 处置升级为真正调用 HelperQuest.pas @Main 脚本对象。
    ///
    /// 全部偏移均取自 flat_image.bin（ImageBase 0x400000, capstone x86-32），反汇编原件与逐 worker
    /// 注解见 <c>tools/taskboardscript_re.py</c>（<c>python tools\taskboardscript_re.py all</c>）。
    ///
    /// ── 板对象 ──────────────────────────────────────────────────────────────────
    /// 任务板是原生单例 <c>[[0x7D5D20]]</c>（VMT 0x72868C）。其 <c>+0x2C</c> 槽是一个由
    /// <c>sub_699FE0</c> 从 "&lt;envir&gt;\PsMapQuest\HelperQuest.pas" 装载的 TSTDScript —— 这就是
    /// 任务板的 @Main 脚本对象。CM 4417/4651 驱动的都是这个对象；本服没有"板对象"实体，其 <c>+0x2C</c>
    /// 非空门等价于"HelperQuest.pas 可装载"，由 <c>TryCallScriptLabel</c> 找不到脚本即静默 no-op 复刻。
    ///
    /// TSTDScript.GotoLabel = vmt+0x44（sub_733D84）：
    ///   0x733DC6 SetVar This_DB = [[0x7D5C40]]
    ///   0x733DD5 SetVar This_Item = 参数2（两条命令都传 0）
    ///   0x733DE5 SetVar This_Player = 参数1（player）
    ///   0x733DF4 若 label ∈ {"@Main","@_Main"} → 0x733F6F: `call [self.vmt+0x38]`（跑脚本主体）
    ///   0x733E15 否则剥离 '@'、处理 '~' 参数，按名在 [self+0x24] 过程表里查表分发。
    /// 其 C# 等价即脚本宿主 <c>M2Share.PasEngine.TryCallScriptLabel(script, label, player)</c>：
    /// <c>ExecuteLabel</c> 把 "@buy" 规范化为过程 "_buy"（与原生 剥 '@' 一致），把 "@Main" 映射为
    /// <c>ExecuteMain</c>（与 vmt+0x38 主体腿一致），且对 @main 恒有 handler、对缺失 label 直接返回 false。
    ///
    /// ── CM 4417 · 脚本命令（三件套）────────────────────────────────────────────
    /// VA:   leaf 0x6DB1BF `eax=[[0x7D5D20]]`(board) / `ecx=0x6DC000`(str "@Main") / `edx=[ebp-4]`(player)
    ///       → worker 0x699EB4(board, player, "@Main")。
    /// 逻辑: 0x699EED `mov eax,[board+0x2C]` / 0x699EF0 `test eax,eax / je` —— +0x2C 为空则**什么都不做**；
    ///       否则 0x699EFE `mov ebx,[eax]`(vmt) / 0x699F00 `call [ebx+0x44]`，参数 = (Self=脚本对象,
    ///       edx=player, ecx=0, push 0, push "@Main") ⇒ GotoLabel(player, 0, 0, "@Main")。无任何玩家状态门。
    /// C#:   <c>TryCallScriptLabel("HelperQuest", "@Main", this)</c> —— 以 This_Player=this 跑 @Main。
    /// 缺口: 无（板对象 +0x2C 非空门由脚本可装载性复刻；This_DB=[[0x7D5C40]] 的默认对象绑定由脚本宿主统一
    ///       负责，与全服所有 label 调用同源，非本处新增语义）。
    ///
    /// ── CM 4651 · 文本命令（三件套）────────────────────────────────────────────
    /// VA:   leaf 0x6DB1D8 `[ebp-0x254]=body 串`(0x405708) → worker 0x6FC054(player, text)。
    /// 逻辑: 0x6FC064 `cmp [board+0x2C],0 / je` —— HelperQuest 未装载则**什么都不做**；否则
    ///       0x6FC08B `call 0x6B8CC4(player, board+0x2C, ecx=0, push 0, push 4059D0(text), push Len(text)+1)`。
    ///       <c>sub_6B8CC4</c> 是玩家↔脚本文本交互总线；**顶部四道门对所有分支生效**：
    ///         0x6B8CF3 `cmp [player+0x73],0 / jne 退出`               → m_boGhost
    ///         0x6B8CFF `call 0x772DA8` = `[player+0x74]` / `jne 退出`  → m_boDeath（sub_772DA8 只 `mov al,[eax+0x74]`）
    ///         0x6B8D0C `cmp [player+0x461],0 / jne 退出`              → m_boDealing（姊妹 sub_6B8B28 同址同名，见 ClientClickNPC）
    ///         0x6B8D19 `test esi,esi / je 退出` + 0x6B8D21 `cmp [ebp+8],1 / jle 退出` → text 非空、Length(text) ≥ 1
    ///       随后按 edi(=board+0x2C) 分流；板腿 0x6B8E6D..0x6B8EB5 **无状态**（不碰 [player+0x18B4] 脚本宿主 /
    ///       [player+0xCD8] 会话绑定）：0x6B8E9F `Insert("_", text, 2)`（"@buy"→"@_buy"）后
    ///       0x6B8EB0 `call 0x699EB4(board, player, label)` —— **与 CM 4417 同一个 worker**，只是 label 换成客户端文本。
    ///       原生板腿把 0x4C6AEC（按 CR=0x0D 分词）的结果整体丢弃、直接用原始 text，本处照此传原文。
    /// C#:   四道门后 <c>TryCallScriptLabel("HelperQuest", text, this)</c> —— 以客户端文本为 label 跑 HelperQuest。
    ///       "@label" 经 ExecuteLabel 规范化为过程 "_label"，与原生 `Insert("_",...,2)` + vmt+0x44 剥 '@' 得到的
    ///       "_label" 逐字一致；label 缺失时 <c>HasLabelHandler</c> 先拦下 → 返回 false（no-op），不误跑 @Main。
    /// 缺口: <c>sub_6B8CC4</c> 面向**其他脚本对象**的分支（mode==1 的 [[0x7D5D9C]] 怪物脚本、[[0x7D6D80]]、
    ///       board+4、[[0x7D6784]] "月老" 等，各自经 [player+0x18B4] 脚本宿主的 vmt+0x8C 交互）不属任务板范围，
    ///       仍 fail-closed / 不建模；'~' 位置参数约定超出常规菜单点击者，沿用脚本宿主既有 label 解析（全服一致）。
    ///
    /// ── INTEGRATOR HOOKUP（防冲突：本分部不改 TPlayObject.Message.cs / 他人文件）──────────
    /// 在 <c>Operate()</c> 的 <c>default:</c> 臂里，把对 <see cref="TryHandleTaskBoardScriptCm"/> 的调用插到
    /// <c>TryHandleNativeCmTailProtocol</c> **之前**（它先于 Q1/Q2/Q3）。这样本忠实处置优先于并**取代**：
    ///   • <see cref="TryHandleTaskBoardCm"/>（TaskBoard.cs，Tail 内首个被调）对 4417/4651 的 fail-closed 腿；
    ///   • cm-4 Tail <c>TryHandleNativeCmTailProtocol</c> 里 4417/4651 的 fail-closed 腿。
    /// 本 hook 只认领 4417/4651（其余 return false），故 TaskBoard.cs 的 4150/4151 忠实腿不受影响。
    ///
    ///     default:
    ///         if (!TryHandleInlayCm(ProcessMsg)
    ///             &amp;&amp; ...
    ///             &amp;&amp; !TryHandleCmMiscTail(ProcessMsg)
    ///             &amp;&amp; !TryHandleTaskBoardScriptCm(ProcessMsg)     // ← add this, before Tail/Q1/Q2/Q3
    ///             &amp;&amp; !TryHandleNativeSocialProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
    ///         {
    ///             result = base.Operate(ProcessMsg);
    ///         }
    ///         break;
    /// </summary>
    public partial class TPlayObject
    {
        private const string TaskBoardHelperQuestScript = "HelperQuest";
        private const string TaskBoardScriptMainLabel = "@Main";

        /// <summary>
        /// 任务板 @Main 脚本派发 hook。命中 CM 4417/4651 即 return true（调用方短路，越过其后的
        /// TaskBoard.cs / cm-4 Tail 对这两条 ident 的 fail-closed 腿）。挂载点见类级 INTEGRATOR HOOKUP。
        /// </summary>
        private bool TryHandleTaskBoardScriptCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_4417:
                    NativeTaskBoardScriptCommandRun();
                    return true;
                case Grobal2.CM_4651:
                    NativeTaskBoardTextCommandRun(processMessage.sMsg);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 4417（leaf 0x6DB1BF → worker 0x699EB4）：以 This_Player=this 运行任务板 HelperQuest.pas 的
        /// "@Main" label。原生仅在板对象 +0x2C（HelperQuest 脚本对象）非空时经 vmt+0x44 GotoLabel(player,0,0,"@Main")
        /// 进入；无玩家状态门。<c>TryCallScriptLabel</c> 在脚本缺失时返回 false（静默），复刻 +0x2C 为空的忠实 no-op。
        /// </summary>
        private void NativeTaskBoardScriptCommandRun()
        {
            M2Share.PasEngine?.TryCallScriptLabel(TaskBoardHelperQuestScript, TaskBoardScriptMainLabel, this);
        }

        /// <summary>
        /// CM 4651（leaf 0x6DB1D8 → worker 0x6FC054 → sub_6B8CC4 板腿）：把客户端文本当作 label 运行在任务板
        /// HelperQuest.pas 上（板腿最终仍调 0x699EB4，与 CM 4417 同 worker）。忠实复刻 sub_6B8CC4 顶部四道门：
        /// 非 m_boGhost([+0x73]) / 非 m_boDeath([+0x74], sub_772DA8) / 非 m_boDealing([+0x461]) / 文本非空
        /// (Length ≥ 1)。板腿无状态，客户端 "@label" 由 ExecuteLabel 规范化为过程 "_label"，与原生 Insert("_",…,2)
        /// + vmt+0x44 剥 '@' 逐字一致。sub_6B8CC4 面向其他脚本对象的分支不属任务板范围，见类级"缺口"。
        /// </summary>
        private void NativeTaskBoardTextCommandRun(string text)
        {
            // sub_6B8CC4 0x6B8CF3/0x6B8CFF/0x6B8D0C：三态门（任一为真则整条静默退出）。
            if (m_boGhost || m_boDeath || m_boDealing)
            {
                return;
            }
            // sub_6B8CC4 0x6B8D19 `test esi,esi / je` + 0x6B8D21 `cmp [ebp+8],1 / jle`：text 非 nil 且 Length ≥ 1。
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            // 板腿 0x6B8E6D..0x6B8EB5：原始文本 → 0x699EB4 → GotoLabel(player,0,0,label)。
            M2Share.PasEngine?.TryCallScriptLabel(TaskBoardHelperQuestScript, text, this);
        }
    }
}
