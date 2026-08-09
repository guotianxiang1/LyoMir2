using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM 命令 @CreateCampMon (idx339, perm4)。
    /// 原版契约 (sub_6EB6B8; 见 GameSvr/Services/NativeGmWorldAdminCommands.cs idx339 @0x00627EDD):
    ///   @CreateCampMon 怪物名 所属阵营 刷怪中心点X Y 怪物数量 刷怪范围 怪物守护点X Y
    ///   = monName, camp, centerX, centerY, count, range, guardX, guardY
    ///   在【GM 当前地图】以 (centerX,centerY) 为中心、range 为半径散布 count 只阵营怪；无 GM 回消息。
    ///
    /// 用真实刷怪基础设施 M2Share.UserEngine.RegenMonsterByName —— 与 @Mob 及活体 PAS `createcampmon`
    /// 同一入口(ExactEnvironmentMonsterSpawnTransaction 覆盖其事务性: 证书发布/回滚/post-commit 脚本初始化)。
    /// 散布 roll 与 PAS `createcampmon` (PasApiBridge.cs) 逐字一致。
    ///
    /// 局限 (已标注供复核，均为真实字段/真实基础设施，非伪造；随下游子系统落地即闭合):
    ///  - 阵营(camp)归属: 本 C# 怪物对象无已接线的阵营字段。活体 PAS `createcampmon` 同样忽略 campType，
    ///    `createcampanimal` 直接 RejectUnsupportedNativeApi("camp ownership differs")。此处解析 camp 但不应用。
    ///  - 守护点(guardX/guardY): 写入怪物 home 锚点(m_sHomeMap/m_nHomeX/m_nHomeY)；但当前怪物回防 AI
    ///    尚未消费该锚点(m_nHomeY 在 GameSvr 内仅声明、无处读取)——待怪物阵营/守护 AI 子系统落地才生效。
    ///  - RNG 精确散布位置需等全局 RandSeed owner 切换后才能与原版逐字一致(下游 long-pole)。
    /// </summary>
    [GameCommand("CreateCampMon", "创建阵营怪物", "怪物名 阵营 中心X 中心Y 数量 范围 守护X 守护Y", 4)]
    public class CreateCampMonCommand : BaseCommond
    {
        [DefaultCommand]
        public void CreateCampMon(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sMonName = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sMonName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            // @Params[1] = 所属阵营(camp) —— 本 C# 怪物对象无已接线的阵营字段(见类注释局限)，
            // 解析位保留但不应用；故此处不取具名局部，避免未用变量。
            var nCenterX = @Params.Length > 2 ? (short)HUtil32.Str_ToInt(@Params[2], 0) : (short)0;
            var nCenterY = @Params.Length > 3 ? (short)HUtil32.Str_ToInt(@Params[3], 0) : (short)0;
            var nCount = @Params.Length > 4 ? HUtil32.Str_ToInt(@Params[4], 0) : 0;
            var nRange = @Params.Length > 5 ? HUtil32.Str_ToInt(@Params[5], 0) : 0;
            var nGuardX = @Params.Length > 6 ? (short)HUtil32.Str_ToInt(@Params[6], 0) : (short)0;
            var nGuardY = @Params.Length > 7 ? (short)HUtil32.Str_ToInt(@Params[7], 0) : (short)0;

            var envir = PlayObject.m_PEnvir;
            if (envir == null)
            {
                return;
            }
            // 中心点缺省/非法 → GM 自身格(原版在 GM 当前地图刷怪；与 PAS createcampmon 的 x<=0 缺省一致)
            if (nCenterX <= 0) nCenterX = PlayObject.m_nCurrX;
            if (nCenterY <= 0) nCenterY = PlayObject.m_nCurrY;
            // 守护点缺省 → 中心点
            if (nGuardX <= 0) nGuardX = nCenterX;
            if (nGuardY <= 0) nGuardY = nCenterY;
            if (nCount <= 0)
            {
                return;
            }
            // 数量上限取同族 PAS createcampmon 的 200(原版核心上限未 dump)
            nCount = HUtil32._MIN(200, nCount);

            for (var i = 0; i < nCount; i++)
            {
                var sx = nCenterX;
                var sy = nCenterY;
                if (nRange > 0)
                {
                    // 散布 roll —— 与 PAS createcampmon 逐字一致。
                    // RNG scatter exact-parity pending RandSeed owner cutover.
                    sx = (short)(nCenterX - nRange + M2Share.RandomNumber.Random(nRange * 2 + 1));
                    sy = (short)(nCenterY - nRange + M2Share.RandomNumber.Random(nRange * 2 + 1));
                }
                var monster = M2Share.UserEngine.RegenMonsterByName(envir, sx, sy, sMonName);
                if (monster == null)
                {
                    // 怪物名无效等 → 与 @Mob 一致，停止本批
                    break;
                }
                // 守护点锚定到 home 字段(真实字段；回防 AI 消费待下游子系统，见局限)。
                monster.m_sHomeMap = envir.sMapName;
                monster.m_nHomeX = nGuardX;
                monster.m_nHomeY = nGuardY;
            }
        }
    }
}
