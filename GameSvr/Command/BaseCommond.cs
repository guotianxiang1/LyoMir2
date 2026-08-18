using GameSvr.CommandSystem;
using System.Reflection;
using SystemModule;

namespace GameSvr
{
    public class BaseCommond
    {
        public GameCommandAttribute GameCommand { get; private set; }

        private MethodInfo CommandMethod { get; set; }

        private int MethodParameterCount { get; set; }

        
        
        
        public void Register(GameCommandAttribute attributes, MethodInfo method)
        {
            this.GameCommand = attributes;
            this.CommandMethod = method;
            this.MethodParameterCount = method.GetParameters().Length;
        }

        
        
        
        
        
        
        public virtual string Handle(string parameters, TPlayObject playObject = null)
        {
            if (playObject != null)
            {
                // Native lookup sub_621F28 @0x00621F83 `cmp eax,[edx+0x1C]` / `jl 0x621F8E`:
                // caller < required → index 0. Fail reply only at 0x00622AB9
                // `cmp bl,3 / jb 0x622B09` then `cmp [required],0 / jbe 0x622B09`,
                // else concat "该命令需要"(0x0062B768 len=10) + IntToStr(N) +
                // "级GM才能使用"(0x0062B77C len=12), SysMsg cx=0x38FF.
                // "权限不够!!!" is 0 hits in the image.
                var caller = GetEffectivePermission(playObject);
                var required = this.GameCommand.nPermissionMin;
                if (caller < required)
                {
                    if (caller < 3)
                        return string.Empty;
                    return "该命令需要" + required + "级GM才能使用";
                }
            }
            switch (MethodParameterCount)
            {
                case 2:
                    {
                        var @params = parameters.Split(new[] { ' ', ',', ':' },
                            StringSplitOptions.RemoveEmptyEntries);
                        return (string)CommandMethod.Invoke(this, new object[] { @params, playObject });
                    }
                case 1:
                    return (string)CommandMethod.Invoke(this, new object[] { playObject });
                default:
                    return (string)CommandMethod.Invoke(this, new object[] { null, playObject });
            }
        }

        internal virtual string HandleRaw(string rawLine, string parameters,
            byte[] rawPayload, int bodyLength, TPlayObject playObject)
        {
            return Handle(parameters, playObject);
        }

        internal static byte GetEffectivePermission(TPlayObject playObject)
        {
            if (playObject == null)
                return 0;
            return M2Share.g_Config?.boTestServer == true &&
                   playObject.m_btPermission == 4
                ? (byte)5
                : playObject.m_btPermission;
        }

        
        
        
        
        
        
        [DefaultCommand]
        public virtual string Fallback(string[] @params = null, TPlayObject PlayObject = null)
        {
            return string.Empty;
        }
    }
}
