using SystemModule;
using GameSvr.Services;

namespace GameSvr
{
    public class TimedService : BackgroundService
    {
        internal const int ItemNumberSaveIntervalMilliseconds = 900_000;

        private readonly GameApp _mirApp;
        private int _checkIntervalTime;
        private int _saveAttemptTime;

        public TimedService(GameApp mirApp)
        {
            _mirApp = mirApp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _checkIntervalTime = HUtil32.GetTickCount();
            _saveAttemptTime = HUtil32.GetTickCount();
            while (!stoppingToken.IsCancellationRequested)
            {
                ServiceTimer();
                await Task.Delay(TimeSpan.FromMilliseconds(10), stoppingToken);
            }
        }

        private void ServiceTimer()
        {
            if (!M2Share.boStartReady)
            {
                return;
            }
            YbDbClient.Instance.Pulse();
            if ((HUtil32.GetTickCount() - _checkIntervalTime) > 3000)
            {
                IdSrvClient.Instance.CheckConnected();
                SnapsmClient.Instance.CheckConnected();
                _checkIntervalTime = HUtil32.GetTickCount();
            }
            if ((HUtil32.GetTickCount() - _saveAttemptTime) >
                ItemNumberSaveIntervalMilliseconds)
            {
                if (!_mirApp.SaveItemNumber())
                {
                    M2Share.ErrorMessage("本轮定时配置保存未完成，将在下个周期重试。");
                }
                _saveAttemptTime = HUtil32.GetTickCount();
            }
        }
    }
}
