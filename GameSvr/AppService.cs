using SystemModule;

namespace GameSvr
{
    public class AppService : BackgroundService
    {
        public static AppService Instance { get; private set; }
        private readonly GameApp _mirApp;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private CancellationToken _stoppingToken;
        private readonly object _engineRunSync = new();

        public AppService(GameApp serverApp, IHostApplicationLifetime applicationLifetime)
        {
            _mirApp = serverApp;
            _applicationLifetime = applicationLifetime;
            Instance = this;
            // TMainThread.Create @0x00792BEC seeds RandSeed here, before Execute
            // @0x00792D2C enters the game loop: 0x00792C5A E8 4D 08 C7 FF call 0x004034AC.
            // AppService is the same object — ctor then ExecuteAsync -> _mirApp.Run().
            DelphiRandom.Randomize();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;
            // 等待 Form.Shown 触发 InitializeEngine + StartNetwork 之后再进入游戏循环
            // 不开端口——端口由 StartNetwork() 在 Form.Shown 后统一打开
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_engineReady)
                {
                    lock (_engineRunSync)
                    {
                        if (_engineReady)
                            _mirApp.Run();
                    }
                }
                await Task.Delay(TimeSpan.FromMilliseconds(10), stoppingToken);
            }
        }

        private volatile bool _engineReady;
        private volatile bool _networkStarted;

        public void StartNetwork()
        {
            if (_networkStarted) return;
            if (M2Share.boStartReady)
            {
                M2Share.GateManager.Start();
                _networkStarted = true;
            }
        }

        /// <summary>由 Form.Shown 调用——先初始化引擎，再开端口，最后标记就绪</summary>
        public void OnFormReady()
        {
            if (_engineReady) return;
            if (!InitializeEngine()) return;
            StartNetwork();
            _engineReady = true;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            // Phase 1: 只加载配置（不做 DB / Map / Engine 初始化）
            // Delphi 模式: Form.Show → 逐项加载 → 最后开端口
            M2Share.MainOutMessage("正在读取配置信息...");
            _mirApp.InitializeServer();
            M2Share.MainOutMessage("读取配置信息完成... (等待 GUI 就绪后初始化引擎)");
            return base.StartAsync(cancellationToken);
        }

        /// <summary>Phase 2: 在 Form.Shown 之后调用，逐项初始化（匹配 Delphi OnShow）</summary>
        public bool InitializeEngine()
        {
            try
            {
                M2Share.MainOutMessage("正在加载物品数据库...");
                if (!_mirApp.Initialize())
                {
                    M2Share.boStartReady = false;
                    M2Share.DataServer?.Stop();
                    M2Share.MainOutMessage("初始化失败，网络服务未启动。");
                    return false;
                }
                M2Share.MainOutMessage("物品/地图/怪物/技能加载完成");
                _mirApp.StartEngine();
                M2Share.MainOutMessage("引擎启动完成");
                _mirApp.StartService();
                M2Share.MainOutMessage("服务初始化完成");
                return true;
            }
            catch
            {
                M2Share.boStartReady = false;
                M2Share.DataServer?.Stop();
                throw;
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _engineReady = false;
            M2Share.boStartReady = false;
            M2Share.GateManager?.Stop();
            lock (_engineRunSync)
            {
                try
                {
                    if (!_mirApp.SaveItemNumber())
                        M2Share.ErrorMessage("停机前物品编号或全局变量保存未完成。");
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage($"保存物品编号失败: {ex.Message}");
                }
                if (!M2Share.SaveChatLog())
                    M2Share.ErrorMessage("停机前聊天日志保存未完成。");
                _mirApp.Stop();
            }
            return base.StopAsync(cancellationToken);
        }

        public void RequestShutdown()
        {
            _applicationLifetime.StopApplication();
            Application.Exit();
        }
    }
}
