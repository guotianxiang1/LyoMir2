using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public class ServerBase
    {



        private int _runTimeTick = 0;
        private int _phase4Tick = 0;
        private int _phase4CycleCount = 0;
        private const int _phase4IntervalMs = 10000;   // Phase4 runs every 10s
        private const int _statsDumpInterval = 60;      // Dump timing stats every 60 Phase4 cycles (~10min)
        private const long _globalSaveIntervalMs = 600000;
        private long _globalSaveTick;

        protected ServerBase()
        {
            _runTimeTick = HUtil32.GetTickCount();
            _phase4Tick = HUtil32.GetTickCount();
            _globalSaveTick = Environment.TickCount64;
        }

        public void StartService()
        {
            M2Share.DataServer.Start();
            YbDbClient.Instance.Start();
            M2Share.UserEngine.Start();
            M2Share.g_dwUsrRotCountTick = HUtil32.GetTickCount();
            _runTimeTick = HUtil32.GetTickCount();
        }

        public void Stop()
        {
            M2Share.GateManager?.Stop();
            M2Share.UserEngine?.Stop();
            HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
            try
            {
                M2Share.SignActManager = null;
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);
            }

            var players = M2Share.UserEngine?.PlayObjects.ToArray() ?? Array.Empty<TPlayObject>();
            foreach (var player in players)
            {
                if (player == null) continue;
                M2Share.UserEngine.SaveHumanRcd(player, 3);
            }
            foreach (var player in players)
            {
                if (player?.m_HeroObject != null
                    && !HeroDataService.QueueSave(player.m_HeroObject, 1))
                {
                    M2Share.ErrorMessage(
                        $"停止服务时英雄存档快照失败: {player.m_HeroObject.m_sCharName}");
                }
            }
            M2Share.CorpsService?.ShutdownAndDrainGildPersistence();
            NativeGloryLogManager.Flush();
            YbDbClient.Instance.FlushPendingSendsSynchronously();
            YbDbClient.Instance.Stop();

            var heroSaved = HeroDataService.FlushPendingSavesAndWait(30_000);
            var humanDeadline = Environment.TickCount64 + 30_000;
            while (M2Share.FrontEngine?.IsIdle() == false
                   && Environment.TickCount64 < humanDeadline)
                Thread.Sleep(25);
            if (M2Share.FrontEngine?.IsIdle() == false)
                M2Share.ErrorMessage(
                    $"停止服务时持久化队列未排空: 人物 {M2Share.FrontEngine.SaveListCount()} 条，" +
                    $"离线金币 {M2Share.FrontEngine.GoldChangeListCount()} 条");
            if (!heroSaved)
                M2Share.ErrorMessage("停止服务时英雄存档队列未全部收到DB确认。");
            if (M2Share.DataServer?.FlushPendingSendsAndWait(30_000) == false)
                M2Share.ErrorMessage(
                    $"停止服务时原生DB发送队列未排空: {M2Share.DataServer.PendingNativeSendCount} 条");

            try
            {
                M2Share.GlobalConf?.SaveConfig();
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"停止服务时保存全局变量失败: {ex.Message}");
            }
            M2Share.FrontEngine?.Stop();
            M2Share.DataServer?.Stop();
        }

        public void Run()
        {
            M2Share.DataServer?.Pulse();
            M2Share.GateManager.Run();
            var currentTick = HUtil32.GetTickCount();
            var processSignActEveryday = unchecked((uint)(currentTick - _runTimeTick)) >= 1000U;
            if (processSignActEveryday)
                _runTimeTick = currentTick;
            HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
            try
            {
                M2Share.PasEngine?.ProcessDeferredCalls();  // Process CallOut/CallOutEx timer queue
                M2Share.PasEngine?.ProcessAutoScripts();
                if (processSignActEveryday)
                    M2Share.SignActManager?.ProcessEveryday(DateTime.Now);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);
            }
            Mall.MallManager.Instance.ProcessScheduledRefresh(DateTime.Now);
            NativeMailCacheService.ProcessScheduledSweep(DateTime.Now);
            IdSrvClient.Instance.Run();
            M2Share.UserEngine.Run();
            M2Share.DynamicRoomManager?.Run();
            M2Share.EventManager.Run();
            NativeGloryLogManager.Run(HUtil32.GetTickCount());
            ProcessPhase4_SlowerExecute();
            if (M2Share.nServerIndex == 0)
            {
                SnapsmService.Instance.Run();
            }
            else
            {
                SnapsmClient.Instance.Run();
            }
        }

        private void ProcessGameNotice()
        {
            if (M2Share.g_Config.boSendOnlineCount && (HUtil32.GetTickCount() - M2Share.g_dwSendOnlineTick) > M2Share.g_Config.dwSendOnlineTime)
            {
                M2Share.g_dwSendOnlineTick = HUtil32.GetTickCount();
                var sMsg = string.Format(M2Share.g_sSendOnlineCountMsg, HUtil32.Round(M2Share.UserEngine.OnlinePlayObject * (M2Share.g_Config.nSendOnlineCountRate / 10.0)));
                M2Share.UserEngine.SendBroadCastMsg(sMsg, MsgType.System);
            }
        }

        public bool SaveItemNumber()
        {
            ProcessGameNotice();
            var success = true;
            try
            {
                M2Share.ServerConf.SaveItemNumbers();
            }
            catch (Exception ex)
            {
                success = false;
                M2Share.ErrorMessage($"保存物品编号失败: {ex.Message}");
            }

            var now = Environment.TickCount64;
            if (now - _globalSaveTick >= _globalSaveIntervalMs)
            {
                try
                {
                    M2Share.GlobalConf?.SaveConfig();
                    _globalSaveTick = now;
                }
                catch (Exception ex)
                {
                    success = false;
                    M2Share.ErrorMessage($"定时保存全局变量失败: {ex.Message}");
                }
            }
            return success;
        }

        /// <summary>
        /// Phase4 slower periodic tasks — consolidated operations that don't need
        /// to run every tick but should execute at 10s intervals within the main
        /// service loop.
        /// Consolidates: CastleManager.Save(), Guild war checks, DenySay cleanup,
        /// statistics logging, and ObjectManager.ClearObject().
        /// </summary>
        private void ProcessPhase4_SlowerExecute()
        {
            if ((HUtil32.GetTickCount() - _phase4Tick) <= _phase4IntervalMs)
                return;

            _phase4Tick = HUtil32.GetTickCount();
            _phase4CycleCount++;

            HUtil32.EnterCriticalSections(M2Share.ProcessHumanCriticalSection);
            try
            {
                // 1. Guild war expiration checks
                M2Share.GuildManager.Run();

                // 1b. Native (MySQL) guild war expiration (GILD-27)
                M2Share.CorpsService.ExpireGildWars(M2Share.g_Config.dwGuildWarTime);

                // 2. Castle manager — includes war state, tax collection, door repair
                M2Share.CastleManager.Run();
                if (!M2Share.CastleManager.Save())
                {
                    M2Share.ErrorMessage("本轮城堡配置保存未完成，将在下个周期重试。");
                }

                // 3. DenySay timeout cleanup — clear expired chat bans
                var denyList = new List<string>(M2Share.g_DenySayMsgList.Count);
                foreach (var item in M2Share.g_DenySayMsgList)
                {
                    if (HUtil32.GetTickCount() > item.Value)
                        denyList.Add(item.Key);
                }
                for (var i = 0; i < denyList.Count; i++)
                {
                    if (M2Share.g_DenySayMsgList.TryRemove(denyList[i], out var _))
                    {
                        M2Share.MainOutMessage($"解除玩家[{denyList[i]}]禁言");
                    }
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSections(M2Share.ProcessHumanCriticalSection);
            }

            // 4. 已删除 ObjectManager.ClearObject() 调用（2026-08-03）：那是非原生的
            //    全局 ghost 扫描（3 分钟 dwMakeGhostTime）。战神没有 id→对象的全局扫描——
            //    检测在各逐类型 tick 循环（ProcessMon sub_67C150 循环2/3），释放集中在
            //    单一延迟 5 分钟的 FIFO（0x67C1BD `cmp eax,0x493E0`）。逐类型 reap 已在
            //    UsrEngn.ProcessMonsters/ProcessNpcs/ProcessMerchants 补齐并走
            //    ObjectManager.Remove(id, expected) 以保住 PAS 脚本状态清理。详见 ObjectManager.cs。

            // 5. Reset timing windows every N Phase4 cycles.
            if (_phase4CycleCount % _statsDumpInterval == 0)
            {
                M2Share.g_nHumCountMax = 0;
                M2Share.dwUsrRotCountMax = 0;
            }
        }
    }
}
