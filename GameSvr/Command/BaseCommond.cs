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
                if (GetEffectivePermission(playObject) < this.GameCommand.nPermissionMin)// 检查用户是否有权限来调用命令。
                {
                    return M2Share.g_sGameCommandPermissionTooLow;
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
