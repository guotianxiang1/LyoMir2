using GameSvr;

namespace GameSvr.Plugins
{
    /// <summary>
    /// 激光（SKILL_SHOOTLIGHTEN = 10）生产者上眼神补的「两槽」读取器 —— B3 消费者的前置。
    ///
    /// 宿主激光生产者在 M2Server 0x0076E9E0（ret 4 @ 0x0076EA30）。它先发射光束
    /// （call sub_76FE44 @ 0x0076EA0F，法术 0x27C1），命中后按 Random(3)+1 训练技能
    /// （0x0076EA14 mov eax,3 / call Random(0x403B4C) / 0x0076EA1E inc ecx /
    /// 0x0076EA27 call [ebx+0x3C] = TrainSkill 虚调用）。眼神在这条链上补了两个槽，
    /// 各由独立配置开关门控（DLL 补丁器 0x100D92xx-0x100D96xx）：
    ///
    ///  ── 槽①：光束 arg0（S(1,81)）──
    ///   宿主 0x0076EA07 `6A 01 push 1` 是 sub_76FE44 的第一个栈参（最后一个 push ->
    ///   [ebp+8]），被 sub_76FE44 以 `8A 45 08 mov al,[ebp+8]`（0x0076FEA7）取**低 8 位**。
    ///   *不是*线跨度：跨度是更早的 arg4 `6A 08 push 8`（0x0076E9FD -> [ebp+0x18] ->
    ///   `0F B7 45 18 movzx eax,word[ebp+0x18]` @ 0x0076FEBF）。
    ///   DLL 补丁点：0x100D92F6 / 0x100D9308 `push 0x76EA07`；门 0x100D92C6
    ///   `cmp [edi+0x4FC],0 / je`（配置开关 "激光范围及系数"，即 docs 的「范围」）。
    ///   语义：开且 S(1,81)>0 -> arg0 = (byte)S(1,81)；否则原生 1（push 1）。
    ///
    ///  ── 槽②：训练 Random 实参（S(1,82)）──
    ///   宿主 0x0076EA14 `B8 03 00 00 00 mov eax,3` 是 TrainSkill 前 Random 的实参
    ///   （随后 call Random / inc ecx = Random(3)+1）。
    ///   DLL 补丁点：0x100D9475 / 0x100D9488 / 0x100D964E / 0x100D9653 `push 0x76EA14`；
    ///   门 0x100D93F4 `cmp [edi+0x4F8],0 / je`（配置开关 "激光命中概率"）。
    ///   语义（AuditTools/docs 已核）：开且 S(1,82)>0 -> Random(S(1,82))+1；
    ///   **开且 S(1,82)<=0 -> Random(1)+1**（fallback 是 mov eax,1，*不是*还原
    ///   原生 mov eax,3）；关 -> 原生 Random(3)+1。本读取器返回 Random 的**实参 N**
    ///   （即 eax），由消费者执行 Random(N)+1。
    ///
    /// S 银行读取走 TPlayObject.TryGetScriptVar('S',1,index)（键=group*1000+index）；
    /// 该银行由 TPlayObject.YanshenSeedLoginSVars 在登录时灌满（0x100CE4EA）。
    /// 开关判定复用 YanshenApi.PatchToggleOn，与 YanshenSkillPatches 同一路径。
    /// </summary>
    internal static class YanshenLaserSlots
    {
        private const string LaserArg0Toggle = "激光范围及系数";
        private const string LaserTrainToggle = "激光命中概率";

        // 宿主原生常量：arg0 = push 1（0x0076EA07）；Random 实参 = 3（0x0076EA14）。
        public const int NativeBeamArg0 = 1;
        public const int NativeTrainRandom = 3;

        /// <summary>
        /// 槽①。sub_76FE44 的 arg0 低 8 位（0x0076FEA7 mov al,[ebp+8]）。
        /// 开且 S(1,81)>0 时用 (byte)S(1,81)，否则原生 1。
        /// </summary>
        public static byte BeamArg0(TPlayObject player)
        {
            if (player == null || !ToggleOn(player, LaserArg0Toggle))
                return unchecked((byte)NativeBeamArg0);
            if (!player.TryGetScriptVar('S', 1, 81, out int v) || v <= 0)
                return unchecked((byte)NativeBeamArg0);
            // 0x0076FEA7 只取低 8 位。
            return unchecked((byte)v);
        }

        /// <summary>
        /// 槽②。返回激光命中后训练技能所用 Random 的实参 N（宿主 0x0076EA14）。
        /// 消费者应执行 Random(N)+1（0x0076EA19 call Random / 0x0076EA1E inc ecx）。
        /// 开 -> S(1,82)>0 ? S(1,82) : 1（S<=0 落 Random(1)+1，*非*还原 3）；关 -> 3。
        /// </summary>
        public static int TrainRandomArg(TPlayObject player)
        {
            if (player == null || !ToggleOn(player, LaserTrainToggle))
                return NativeTrainRandom;
            if (!player.TryGetScriptVar('S', 1, 82, out int v) || v <= 0)
                return 1; // 开且 S<=0 -> mov eax,1（fallback），不还原 mov eax,3。
            return v;
        }

        // 与 YanshenSkillPatches.ToggleOn 同一实现：PluginManager 缺席则关（off = 原样）。
        private static bool ToggleOn(TPlayObject player, string key)
        {
            if (M2Share.PluginManager == null)
                return false;
            return new YanshenApi(player, null, M2Share.PluginManager)
                .PatchToggleOn(key);
        }
    }
}
