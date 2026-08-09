using System.Reflection;
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
            RegisterNativePlayerCommandAliases();
            RegisterCommandGroups();
        }

        private static void RegisterNativePlayerCommandAliases()
        {
            RegisterNativePlayerCommandAlias("UserMoveXY",
                M2Share.g_GameCommand.USERMOVE.sCmd, "UserMove");
            RegisterNativePlayerCommandAlias("SearchHuman",
                M2Share.g_GameCommand.SEARCHING.sCmd, "Searching");
        }

        private static void RegisterNativePlayerCommandAlias(string command,
            string configuredName, string defaultName)
        {
            if (CustomCommands.ContainsKey(command))
                return;

            var effectiveName = string.IsNullOrWhiteSpace(configuredName)
                ? defaultName
                : configuredName.Trim();
            if (effectiveName.StartsWith('@'))
                effectiveName = effectiveName[1..];
            if (!string.IsNullOrWhiteSpace(effectiveName))
                CustomCommands[command] = effectiveName;
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
            RegisterNativePlayerCommandAliases();

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
        }

        
        
        
        
        
        public void UpdataRegisterCommandGroups(GameCommandAttribute OldCmd, string sCmd)
        {
            if (CommandMaps.ContainsKey(OldCmd.Command))
            {
                
                
                
                
                

                
                
                
                
            }
        }

        
        
        
        
        
        
        public bool ExecCmd(string line, TPlayObject playObject)
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
                output = commond.Handle(parameters, playObject);
                found = true;
            }

            if (!found)
            {
                output = $"未知命令: {command} {parameters}";
            }

            
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

        [GameCommand("commands", "列出可用的命令")]
        public class CommandsCommandGroup : BaseCommond
        {
            public override string Fallback(string[] parameters = null, TPlayObject PlayObject = null)
            {
                var commandList = CommandMaps.Values
                    .Where(c => PlayObject == null ||
                                c.GameCommand.nPermissionMin <=
                                GetEffectivePermission(PlayObject))
                    .OrderBy(c => c.GameCommand.nPermissionMin)
                    .ThenBy(c => c.GameCommand.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var sb = new System.Text.StringBuilder();
                foreach (var cmd in commandList)
                {
                    sb.Append('@');
                    sb.Append(cmd.GameCommand.Name);
                    if (!string.IsNullOrWhiteSpace(cmd.GameCommand.Help))
                    {
                        sb.Append(' ');
                        sb.Append(cmd.GameCommand.Help);
                    }
                    sb.Append("\r\n");
                }
                return sb.Length > 0 ? sb.ToString().TrimEnd() : "暂无可用命令。";
            }
        }

        [GameCommand("help", "帮助命令")]
        public class HelpCommandGroup : BaseCommond
        {
            public override string Fallback(string[] parameters = null, TPlayObject PlayObject = null)
            {
                return "usage: help <command>";
            }

            public override string Handle(string parameters, TPlayObject PlayObject = null)
            {
                if (parameters == string.Empty)
                    return this.Fallback();
                var @params = parameters.Split(' ');
                var group = @params[0];
                var command = @params.Count() > 1 ? @params[1] : string.Empty;
                var output = $"Unknown command: {group} {command}";
                return output;
            }
        }
    }
}
