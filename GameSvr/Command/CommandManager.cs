using System.Reflection;
using System.Text;
using SystemModule;

namespace GameSvr.CommandSystem
{
    public class CommandManager
    {
        private static readonly Dictionary<string, BaseCommond> CommandMaps = new Dictionary<string, BaseCommond>(StringComparer.OrdinalIgnoreCase);
        
        // 保存原始命令名 → 命令对象，用于热重载时重建 CommandMaps
        private static readonly Dictionary<string, BaseCommond> OriginalCommandMaps = new Dictionary<string, BaseCommond>(StringComparer.OrdinalIgnoreCase);
        
        private static readonly Dictionary<string, string> CustomCommands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public CommandManager()
        {

        }

        public void RegisterCommand()
        {
            M2Share.CommandConf.LoadConfig();
            RegisterCommandGroups();
        }

        
        
        
        private void RegisterCommandGroups()
        {
            var cmdName = string.Empty;
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (!type.IsSubclassOf(typeof(BaseCommond))) continue;//只有继承BaseCommond，才能添加到命令对象中

                var attributes = (GameCommandAttribute[])type.GetCustomAttributes(typeof(GameCommandAttribute), true);
                if (attributes.Length == 0) continue;

                var groupAttribute = attributes[0];
                if (CommandMaps.ContainsKey(groupAttribute.Name))
                {
                    M2Share.ErrorMessage($"重复游戏命令: {groupAttribute.Name}");
                }

                if (CustomCommands.TryGetValue(groupAttribute.Name, out cmdName))
                {
                    groupAttribute.Command = groupAttribute.Name;  // 保存原始命令名
                    groupAttribute.Name = cmdName;
                }

                var commandGroup = (BaseCommond)Activator.CreateInstance(type);
                if (commandGroup == null)
                {
                    return;
                }
                MethodInfo methodInfo = null;
                foreach (var method in commandGroup.GetType().GetMethods())
                {
                    var methodAttributes = method.GetCustomAttribute(typeof(DefaultCommand), true);
                    if (methodAttributes != null)
                    {
                        methodInfo = method;
                        break;
                    }
                }
                if (methodInfo == null)
                {
                    return;
                }
                commandGroup.Register(groupAttribute, methodInfo);
                CommandMaps.Add(groupAttribute.Name, commandGroup);
                // 以原始名为 key 存入 OriginalCommandMaps，供热重载使用
                var originalName = string.IsNullOrEmpty(groupAttribute.Command) ? groupAttribute.Name : groupAttribute.Command;
                OriginalCommandMaps[originalName] = commandGroup;
            }
            ApplyNativeFormGmCommandIni();
        }

        public void RegisterCommand(string command, string commandName)
        {
            CustomCommands[command] = commandName;
        }

        /// <summary>
        /// 热重载自定义命令别名：重新读取 Command.conf 的 [CustomAlias] 节，
        /// 无需重启服务器即可使别名修改生效。
        /// </summary>
        public void ReloadCustomAlias()
        {
            CustomCommands.Clear();
            // 从磁盘重新读取别名并填充 CustomCommands。
            M2Share.CommandConf.ReloadCustomAlias();

            CommandMaps.Clear();
            foreach (var kv in OriginalCommandMaps)
            {
                var origName = kv.Key;
                var cmd = kv.Value;
                // 将 GameCommand.Name 恢复为原始命令名
                cmd.GameCommand.Name = origName;
                var effectiveName = origName;
                if (CustomCommands.TryGetValue(origName, out var alias))
                {
                    cmd.GameCommand.Name = alias;
                    effectiveName = alias;
                }
                if (!CommandMaps.ContainsKey(effectiveName))
                    CommandMaps[effectiveName] = cmd;
            }
            ApplyNativeFormGmCommandIni();
        }

        /// <summary>
        /// Native init 0x0062255E..0x0062258A: IntToStr(record+0x18) then
        /// TStringList.IndexOf on FormGMCommand.ini (long string at 0x006225FC,
        /// lenpfx 17). Hit → UpperCase(value) replaces the hash key; miss keeps
        /// the table ShortString. Production Gs1/FormGMCommand.ini overlays 12
        /// names (gowgo→sdgo, CallMan→拉, SuperGm→gm, ReLoadGmFile→shuagm, …).
        /// </summary>
        private static void ApplyNativeFormGmCommandIni()
        {
            var path = Path.Combine(M2Share.sConfigPath ?? string.Empty, "FormGMCommand.ini");
            if (!File.Exists(path))
                return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.GetEncoding("GBK"));
            }
            catch
            {
                return;
            }
            foreach (var raw in lines)
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '#')
                    continue;
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                if (!int.TryParse(line.Substring(0, eq).Trim(), out var idx))
                    continue;
                var overlay = line.Substring(eq + 1).Trim();
                if (string.IsNullOrEmpty(overlay))
                    continue;
                if (!NativeGmCommandRegistry.DefaultNameByIndex.TryGetValue(idx, out var defaultName))
                    continue;
                if (!OriginalCommandMaps.TryGetValue(defaultName, out var cmd))
                    continue;
                if (string.Equals(overlay, cmd.GameCommand.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                var previous = cmd.GameCommand.Name;
                if (!string.IsNullOrEmpty(previous))
                    CommandMaps.Remove(previous);
                cmd.GameCommand.Name = overlay;
                CommandMaps[overlay] = cmd;
            }
        }

        
        
        
        
        
        public void UpdataRegisterCommandGroups(GameCommandAttribute OldCmd, string sCmd)
        {
            if (CommandMaps.ContainsKey(OldCmd.Command))
            {
                
                
                
                
                

                
                
                
                
            }
        }

        
        
        
        
        
        
        public bool ExecCmd(string line, TPlayObject playObject)
        {
            return ExecCmd(line, playObject, null, 0);
        }

        internal bool ExecCmd(string line, TPlayObject playObject,
            byte[] rawPayload, int bodyLength)
        {
            var output = string.Empty;
            string command;
            string parameters;
            var found = false;

            if (playObject == null)
                throw new ArgumentException("PlayObject");
            if (!ExtractCommandAndParameters(line, out command, out parameters))
                return found;

            BaseCommond commond;
            if (CommandMaps.TryGetValue(command, out commond))
            {
                output = commond.HandleRaw(line, parameters, rawPayload,
                    bodyLength, playObject);
                found = true;
            }

            // 未注册的 @命令 一律不回话。原生 0x00621F4F `mov byte [esi],0` 把「所需权限」出参清 0，
            // 查表未命中时返回索引 0；回到 0x00622AB7 `jne` 不成立后 0x00622AC2 `jbe 0x622B09` 直接
            // 跳过唯一的失败回复（"该命令需要"0x0062B768 + "级GM才能使用"0x0062B77C），落到
            // jt[0]=0x0062B648 静默收尾。回一句"未知命令"等于把命令是否存在泄露给任意玩家。
            if (!string.IsNullOrEmpty(output))
            {
                playObject.SysMsg(output, MsgColor.Red, MsgType.Hint);
            }
            return found;
        }

        public void ExecCmd(string line)
        {
            var output = string.Empty;
            string command;
            string parameters;
            var found = false;

            if (!ExtractCommandAndParameters(line, out command, out parameters))
                return;

            BaseCommond commond = null;
            if (CommandMaps.TryGetValue(command, out commond))
            {
                output = commond.Handle(parameters);
                found = true;
            }

            if (!found)
            {
                output = $"未知命令: {command} {parameters}";
            }
        }

        
        
        
        
        
        
        
        internal static bool ExtractCommandAndParameters(string line,
            out string command, out string parameters)
        {
            line = line.Trim();
            command = string.Empty;
            parameters = string.Empty;

            if (line == string.Empty)
                return false;

            if (line[0] != '@') 
                return false;

            line = line.Substring(1);
            var separatorIndex = line.IndexOfAny(new[] { ' ', ',', ':' });
            command = separatorIndex < 0
                ? line
                : line.Substring(0, separatorIndex);

            // 命令名允许字母、数字、中文、下划线及点号，排除 @$$#表情/地图坐标等非命令前缀
            if (command.Length == 0 || !IsValidCommandName(command))
                return false;

            parameters = separatorIndex < 0
                ? string.Empty
                : line.Substring(separatorIndex + 1).Trim();
            return true;
        }

        /// <summary>
        /// 命令名合法性：只允许 Unicode 字母（含中文）、数字、下划线、点号。
        /// 排除 $  # ! ~ 等客户端频道前缀字符。
        /// </summary>
        private static bool IsValidCommandName(string name)
        {
            foreach (var c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                    return false;
            }
            return true;
        }

    }
}
