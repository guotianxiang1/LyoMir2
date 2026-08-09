using System.Text.Json;
using GameSvr;

namespace GameSvr.Plugins
{
    /// <summary>
    /// Plugin Configuration Panel — Console-based UI for managing plugins.
    ///
    /// Provides:
    ///   - Plugin list view with status indicators
    ///   - Enable/disable plugins at runtime
    ///   - Configuration editing via JSON
    ///   - Plugin health monitoring
    ///   - Command statistics
    ///   - Hot-reload capability
    ///
    /// Can be extended with a WinForms/WPF GUI in a separate project.
    /// Accessible via GM commands: @plugin list|status|config|reload
    /// </summary>
    public class PluginConfigPanel
    {
        private readonly PluginManager _manager;

        public PluginConfigPanel(PluginManager manager)
        {
            _manager = manager;
        }

        // ===== Console Rendering =====

        /// <summary>
        /// Render the full plugin management console view.
        /// </summary>
        public string RenderDashboard()
        {
            var sb = new System.Text.StringBuilder();
            var plugins = _manager.GetAllPlugins();

            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║               Plugin Management Console                      ║");
            sb.AppendLine("╠══════════════════════════════════════════════════════════════╣");

            // Plugin list
            sb.AppendLine("║ ID  Name              Status     Type      Cmds    Errors   ║");
            sb.AppendLine("╠══════════════════════════════════════════════════════════════╣");

            int id = 0;
            foreach (var p in plugins)
            {
                id++;
                var health = _manager.GetPluginHealth(p.Name);
                var statusIcon = p.State switch
                {
                    PluginState.Running => "●",
                    PluginState.Error => "✕",
                    PluginState.Loading => "◐",
                    _ => "○"
                };

                sb.AppendLine($"║ {id,-2} {statusIcon} {Truncate(p.Name, 15),-15}  {p.State.ToString() ?? "",-7}  {p.Type.ToString() ?? "",-7}  {health.CommandCount,7}  {health.ErrorCount,7}  ║");
            }

            sb.AppendLine("╠══════════════════════════════════════════════════════════════╣");
            sb.AppendLine("║ [L]ist  [E]nable <id>  [D]isable <id>  [C]onfig <id>       ║");
            sb.AppendLine("║ [R]eload <id>  [H]ealth <id>  [S]tats  [Q]uit              ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            return sb.ToString();
        }

        /// <summary>
        /// Render detailed plugin status.
        /// </summary>
        public string RenderPluginDetail(string pluginName)
        {
            var info = _manager.GetPlugin(pluginName);
            if (info == null) return $"Plugin not found: {pluginName}";

            var health = _manager.GetPluginHealth(pluginName);
            var config = _manager.GetPluginConfig(pluginName);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"╔══ Plugin: {info.Name} ════════════════════════════════════╗");
            sb.AppendLine($"║ Version:     {info.Version}");
            sb.AppendLine($"║ Type:        {info.Type}");
            sb.AppendLine($"║ State:       {info.State}");
            sb.AppendLine($"║ Description: {info.Description}");
            sb.AppendLine($"║ Status:      {health.Status}");
            sb.AppendLine($"║ Uptime:      {health.Uptime}");
            sb.AppendLine($"║ Commands:    {health.CommandCount}");
            sb.AppendLine($"║ Errors:      {health.ErrorCount}");
            sb.AppendLine($"║ Memory:      ~{health.MemoryEstimateMB} MB");
            if (!string.IsNullOrEmpty(health.LastError))
                sb.AppendLine($"║ Last Error:  {health.LastError}");
            sb.AppendLine($"║ Config Keys: {config.Count}");
            foreach (var kv in config)
                sb.AppendLine($"║   {kv.Key} = {kv.Value}");
            sb.AppendLine($"╚══════════════════════════════════════════════════════════╝");
            return sb.ToString();
        }

        /// <summary>
        /// Render all running plugins quick status.
        /// </summary>
        public string RenderQuickStatus()
        {
            var running = _manager.GetRunningPlugins();
            if (running.Count == 0) return "No plugins running.";

            var sb = new System.Text.StringBuilder();
            foreach (var p in running)
            {
                var health = _manager.GetPluginHealth(p.Name);
                sb.AppendLine($"[{p.Name}] v{p.Version} | {health.Uptime.TotalMinutes:F0}min | {health.CommandCount} cmds");
            }
            return sb.ToString();
        }

        // ===== Yanshen-Specific Config Presets =====

        /// <summary>
        /// Apply default yanshen-compatible configuration.
        /// Sets up the standard yanshen settings for:
        ///   - 17-element system
        ///   - Custom damage formulas
        ///   - Pet attribute templates
        ///   - Auto-recycle rules
        ///   - Skill effect mappings
        /// </summary>
        public void ApplyYanshenDefaults()
        {
            var yanshenConfig = new Dictionary<string, object>
            {
                // --- Element System ---
                ["elements.enabled"] = true,
                ["elements.maxValue"] = 255,
                ["elements.maxFirstValue"] = 2100000000, // ys1 can be up to 2.1 billion
                ["elements.names"] = new[] {
                    "忽视防御", "物理伤害减少", "魔法伤害减少",
                    "增加伤害", "增加暴击", "增加攻击伤害",
                    "增加攻击", "增加魔法", "增加道术",
                    "增加防御", "增加魔防", "HP恢复",
                    "MP恢复", "暴击等级", "攻击等级",
                    "防御等级", "魔法等级"
                },

                // --- Custom Damage ---
                ["damage.formula"] = "(maxDC - targetMaxAC) + (baseHP * (magicLv + 1)) / 10 + cuttingV",
                ["damage.maxMultiplier"] = 5.0,
                ["damage.cuttingEnabled"] = true,

                // --- Pet System ---
                ["pet.defaultAc"] = 50,
                ["pet.defaultDc"] = 100,
                ["pet.defaultHp"] = 5000,
                ["pet.maxPets"] = 5,

                // --- Auto Recycle ---
                ["recycle.enabled"] = false,
                ["recycle.jsonPath"] = "MyJson/recycle_rules.json",

                // --- Skill Effects ---
                ["skills.fireWallMaxDuration"] = 60,
                ["skills.bounceMaxCount"] = 8,
                ["skills.cdResolutionMs"] = true, // millisecond CD

                // --- DB Operations ---
                ["db.allowScriptSql"] = true,
                ["db.maxResultRows"] = 1000,

                // --- Limits ---
                ["limits.maxDamagePerHit"] = 1000000,
                ["limits.paralysisMaxDuration"] = 60,
                ["limits.poisonMaxDuration"] = 300,
            };

            _manager.SavePluginConfig("YanshenCompat", yanshenConfig);
            M2Share.MainOutMessage("[PluginConfig] Applied yanshen default configuration.");
        }

        /// <summary>
        /// Export yanshen-compatible AllFuc.pas wrapper template.
        /// This generates the Pascal wrapper file that scripts would include
        /// to use the native command engine.
        /// </summary>
        public string GenerateAllFucPasTemplate()
        {
            return @"
// AllFuc.pas — Yanshen Plugin API Wrappers (Native Implementation)
// Auto-generated by PluginConfigPanel. Do not modify manually.
// These functions call the native C# command engine via !!!! tunnel protocol.

unit AllFuc;

interface

// === Element System (17元素) ===
procedure Ys_GivePis(Player:TObject; id,pis,val:Integer);
function  Ys_GetPis(Player:TObject; id,pis:Integer):Integer;
procedure Ys_GiveNewItem(Player:TObject; ItemName:string; isbind:Integer;
  ys1,ys2,ys3,ys4,ys5,ys6,ys7,ys8,ys9,ys10,ys11,ys12,ys13,ys14,ys15,ys16,ys17:Integer);
procedure Ys_GiveItemYS_JP(Player:TObject; ItemName:string; isbind:Integer;
  ys1..ys17, jp1,jp2,jp3,jp4,jp5,jp6:Integer);

// === Custom Damage (自定义伤害) ===
function Ys_MyYsJn(Player:TObject; magicLV,baseHP,round,TargetX,TargetY,
  Canl,types,cuttingV,ys_id,v1,Doubling:Integer; lei:string):Integer;
function ys_MyJn_plus2(Player:TObject; magicLV,baseHP,round,TargetX,TargetY,
  Canl,types,cuttingV,lei:Integer):Integer;

// === Control Skills (控制技能) ===
function Ys_Mymabi(Player:TObject; timer,rand,round,TargetX,TargetY,
  Canl:Integer; isqun:Boolean):Integer;
function ys_ShiDu(Player:TObject; shijian,leix,hp,gailv,fanwei,
  TargetX,TargetY,Canl,isqun:Integer):Integer;
function ys_JiTui(Player:TObject; juli,fangxiang,gailv,fanwei,TargetX,
  TargetY,Canl,isqun:Integer):Integer;
function ys_TuiTui2(Player:TObject; why,level,gailv,fanwei,TargetX,
  TargetY,Canl,isqun,roleid:Integer):Integer;
function ys_DingShen(Player:TObject; shijian:Integer):Integer;
function ys_XiXue(Player:TObject; hp,bf_hp:Integer):Integer;

// === Pet/Baby System (宝宝系统) ===
function ys_SetPetV(Player:TObject; MonName:string; id,Ac,Dc,DcMax,Mac,
  Mc,Sc,gs,ys,hp,Maxhp:Integer):Integer;
function ys_MakeSlaveEx(Player:TObject; MonName:string; num,lv,Ac,Dc,DcMax,
  Mac,Mc,Sc,gs,ys,hp,Maxhp:Integer):Integer;

// === Item Operations (物品操作) ===
function ys_WupinMakeIndex(Player:TObject; isall:Boolean):string;
function ys_WupinGetData(Player:TObject; MakeIndex:Integer):string;
function Ys_GiveBind(Player:TObject; itemid,flag:Integer):Integer;
function Ys_GetItemid(Player:TObject; ClientItemID:Integer):Integer;

// === Pet/Player Attribute (属性操作) ===
function Ys_Getshuxing(Player:TObject; roleid,types:Integer):Integer;
function ys_GetMemberCount(Player:TObject):Integer;
function ys_GetMember_PlayerName(Player:TObject; ID:Integer):string;

// === Database (数据库) ===
function ys_SqlDbInsert(Player:TObject; sql:string; fg:Boolean):Integer;
function ys_SqlDbSelect(Player:TObject; sql:string):string;

// === CD Timer (CD定时器) ===
function ys_CDGetTimes(y:Integer):Integer;
function ys_CmpTime(v_x,v_y,chazhi:Integer):Boolean;
function ys_GetTime_cha(v_x,v_y:Integer):Integer;
procedure ys_SetCD(v_x,v_y:Integer);

// === Other (其他) ===
function Ys_SetTimerByName(Player:TObject; timer:Integer; fucName:string):Integer;
function ys_PlayerOut(Player:TObject):Integer;
function Ys_NewXiGuai(Player:TObject; round,lv,num:Integer):Integer;

implementation
// Implementation delegates to native C# engine via !!!! tunnel
// All functions encoded as GetBagItemCount('!!!!id,params$')
end.
".Trim();
        }

        // ===== Utility =====

        private static string Truncate(string value, int maxLen) =>
            value.Length <= maxLen ? value : value.Substring(0, maxLen - 1) + "…";

        /// <summary>
        /// Process a GM console command for plugin management.
        /// Commands:
        ///   @plugin list               - show all plugins
        ///   @plugin status [name]      - show plugin detail
        ///   @plugin enable <name>      - enable/load a plugin
        ///   @plugin disable <name>     - disable/unload a plugin
        ///   @plugin reload <name>      - hot-reload a plugin
        ///   @plugin config <name>      - show plugin config
        ///   @plugin config <name> set key=value - set config
        ///   @plugin yanshen defaults   - apply yanshen defaults
        ///   @plugin yanshen allfuc     - generate AllFuc.pas template
        ///   @plugin health             - quick health check
        /// </summary>
        public string ProcessCommand(string args)
        {
            var parts = args?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            if (parts.Length == 0) return RenderDashboard();

            switch (parts[0].ToLower())
            {
                case "list":
                    return RenderDashboard();

                case "status":
                    return parts.Length >= 2
                        ? RenderPluginDetail(parts[1])
                        : RenderQuickStatus();

                case "enable":
                    return parts.Length >= 2
                        ? (_manager.LoadPlugin(parts[1]) ? $"Enabled: {parts[1]}" : $"Failed to enable: {parts[1]}")
                        : "Usage: @plugin enable <name>";

                case "disable":
                    return parts.Length >= 2
                        ? (_manager.UnloadPlugin(parts[1]) ? $"Disabled: {parts[1]}" : $"Failed to disable: {parts[1]}")
                        : "Usage: @plugin disable <name>";

                case "reload":
                    return parts.Length >= 2
                        ? (_manager.HotReloadPlugin(parts[1]) ? $"Reloaded: {parts[1]}" : $"Failed to reload: {parts[1]}")
                        : "Usage: @plugin reload <name>";

                case "config":
                    if (parts.Length < 2) return "Usage: @plugin config <name> [set key=value]";
                    if (parts.Length >= 4 && parts[2] == "set")
                    {
                        var kv = parts[3].Split('=', 2);
                        if (kv.Length == 2)
                        {
                            var config = _manager.GetPluginConfig(parts[1]);
                            config[kv[0]] = kv[1];
                            _manager.SavePluginConfig(parts[1], config);
                            return $"Set {kv[0]}={kv[1]} on {parts[1]}";
                        }
                    }
                    return RenderPluginDetail(parts[1]);

                case "yanshen":
                    if (parts.Length < 2) return "Usage: @plugin yanshen [defaults|allfuc]";
                    switch (parts[1])
                    {
                        case "defaults":
                            ApplyYanshenDefaults();
                            return "Yanshen defaults applied.";
                        case "allfuc":
                            return GenerateAllFucPasTemplate();
                        default:
                            return "Unknown yanshen subcommand";
                    }

                case "health":
                    return RenderQuickStatus();

                default:
                    return $"Unknown plugin command: {parts[0]}. Try: list, status, enable, disable, reload, config";
            }
        }
    }
}
