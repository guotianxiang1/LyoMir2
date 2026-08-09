using GameSvr.CommandSystem;

namespace GameSvr
{
    [GameCommand("DelHero", "删除自己的英雄", "", 4)]
    public class DelHeroCommand : BaseCommond
    {
        [DefaultCommand]
        public void DelHero(string[] @Params, TPlayObject PlayObject)
        {
            HeroDataService.RequestDelete(PlayObject);
        }
    }
}
