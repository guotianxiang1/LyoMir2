using System;
using System.Collections.Generic;
using System.IO;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public partial class NormNpc
    {
        private readonly object m_NativeMagicTowerChallengeSync = new();
        private NativeMagicTowerChallengeState m_NativeMagicTowerChallenge;
        private bool m_boNativeMagicTowerChallengeActive;

        internal bool NativeMagicTowerChallengeActive
        {
            get
            {
                lock (m_NativeMagicTowerChallengeSync)
                    return m_boNativeMagicTowerChallengeActive;
            }
        }

        internal NativeMagicTowerChallengeState NativeMagicTowerChallenge
        {
            get
            {
                lock (m_NativeMagicTowerChallengeSync)
                    return m_NativeMagicTowerChallenge;
            }
        }

        internal void StartNativeMagicTowerChallenge(TPlayObject player)
        {
            if (player == null || !HasNativePasProperty(12)) return;

            var catalog = NativeMagicTowerMonsterCatalog.Capture();
            if (!catalog.IsAvailable) return;

            player.m_btNativeMagicTowerPhase = 3;
            var sequence = new byte[50];
            lock (m_NativeMagicTowerChallengeSync)
            {
                m_NativeMagicTowerChallenge =
                    new NativeMagicTowerChallengeState(player,
                        player.m_PEnvir, HUtil32.GetTickCount(), sequence, 0,
                        catalog.Names, catalog.Split);

                for (var index = 0; index < sequence.Length; index++)
                {
                    if (index <= 9)
                    {
                        sequence[index] = (byte)index;
                    }
                    else if (index <= 34)
                    {
                        sequence[index] = unchecked((byte)(
                            NextNativeMagicTowerRandom(catalog.FirstBound) +
                            10));
                    }
                    else
                    {
                        sequence[index] = unchecked((byte)(
                            NextNativeMagicTowerRandom(catalog.SecondBound) +
                            catalog.Split + 1));
                    }
                }

                m_boNativeMagicTowerChallengeActive = true;
            }
        }

        internal void ProcessNativeMagicTowerChallenge()
        {
            if (!HasNativePasProperty(12)) return;

            var now = HUtil32.GetTickCount();
            lock (m_NativeMagicTowerChallengeSync)
            {
                var state = m_NativeMagicTowerChallenge;
                if (!m_boNativeMagicTowerChallengeActive || state == null ||
                    state.Player == null)
                    return;
                if (unchecked((uint)(now - state.Tick)) < 2000u ||
                    state.Player.m_boGhost ||
                    !ReferenceEquals(state.Player.m_PEnvir,
                        state.Environment) || state.Position > 49)
                    return;

                try
                {
                    var monsterName = state.Monsters[
                        state.Sequence[state.Position]];
                    var monster = M2Share.UserEngine?
                        .RegenNativeMagicTowerChallengeMonster(
                            state.Environment, monsterName, 29, 19);
                    if (monster != null && state.Position <= 34)
                        monster.m_boMission = true;

                    state.Position++;
                    m_boNativeMagicTowerChallengeActive =
                        state.Position != 50;
                }
                catch (Exception)
                {
                    try
                    {
                        M2Share.ErrorMessage("[SupplyMon Err]:" +
                                             state.Position);
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    state.Tick = now;
                }
            }
        }

        private static int NextNativeMagicTowerRandom(int range)
        {
            return range > 0 ? M2Share.RandomNumber.Random(range) : 0;
        }

        internal static void InitializeNativeMagicTowerMonsterCatalog(
            string rootPath, string baseDir)
        {
            NativeMagicTowerMonsterCatalog.Initialize(rootPath, baseDir);
        }

        internal sealed class NativeMagicTowerChallengeState
        {
            internal NativeMagicTowerChallengeState(TPlayObject player,
                Envirnoment environment, int tick, byte[] sequence,
                int position, IReadOnlyList<string> monsters, int split)
            {
                Player = player;
                Environment = environment;
                Tick = tick;
                Sequence = sequence;
                Position = position;
                Monsters = monsters;
                Split = split;
            }

            internal TPlayObject Player { get; }
            internal Envirnoment Environment { get; }
            internal int Tick { get; set; }
            internal byte[] Sequence { get; }
            internal int Position { get; set; }
            internal IReadOnlyList<string> Monsters { get; }
            internal int Split { get; }
        }

        private sealed class NativeMagicTowerMonsterCatalog
        {
            private static readonly object SyncRoot = new();
            private static NativeMagicTowerMonsterCatalog s_Catalog;

            private NativeMagicTowerMonsterCatalog(string[] names, int split,
                bool isAvailable = true)
            {
                Names = names;
                Split = split;
                FirstBound = split - 10;
                SecondBound = names.Length - split - 1;
                IsAvailable = isAvailable;
            }

            internal string[] Names { get; }
            internal int Split { get; }
            internal int FirstBound { get; }
            internal int SecondBound { get; }
            internal bool IsAvailable { get; }

            internal static void Initialize(string rootPath, string baseDir)
            {
                lock (SyncRoot)
                    s_Catalog = LoadSnapshot(rootPath, baseDir);
            }

            internal static NativeMagicTowerMonsterCatalog Capture()
            {
                lock (SyncRoot)
                {
                    s_Catalog ??= LoadSnapshot(M2Share.sRootPath,
                        M2Share.g_Config.sBaseDir);
                    return s_Catalog;
                }
            }

            private static NativeMagicTowerMonsterCatalog LoadSnapshot(
                string rootPath, string baseDir)
            {
                try
                {
                    var path = Path.GetFullPath(Path.Combine(rootPath,
                        baseDir, "Config", "TianMon.ini"));
                    return Load(path);
                }
                catch (Exception e)
                {
                    LogLoadException(e);
                    return new NativeMagicTowerMonsterCatalog(
                        Array.Empty<string>(), 0, false);
                }
            }

            private static NativeMagicTowerMonsterCatalog Load(string path)
            {
                if (!File.Exists(path))
                {
                    M2Share.ErrorMessage(
                        "[Error]: 服务器错误：新天关怪物文件错误");
                    return new NativeMagicTowerMonsterCatalog(
                        Array.Empty<string>(), 0);
                }

                try
                {
                    var ini = new NativeMagicTowerMonsterIni(path);
                    var names = new List<string>(150);
                    AddSection(ini, names, "低级怪物");
                    AddSection(ini, names, "中级怪物");
                    var split = names.Count;
                    AddSection(ini, names, "高级怪物");
                    if (names.Count == 0)
                    {
                        M2Share.ErrorMessage(
                            "[Error]: 服务器错误：新天关怪物文件错误");
                        return new NativeMagicTowerMonsterCatalog(
                            Array.Empty<string>(), 0);
                    }
                    return new NativeMagicTowerMonsterCatalog(
                        names.ToArray(), split);
                }
                catch (Exception e)
                {
                    LogLoadException(e);
                    M2Share.ErrorMessage(
                        "[Error]: 服务器错误：新天关怪物文件错误");
                    return new NativeMagicTowerMonsterCatalog(
                        Array.Empty<string>(), 0, false);
                }
            }

            private static void LogLoadException(Exception exception)
            {
                try
                {
                    M2Share.ErrorMessage(
                        "[Exception] TSkyQuest.LoadTianMon: " +
                        exception.Message);
                }
                catch
                {
                }
            }

            private static void AddSection(NativeMagicTowerMonsterIni ini,
                List<string> names, string section)
            {
                for (var number = 1; number <= 50; number++)
                {
                    var name = ini.ReadMonster(section, number);
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
            }
        }

        private sealed class NativeMagicTowerMonsterIni : IniFile
        {
            internal NativeMagicTowerMonsterIni(string path) : base(path)
            {
                Load();
            }

            internal string ReadMonster(string section, int number)
            {
                return ReadString(section, "怪物" + number, string.Empty);
            }
        }
    }
}
