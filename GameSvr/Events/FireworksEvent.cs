using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>TFireworksEvent</c>. Its constructor at <c>0x719828</c>
    /// always passes event type <c>0x17</c> and visible=true to
    /// <c>TMapEvent</c>.
    /// </summary>
    internal class FireworksEvent : Event
    {
        internal FireworksEvent(Envirnoment envir, int x, int y,
            int duration, string text, byte[] rawText)
            : base(envir, x, y, Grobal2.ET_YANHUA_TEXT, duration, true,
                text, rawText)
        {
        }
    }
}
