namespace GameSvr.CommandSystem
{
    [AttributeUsage(AttributeTargets.Class)]
    public class GameCommandAttribute : Attribute
    {
        
        
        
        public string Command { get; set; }

        
        
        
        public string Name { get; set; }

        
        
        
        public string Desc { get; set; }

        public string Help { get; set; }

        
        
        
        public byte nPermissionMin { get; private set; }

        
        
        
        public byte nPermissionMax { get; private set; }

        public GameCommandAttribute(string name, string desc, byte minUserLevel = 0, byte maxUserLevel = 10)
        {
            this.Name = name;
            this.Desc = desc;
            this.nPermissionMin = minUserLevel;
            this.nPermissionMax = maxUserLevel;
        }

        public GameCommandAttribute(string name, string desc, string help, byte minUserLevel = 0, byte maxUserLevel = 10)
        {
            this.Name = name;
            this.Desc = desc;
            this.Help = help;
            this.nPermissionMin = minUserLevel;
            this.nPermissionMax = maxUserLevel;
        }

        public string ShowHelp => $"命令格式: @{Name} {Help}";
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class CommandAttribute : Attribute
    {
        
        
        
        public string Name { get; private set; }

        
        
        
        public string Desc { get; private set; }

        
        
        
        public string Help { get; private set; }

        
        
        
        public byte MinUserLevel { get; private set; }

        public CommandAttribute(string command, string desc, string help, byte minUserLevel = 0)
        {
            this.Name = command;
            this.Desc = desc;
            this.Help = help;
            this.MinUserLevel = minUserLevel;
        }
    }

    
    
    
    
    
    
    [AttributeUsage(AttributeTargets.Method)]
    public class DefaultCommand : CommandAttribute
    {
        public DefaultCommand(byte minUserLevel = 0) : base("", "", "", minUserLevel)
        {

        }
    }
}