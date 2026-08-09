using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    
    
    
    [GameCommand("LoadAdmin", "重新加载管理员列表", 5)]
    public class LoadAdminCommand : BaseCommond
    {
        [DefaultCommand]
        public void LoadAdmin(TPlayObject PlayObject)
        {
            // 诚实 fail-closed + ancestor-only：@LoadAdmin 是 GameOfMir/LyoMir2 祖传命令，战神无对应。
            // ✅ 这条"战神无对应"是战神证据支撑的,不是 ref 推断 —— 判据是【全表枚举】而非 xref 缺失:
            //   战神 GM 注册表 340 行已完整 dump(staging/ida_award_case584_command_registry_20260720.txt),
            //   其中【没有】LoadAdmin 行;唯一的管理员重载是
            //   index=271 ea=0x007C7734 id=206 perm=5 command='ReLoadGmFile'(同文件行 352),
            //   已由 @ReLoadAdmin 承接。二次确认: ida_adjudicate8_20260803.txt:2141 `LoadAdmin : ABSENT`。
            //   (对比反例: 单纯"0 xref"不能证明缺席 —— Delphi 内联 record 字段天生 0 xref;
            //    本结论靠的是注册表全量枚举,属于有效缺席证明。)
            // 此前实现仅 perm 检查后打印"管理员列表重新加载成功…"却把真正的 LoadAdminList() 调用注释掉了
            // ——谎报成功的假命令。真正的重载请用 @ReLoadAdmin。按 lie→honest 政策改为如实上报，
            // 也不新增第二个可用重载(越 1:1)。
            // 分类备注: staging/verification_ledger_20260803.md 建议把本文件归入 silentAbsentCommandFiles
            // (class-1 ABSENT,战神对未知命令只记日志、不回红字),即当前红字回包本身仍是一处 over-send。
            NativeCommandFailure.Report(PlayObject, "LoadAdmin",
                "祖传命令，战神无对应；真正的管理员列表重载请用 @ReLoadAdmin(=原版 ReLoadGmFile)。");
        }
    }
}