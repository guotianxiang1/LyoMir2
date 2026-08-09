using GameSvr.Services;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal void QueryNativeAwardCode(string code)
        {
            NativeAwardCodeService.EnqueueQuery(this, code);
        }

        internal void SetNativeAwardCodeActiveParam(string code,
            int activeParam)
        {
            NativeAwardCodeService.EnqueueSetActiveParam(
                this, code, activeParam);
        }
    }
}
