using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DBSvr.Core;
using DBSvr.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DBSvr
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((ctx, services) =>
                {
                    // 配置
                    var configMgr = new ConfigManager(Path.Combine(AppContext.BaseDirectory, "DBService.ini"));
                    services.AddSingleton(configMgr);

                    // 角色系统
                    services.AddSingleton<IPlayRecordService, MySqlPlayRecordService>();
                    services.AddSingleton<IPlayDataService, MySqlPlayDataService>();
                    services.AddSingleton<IHeroRecordService, MySqlHeroRecordService>();
                    services.AddSingleton<IHeroDataService, MySqlHeroDataService>();

                    // 仓库/宠物/师徒/转区
                    services.AddSingleton<IStorageService, MySqlStorageService>();
                    services.AddSingleton<IPetService, MySqlPetService>();
                    services.AddSingleton<IZongpaiService, MySqlZongpaiService>();
                    // 角色改名 opcode 0xFB0：主档(fn_5A8DDC) + 22 条三库级联(fn_5A923C)
                    services.AddSingleton<INativeRenameCascadeService,
                        MySqlNativeRenameCascadeService>();
                    services.AddSingleton<ITransferAreaService, MySqlTransferAreaService>();
                    services.AddSingleton<NativeAccountStorageCache>();
                    services.AddSingleton<NativeUserAdmissionControl>();
                    services.AddSingleton<INativeHallOfFameService,
                        MySqlNativeHallOfFameService>();
                    services.AddSingleton<INativeAwardPlayerService,
                        MySqlNativeAwardPlayerService>();

                    // 核心工具
                    services.AddSingleton<SensitiveWordFilter>();
                    services.AddSingleton<WhitelistService>();
                    services.AddSingleton<NativeType2InitializationCache>();
                    services.AddSingleton<INativeType2StaticLoader,
                        MySqlNativeType2StaticLoader>();
                    services.AddSingleton<INativeType2StdItemsImportService,
                        MySqlNativeType2StdItemsImportService>();
                    services.AddSingleton<INativeType2RankingLoader,
                        MySqlNativeType2RankingLoader>();
                    services.AddSingleton<NativeType2RankingReloadCoordinator>();
                    services.AddSingleton<NativeHeroLogicalCache>();
                    services.AddSingleton<NativeDominatorPetCache>();
                    services.AddSingleton<NativeDominatorPetBackupQueue>();
                    services.AddSingleton<INativeForceLevelStore,
                        NativeForceLevelStore>();
                    services.AddSingleton<NativeForceLevelService>();
                    services.AddSingleton(s => new CleanupService(DBShare.DBConnection));
                    services.AddSingleton(s => new BackupService(DBShare.DBConnection));

                    // 网络服务
                    services.AddSingleton<LoginSvrService>();
                    services.AddSingleton<UserSocService>();
                    services.AddSingleton<GameSocService>();

                    // GUI
                    services.AddSingleton<MainForm>();
                })
                .Build();

            using (host)
            {
                // Load native configuration before resolving services that capture the
                // connection string (cleanup, backup and rename services).
                DBShare.Initialization();
                host.Services.GetRequiredService<ConfigManager>().LoadConfig();
                DBShare.LoadConfig();
                var form = host.Services.GetRequiredService<MainForm>();
                Application.Run(form);
            }
        }
    }
}
