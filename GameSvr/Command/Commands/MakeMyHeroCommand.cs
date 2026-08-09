using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    // @MakeMyHero 怪物名  (HeroPet family, dispatch idx 147, perm 4, case@0x0062557D). Reversed 1:1
    // from M2Server_unpacked_fixed.exe (staging update_clothes_4637_ida_work/wf2_out.txt, 2026-08-02):
    //   case 0x0062557D:
    //     X = player[+0x12C], Y = player[+0x130]
    //     sub_766298(player, &X, &Y)                       ; clamp (X,Y) into the player's map bounds
    //     hero = sub_604E3C(off_7D6724, monsterName, envir=player[+0x128], ownerId=0, Y, X)
    //       - normalize name; template = mgr[+0x1C].findByName(name); null -> return null
    //       - find a free cell near (X,Y) (<=0x1F tries, sub_777EF8 cell probe); none -> null
    //       - classByte = template[+0x10]; switch(0..7) -> one of 8 hero-class factories
    //         (sub_60B6EC/sub_60C1DC/.../sub_609038) -> new actor; init from template (sub_60B154),
    //         set env/x/y, optional owner-bind, place in map (VMT+0x78), register into mgr[+0x24]
    //       - returns the new hero actor, or null on any failure
    //     hero == null                 -> SILENT no-op (jz def_62B64C)
    //     hero != null                 -> sub_60A538(hero, player): hero[+0x5F0] = player (bind the
    //                                     卧龙 hero to the GM) + SysMsg(0xFCFF, dword_62BFC8 success)
    //
    // DEFERRED (fail-closed): faithful wiring requires the hero-from-monster ACTOR FACTORY
    // (8-class create + template init + map placement + the [+0x5F0] owner-bind), which lives in the
    // actor / UserEngine layer — outside this command file's scope. It is NOT wired here because any
    // partial/approximate spawn (e.g. UserEngine.RegenMonsterByName) would add a mis-bound world
    // entity (not conservation-safe) and diverge from the native 卧龙-bound hero. The contract above
    // is complete for a future actor-layer port.
    [GameCommand("MakeMyHero", "创建英雄", "英雄名称 职业", 4)]
    public class MakeMyHeroCommand : BaseCommond
    {
        [DefaultCommand]
        public void MakeMyHero(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHeroName = @Params.Length > 0 ? @Params[0] : "";
            if (string.IsNullOrEmpty(sHeroName))
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            // Core reversed (sub_604E3C); actor-factory port deferred to the actor/UserEngine layer.
            NativeCommandFailure.Report(PlayObject, "MakeMyHero",
                "原版卧龙英雄由怪物模板工厂创建(sub_604E3C)，需在角色/UserEngine层移植，命令层暂不接线。");
        }
    }
}
