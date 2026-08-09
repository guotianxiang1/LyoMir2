using System;

namespace DBSvr.Core
{
    public sealed class NativeForceLevelStore : INativeForceLevelStore
    {
        private readonly IPlayRecordService _players;
        private readonly IHeroRecordService _heroes;
        private readonly IHeroDataService _heroData;
        private readonly NativeHeroLogicalCache _heroLogicalCache;

        public NativeForceLevelStore(IPlayRecordService players,
            IHeroRecordService heroes, IHeroDataService heroData,
            NativeHeroLogicalCache heroLogicalCache)
        {
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _heroes = heroes ?? throw new ArgumentNullException(nameof(heroes));
            _heroData = heroData ?? throw new ArgumentNullException(nameof(heroData));
            _heroLogicalCache = heroLogicalCache
                ?? throw new ArgumentNullException(nameof(heroLogicalCache));
        }

        public NativeForceLevelStoreAttempt ApplyPlayer(byte[] characterName,
            ushort forceLevel) =>
            _players.ApplyNativeForceLevel(characterName, forceLevel);

        public NativeForceLevelStoreAttempt ApplyHero(byte[] characterName,
            ushort forceLevel)
        {
            try
            {
                if (!_heroes.TryGetNativeForceLevelIndex(characterName, out var index))
                    return new NativeForceLevelStoreAttempt(
                        NativeForceLevelStoreResult.Missing);
                if (_heroLogicalCache.TryApplyForceLevel(index, forceLevel,
                        out var snapshot, out _))
                {
                    if (snapshot == null)
                        return new NativeForceLevelStoreAttempt(
                            NativeForceLevelStoreResult.LoadFailed);
                    _heroData.SetNativeForceLevelOverride(index, forceLevel);
                    return new NativeForceLevelStoreAttempt(
                        NativeForceLevelStoreResult.Queued,
                        new NativeForceLevelMutation
                        {
                            Target = NativeForceLevelTarget.Hero,
                            Index = index,
                            ForceLevel = forceLevel,
                            CharacterNameBytes = characterName == null
                                ? Array.Empty<byte>()
                                : (byte[])characterName.Clone()
                        });
                }
                return _heroData.ApplyNativeForceLevel(
                    index, characterName, forceLevel);
            }
            catch (Exception ex)
            {
                DBShare.MainOutMessage(
                    "[NativeForceLevel] hero load failed: " + ex.Message);
                return new NativeForceLevelStoreAttempt(
                    NativeForceLevelStoreResult.LoadFailed);
            }
        }
    }
}
