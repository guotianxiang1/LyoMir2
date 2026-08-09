using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GameSvr;

namespace GameSvr.Plugins
{
    public class PluginHttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly PluginManager _manager;
        private readonly PluginConfigPanel _panel;
        private readonly CancellationTokenSource _cts;
        private Task _listenTask;
        private readonly int _port;
        private readonly List<TunnelLogEntry> _commandLog = new();
        private const int MaxLogEntries = 200;

        public PluginHttpServer(PluginManager manager, int port = 8899)
        {
            _manager = manager;
            _panel = new PluginConfigPanel(manager);
            _port = port;
            _cts = new CancellationTokenSource();
            if (port > 0)
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://+:" + port + "/");
                _listener.Prefixes.Add("http://localhost:" + port + "/");
            }
        }

        public void Start()
        {
            if (_port <= 0 || _listener == null) return;
            try
            {
                _listener.Start();
                _listenTask = Task.Run(() => ListenLoop(_cts.Token));
                M2Share.MainOutMessage("[PluginGUI] Web panel: http://localhost:" + _port + "/plugins");
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                M2Share.MainOutMessage("[PluginGUI] Admin rights needed. Run: netsh http add urlacl url=http://+:" + _port + "/ user=Everyone");
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[PluginGUI] Failed: " + ex.Message);
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            _listener?.Stop();
            _listenTask?.Wait(TimeSpan.FromSeconds(3));
        }

        public void LogCommand(TunnelCommand cmd, int result)
        {
            if (_commandLog.Count >= MaxLogEntries)
                _commandLog.RemoveAt(0);
            _commandLog.Add(new TunnelLogEntry
            {
                Time = DateTime.Now,
                CommandId = cmd.CommandId,
                ChineseCommand = cmd.ChineseCommand,
                RawPayload = Truncate(cmd.RawPayload, 80),
                Result = result,
            });
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                    _ = Task.Run(() => HandleRequest(ctx), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) { break; }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath.ToLower();
                var method = ctx.Request.HttpMethod;
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                ctx.Response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

                if (method == "OPTIONS") { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

                switch (path)
                {
                    case "/": case "/plugins": case "/plugins/":
                        ServeHtml(ctx, BuildIndexHtml()); break;
                    case "/api/plugins":
                        ServeJson(ctx, _manager.GetAllPlugins().Select(p => new {
                            p.Name, p.Version, p.Description, p.Type,
                            State = p.State.ToString(),
                            Health = _manager.GetPluginHealth(p.Name)
                        })); break;
                    case "/api/plugins/toggle":
                        if (method == "POST") HandleToggle(ctx);
                        else ServeJson(ctx, GetFeatureToggles()); break;
                    case "/api/plugins/config":
                        if (method == "POST") HandleConfigSave(ctx);
                        else HandleConfigGet(ctx); break;
                    case "/api/plugins/commandlog":
                        ServeJson(ctx, _commandLog); break;
                    case "/api/plugins/enable":
                        HandlePluginAction(ctx, _manager.LoadPlugin); break;
                    case "/api/plugins/disable":
                        HandlePluginAction(ctx, _manager.UnloadPlugin); break;
                    case "/api/plugins/reload":
                        HandlePluginAction(ctx, _manager.HotReloadPlugin); break;
                    case "/api/plugins/yanshen/defaults":
                        _panel.ApplyYanshenDefaults();
                        ServeJson(ctx, new { ok = true, msg = "Defaults applied" }); break;
                    default:
                        ctx.Response.StatusCode = 404; ServeText(ctx, "Not Found"); break;
                }
            }
            catch { try { ctx.Response.StatusCode = 500; ServeText(ctx, "Error"); } catch { } }
        }

        private void HandleToggle(HttpListenerContext ctx)
        {
            var body = ReadBody(ctx);
            var incoming = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
            string error = null;
            Dictionary<string, object> changes = null;
            if (incoming == null || !TryBuildNativeChanges(incoming, true, out changes, out error))
            {
                ctx.Response.StatusCode = 400;
                ServeJson(ctx, new { ok = false, error = error ?? "Invalid JSON" });
                return;
            }

            var saved = _manager.ApplyNativeConfigChanges(changes, out error);
            if (!saved) ctx.Response.StatusCode = 500;
            ServeJson(ctx, new { ok = saved, error });
        }

        private List<FeatureToggle> GetFeatureToggles()
        {
            var toggles = new List<FeatureToggle>();
            var native = _manager.GetNativeConfig();
            if (native == null || native.Count == 0) return toggles;

            string Categorize(string key)
            {
                if (key.Contains("全屏拾取") || key.Contains("高级回收") || key.Contains("大背包") || key.Contains("临时大背包") || key.Contains("随身仓库") || key.Contains("名字变色") || key.Contains("踢玩家下线") || key.Contains("毫秒级cd记录") || key.Contains("全屏吸怪") || key.Contains("全服击杀提示")) return "第1季 — 基础";
                if (key.Contains("刀刀切割") || key.Contains("攻击反伤") || key.Contains("攻击吸血") || key.Contains("装备吸血") || key.Contains("麻痹概率") || key.Contains("新倍攻和暴击") || key.Contains("装备提升")) return "第1季 — 切割倍攻";
                if (key.Contains("剑术") || key.Contains("剑法") || key.Contains("弯刀") || key.Contains("真气") || key.Contains("野蛮") || key.Contains("雷电") || key.Contains("火球") || key.Contains("火雨") || key.Contains("火墙") || key.Contains("火符") || key.Contains("冰咆哮") || key.Contains("爆裂火焰") || key.Contains("地狱雷光") || key.Contains("激光") || key.Contains("嗜血术") || key.Contains("施毒术") || key.Contains("召唤") || key.Contains("半月") || key.Contains("群毒") || key.Contains("免毒符") || key.Contains("自定义伤害")) return "第2季 — 技能";
                if (key.Contains("元素") || key.Contains("来源") || key.Contains("投保") || key.Contains("绑定") || key.Contains("随机极品") || key.Contains("give极品")) return "第2季 — 装备";
                if (key.Contains("技能触发") || key.Contains("魔法攻击触发") || key.Contains("物理攻击触发") || key.Contains("攻击触发") || key.Contains("穿戴触发") || key.Contains("BB") || key.Contains("怪物触发") || key.Contains("全局循环函数")) return "第3季 — 系统";
                if (key.Contains("屏蔽元宝") || key.Contains("物品数据")) return "第3季 — 数据库";
                if (key.Contains("特殊宝宝") || key.Contains("特殊属性") || key.Contains("宠物") || key.Contains("宝宝")) return "星耀 — 宠物英雄";
                if (key.Contains("英雄")) return "星耀 — 宠物英雄";
                if (key.Contains("行会") || key.Contains("战队") || key.Contains("永久")) return "星耀 — 组队属性";
                if (key.Contains("星耀")) return "星耀";
                if (key.Contains("盘古")) return "星耀 — 盘古";
                if (key.Contains("合击")) return "星耀 — 合击";
                if (key.Contains("摆摊") || key.Contains("交易") || key.Contains("禁止丢物")) return "其他 — 摆摊交易";
                if (key.Contains("复活戒指") || key.Contains("护身") || key.Contains("生命")) return "其他 — 戒指";
                if (key.Contains("武器属性") || key.Contains("衣服属性") || key.Contains("头盔属性") || key.Contains("项链属性") || key.Contains("手镯属性") || key.Contains("戒指属性")) return "其他 — 极品属性";
                return "其他";
            }

            foreach (var kv in native.OrderBy(x => x.Key))
            {
                var row = TogglePanel.ToggleRow.FromConfig(kv.Key, kv.Value);
                if (!row.IsToggle) continue;

                toggles.Add(new FeatureToggle { Id = kv.Key, Label = kv.Key, Category = Categorize(kv.Key), On = row.BoolValue });
            }
            return toggles;
        }

        private void HandleConfigGet(HttpListenerContext ctx)
        {
            var name = ctx.Request.QueryString["name"] ?? "YanshenCompat";
            ServeJson(ctx, string.Equals(name, "YanshenCompat", StringComparison.OrdinalIgnoreCase)
                ? _manager.GetNativeConfig()
                : _manager.GetPluginConfig(name));
        }

        private void HandleConfigSave(HttpListenerContext ctx)
        {
            var name = ctx.Request.QueryString["name"] ?? "YanshenCompat";
            var body = ReadBody(ctx);
            var config = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
            if (config == null)
            {
                ctx.Response.StatusCode = 400;
                ServeJson(ctx, new { ok = false, error = "Invalid JSON" });
                return;
            }

            if (string.Equals(name, "YanshenCompat", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryBuildNativeChanges(config, false, out var changes, out var error))
                {
                    ctx.Response.StatusCode = 400;
                    ServeJson(ctx, new { ok = false, error });
                    return;
                }
                var saved = _manager.ApplyNativeConfigChanges(changes, out error);
                if (!saved) ctx.Response.StatusCode = 500;
                ServeJson(ctx, new { ok = saved, error });
                return;
            }

            var pluginSaved = _manager.SavePluginConfig(name, config);
            if (!pluginSaved) ctx.Response.StatusCode = 500;
            ServeJson(ctx, new { ok = pluginSaved });
        }

        private bool TryBuildNativeChanges(
            IReadOnlyDictionary<string, object> incoming,
            bool togglesOnly,
            out Dictionary<string, object> changes,
            out string error)
        {
            changes = new Dictionary<string, object>(StringComparer.Ordinal);
            error = null;
            var current = _manager.GetNativeConfig();

            foreach (var (key, rawValue) in incoming)
            {
                if (!current.TryGetValue(key, out var originalValue))
                {
                    error = $"Unknown native config key: {key}";
                    return false;
                }

                var row = TogglePanel.ToggleRow.FromConfig(key, originalValue);
                var value = PluginManager.NormalizeConfigValue(rawValue);
                if (togglesOnly && !row.IsToggle)
                {
                    error = $"Config key is not a switch: {key}";
                    return false;
                }

                if (row.IsToggle)
                {
                    if (!TryReadBoolean(value, out var enabled))
                    {
                        error = $"Invalid switch value for {key}";
                        return false;
                    }
                    changes[key] = row.GetToggleValue(enabled);
                    continue;
                }

                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (!row.TryConvertText(text, out var converted, out var conversionError))
                {
                    error = $"{key}: {conversionError}";
                    return false;
                }
                changes[key] = converted;
            }
            return true;
        }

        private static bool TryReadBoolean(object value, out bool enabled)
        {
            if (value is bool boolean)
            {
                enabled = boolean;
                return true;
            }
            if (value is string text)
            {
                if (bool.TryParse(text, out enabled)) return true;
                if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    enabled = number != 0;
                    return true;
                }
            }
            if (value is IConvertible convertible)
            {
                try
                {
                    enabled = convertible.ToDecimal(CultureInfo.InvariantCulture) != 0;
                    return true;
                }
                catch { }
            }
            enabled = false;
            return false;
        }

        private void HandlePluginAction(HttpListenerContext ctx, Func<string, bool> action)
        {
            var name = ctx.Request.QueryString["name"];
            if (string.IsNullOrEmpty(name)) { ctx.Response.StatusCode = 400; ServeJson(ctx, new { ok = false, error = "Missing name" }); return; }
            ServeJson(ctx, new { ok = action(name), name });
        }

        // ===== HTML Builder (no string interpolation to avoid C# brace conflicts) =====

        private string BuildIndexHtml()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html lang='zh-CN'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
            sb.AppendLine("<title>眼神插件 — 配置管理</title>");

            // CSS
            sb.AppendLine("<style>");
            sb.AppendLine("*{margin:0;padding:0;box-sizing:border-box}body{font-family:'Segoe UI','Microsoft YaHei',sans-serif;background:#1a1a2e;color:#e0e0e0;min-height:100vh}");
            sb.AppendLine(".header{background:#16213e;padding:16px 24px;border-bottom:2px solid #0f3460;display:flex;justify-content:space-between;align-items:center}");
            sb.AppendLine(".header h1{font-size:20px;color:#e94560}.header .status{font-size:13px;color:#888}");
            sb.AppendLine(".container{max-width:1200px;margin:0 auto;padding:20px}");
            sb.AppendLine(".tabs{display:flex;gap:8px;margin-bottom:20px}");
            sb.AppendLine(".tab{padding:8px 20px;background:#16213e;border:1px solid #0f3460;color:#aaa;cursor:pointer;border-radius:4px 4px 0 0;font-size:14px}");
            sb.AppendLine(".tab.active{background:#0f3460;color:#fff;border-color:#e94560}.tab:hover{color:#fff}");
            sb.AppendLine(".panel{display:none}.panel.active{display:block}");
            sb.AppendLine(".card{background:#16213e;border:1px solid #0f3460;border-radius:8px;padding:20px;margin-bottom:16px}");
            sb.AppendLine(".card h2{font-size:16px;margin-bottom:16px;color:#e94560}");
            sb.AppendLine(".category{margin-bottom:24px}.category h3{font-size:14px;color:#aaa;margin-bottom:10px;padding-bottom:6px;border-bottom:1px solid #0f3460}");
            sb.AppendLine(".toggle-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:10px}");
            sb.AppendLine(".toggle-item{display:flex;align-items:center;gap:10px;padding:8px 12px;background:#1a1a2e;border-radius:6px;border:1px solid #0f3460}");
            sb.AppendLine(".toggle-item.on{border-color:#2ecc71}.toggle-item.off{border-color:#e74c3c;opacity:.7}");
            sb.AppendLine(".switch{position:relative;display:inline-block;width:44px;height:24px;flex-shrink:0}");
            sb.AppendLine(".switch input{opacity:0;width:0;height:0}");
            sb.AppendLine(".slider{position:absolute;cursor:pointer;top:0;left:0;right:0;bottom:0;background:#444;border-radius:24px;transition:.3s}");
            sb.AppendLine(".slider:before{content:'';position:absolute;height:18px;width:18px;left:3px;bottom:3px;background:#fff;border-radius:50%;transition:.3s}");
            sb.AppendLine("input:checked+.slider{background:#2ecc71}input:checked+.slider:before{transform:translateX(20px)}");
            sb.AppendLine(".label{font-size:13px}table{width:100%;border-collapse:collapse;font-size:13px}");
            sb.AppendLine("th,td{padding:10px 14px;text-align:left;border-bottom:1px solid #0f3460}th{color:#888;font-weight:600}tr:hover{background:#1a1a2e}");
            sb.AppendLine(".btn{padding:6px 16px;border:none;border-radius:4px;cursor:pointer;font-size:13px;margin-right:8px}");
            sb.AppendLine(".btn-primary{background:#e94560;color:#fff}.btn-primary:hover{background:#c23152}");
            sb.AppendLine(".btn-success{background:#2ecc71;color:#fff}.btn-warning{background:#f39c12;color:#fff}.btn-danger{background:#e74c3c;color:#fff}");
            sb.AppendLine(".actions{display:flex;gap:8px;margin-top:12px;flex-wrap:wrap}");
            sb.AppendLine(".toast{position:fixed;top:20px;right:20px;padding:12px 20px;border-radius:6px;color:#fff;font-size:14px;z-index:9999;animation:fadeIn .3s}");
            sb.AppendLine(".toast.success{background:#2ecc71}.toast.error{background:#e74c3c}");
            sb.AppendLine("@keyframes fadeIn{from{opacity:0;transform:translateY(-10px)}to{opacity:1;transform:translateY(0)}}");
            sb.AppendLine(".refresh{color:#888;font-size:12px;text-align:right}");
            sb.AppendLine("</style></head><body>");

            // Header
            sb.AppendLine("<div class='header'><div><h1>&#x2699; 眼神插件 — 配置面板 (Web)</h1></div><div class='status' id='serverStatus'>Connecting...</div></div><div class='container'>");

            // Tabs
            sb.AppendLine("<div class='tabs'><div class='tab active' onclick='switchTab(\"toggles\")'>季节开关</div><div class='tab' onclick='switchTab(\"plugins\")'>插件</div><div class='tab' onclick='switchTab(\"log\")'>命令日志</div><div class='tab' onclick='switchTab(\"info\")'>关于</div></div>");

            // Panel: Toggles
            sb.AppendLine("<div id='panel-toggles' class='panel active'><div class='card'><h2>功能开关 — 按季节分组</h2><p style='color:#888;font-size:12px;margin-bottom:16px'>共 4 季：第1季(基础/切割倍攻) | 第2季(技能/控制/装备元素) | 第3季(技能物品/数据库/系统) | 星耀(宠物英雄/组队属性)。关闭的功能立即返回 0。</p>");
            BuildToggleHtml(sb);
            sb.AppendLine("<div class='actions'><button class='btn btn-primary' onclick='saveAllToggles()'>Save</button><button class='btn btn-warning' onclick='applyDefaults()'>Defaults</button><button class='btn btn-danger' onclick='disableAll()'>All Off</button><button class='btn btn-success' onclick='enableAll()'>All On</button></div></div></div>");

            // Panel: Plugins
            sb.AppendLine("<div id='panel-plugins' class='panel'><div class='card'><h2>Registered Plugins</h2><table><tr><th></th><th>Name</th><th>Version</th><th>State</th><th>Commands</th><th>Errors</th></tr>");
            BuildPluginHtml(sb);
            sb.AppendLine("</table><div class='refresh' id='pluginRefresh'>Auto-refreshing...</div></div></div>");

            // Panel: Log
            sb.AppendLine("<div id='panel-log' class='panel'><div class='card'><h2>Command Log (recent 20)</h2><table id='logTable'><tr><th>Time</th><th>CmdID</th><th>Payload</th><th>Result</th></tr></table><div class='refresh' id='logRefresh'>Auto-refreshing...</div></div></div>");

            // Panel: Info
            sb.AppendLine("<div id='panel-info' class='panel'><div class='card'><h2>关于</h2><p style='line-height:1.8'><b>眼神插件 — C# 原生实现</b><br>替代 VMProtect 保护的 yanshen2.0.7.dll<br>协议兼容 !!!! 隧道 + AllFuc.pas<br><br><b>功能分季：</b>第1季(基础) → 第2季(技能控制元素) → 第3季(数据库定时器怪物) → 星耀(宠物英雄组队)<br><br><b>API：</b>41 命令ID + 37 caret命令 + ~100 PAS内置函数<br><br><b>命令：</b> @plugin list|status|enable|disable|reload|config|yanshen|health</p></div></div>");

            sb.AppendLine("</div></div>");

            // JavaScript
            sb.AppendLine("<script>");
            sb.AppendLine("var API='/api/plugins',pendingToggles={};");
            sb.AppendLine("function switchTab(n){document.querySelectorAll('.tab').forEach(function(t){t.classList.remove('active')});document.querySelectorAll('.panel').forEach(function(p){p.classList.remove('active')});document.querySelector('.tab[onclick*='+n+']').classList.add('active');document.getElementById('panel-'+n).classList.add('active');if(n==='plugins')refreshPlugins();if(n==='log')refreshLog();}");
            sb.AppendLine("function toggleFeature(id,on){pendingToggles[id]=on;var item=document.getElementById('item-'+id);item.classList.toggle('on',on);item.classList.toggle('off',!on);fetch(API+'/toggle',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({[id]:on})}).then(function(r){return r.json()}).then(function(d){if(d.ok)showToast((on?'ON: ':'OFF: ')+id,'success');});}");
            sb.AppendLine("function saveAllToggles(){fetch(API+'/toggle',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(pendingToggles)}).then(function(r){return r.json()}).then(function(d){if(d.ok){pendingToggles={};showToast('Saved','success');}});}");
            sb.AppendLine("function applyDefaults(){fetch(API+'/yanshen/defaults').then(function(r){return r.json()}).then(function(d){showToast(d.ok?'Defaults applied':'Failed',d.ok?'success':'error');setTimeout(function(){location.reload()},1000);});}");
            sb.AppendLine("function disableAll(){document.querySelectorAll('input[type=checkbox]').forEach(function(cb){cb.checked=false;toggleFeature(cb.id,false);});}");
            sb.AppendLine("function enableAll(){document.querySelectorAll('input[type=checkbox]').forEach(function(cb){cb.checked=true;toggleFeature(cb.id,true);});}");
            sb.AppendLine("function refreshPlugins(){fetch(API).then(function(r){return r.json()}).then(function(data){document.getElementById('pluginRefresh').textContent='Last: '+new Date().toLocaleTimeString();document.getElementById('serverStatus').textContent='Connected';}).catch(function(){document.getElementById('serverStatus').textContent='Disconnected';});}");
            sb.AppendLine("function refreshLog(){fetch(API+'/commandlog').then(function(r){return r.json()}).then(function(data){var tb=document.getElementById('logTable');tb.innerHTML='<tr><th>Time</th><th>CmdID</th><th>Payload</th><th>Result</th></tr>';data.reverse().slice(0,20).forEach(function(e){tb.innerHTML+='<tr><td>'+e.Time+'</td><td>'+e.CommandId+'</td><td>'+e.RawPayload+'</td><td>'+e.Result+'</td></tr>';});document.getElementById('logRefresh').textContent='Last: '+new Date().toLocaleTimeString();});}");
            sb.AppendLine("function showToast(m,t){var d=document.createElement('div');d.className='toast '+t;d.textContent=m;document.body.appendChild(d);setTimeout(function(){d.remove()},2500);}");
            sb.AppendLine("setInterval(refreshPlugins,10000);setInterval(refreshLog,10000);refreshPlugins();");
            sb.AppendLine("</script></body></html>");

            return sb.ToString();
        }

        private void BuildToggleHtml(StringBuilder sb)
        {
            var toggles = GetFeatureToggles();
            var categories = toggles.GroupBy(t => t.Category);
            foreach (var cat in categories)
            {
                sb.AppendLine("<div class='category'><h3>" + cat.Key + "</h3><div class='toggle-grid'>");
                foreach (var t in cat)
                {
                    var checkedAttr = t.On ? " checked" : "";
                    var statusClass = t.On ? "on" : "off";
                    sb.AppendLine("<div class='toggle-item " + statusClass + "' id='item-" + t.Id + "'>");
                    sb.AppendLine("<label class='switch'><input type='checkbox' id='" + t.Id + "'" + checkedAttr + " onchange='toggleFeature(\"" + t.Id + "\",this.checked)'><span class='slider'></span></label>");
                    sb.AppendLine("<span class='label'>" + t.Label + "</span></div>");
                }
                sb.AppendLine("</div></div>");
            }
        }

        private void BuildPluginHtml(StringBuilder sb)
        {
            foreach (var p in _manager.GetAllPlugins())
            {
                var health = _manager.GetPluginHealth(p.Name);
                var dot = p.State == PluginState.Running ? "🟢" : p.State == PluginState.Error ? "🔴" : "⚪";
                sb.AppendLine("<tr><td>" + dot + "</td><td>" + p.Name + "</td><td>" + p.Version + "</td><td>" + p.State + "</td><td>" + health.CommandCount + "</td><td>" + health.ErrorCount + "</td></tr>");
            }
        }

        // ===== Helpers =====

        private void ServeHtml(HttpListenerContext ctx, string html)
        {
            var data = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.Close();
        }

        private void ServeJson(HttpListenerContext ctx, object obj)
        {
            var json = JsonSerializer.Serialize(obj);
            var data = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.Close();
        }

        private void ServeText(HttpListenerContext ctx, string text)
        {
            var data = Encoding.UTF8.GetBytes(text);
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.Close();
        }

        private string ReadBody(HttpListenerContext ctx)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            return reader.ReadToEnd();
        }

        private static string Truncate(string s, int max) =>
            s != null && s.Length > max ? s.Substring(0, max - 3) + "..." : s ?? "";

        public void Dispose()
        {
            Stop();
            _listener?.Close();
            _cts?.Dispose();
        }
    }
}
