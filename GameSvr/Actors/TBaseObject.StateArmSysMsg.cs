using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Shared helper for the state arms that call <c>[ebx+0xD4]</c> with a packed
    /// <c>cx</c> instead of going through the broadcast path. Split out of
    /// TBaseObject.TimedAbility.cs so batch arm files can land without touching
    /// that hot spot.
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>
        /// The arms load a single 16-bit immediate (<c>66 B9 DB FF mov cx,0xFFDB</c>)
        /// and pass it straight to the virtual at <c>[ebx+0xD4]</c> = 0x73C8F4, whose
        /// prologue splits it: <c>0FB6C1 movzx eax,cl</c> is the colour and
        /// <c>0FB7C1 / C1E8 08</c> the message type. So 0xFFDB means colour 0xDB,
        /// type 0xFF.
        /// <para>
        /// A hero has no socket of its own; its message pump relays to the master
        /// and prefixes the literal at 0x6899B4 first
        /// (<c>0x689680 BA B4 99 68 00 mov edx,0x6899B4 / 0x689685 call 0x40581C</c>).
        /// </para>
        /// </summary>
        private void SendNativeStateSysMsg(int nativeCx, string text)
        {
            // 屏蔽属性提升提示：31×VA 0x741A21..0x74298C jmp 跳过等价。
            if (Plugins.YanshenPangu1Patches.ShouldSuppressAttrUpHint(text))
                return;
            var colour = nativeCx & 0xFF;
            var type = (nativeCx >> 8) & 0xFF;
            if (this is HeroObject hero)
            {
                if (hero.m_Master is TPlayObject master)
                {
                    master.SendMsg(hero, Grobal2.RM_SYSMESSAGE, 0,
                        colour, type, 0, "(英雄) " + text);
                }
            }
            else if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                    colour, type, 0, text);
            }
        }

        /// <summary>
        /// Seconds as the arms render it: <c>0F B7 C7 movzx eax, di</c> takes only
        /// the low word of the already truncated <c>[node+2]/1000</c>, then
        /// @LStrCatN 0x405890 joins prefix + that string + suffix.
        /// </summary>
        private static string NativeStateSeconds(int remainingMilliseconds)
        {
            return unchecked((ushort)(remainingMilliseconds / 1000)).ToString();
        }
    }
}
