using System.Collections.Generic;

namespace GameSvr
{
    /// <summary>
    /// Field partial for native <c>TWithChildMon</c> (VMT 0x0071A9xx band, instance
    /// size 1244). Its whole own band over TAnimal is 4 bytes — a single pointer —
    /// which is the spawned-child list every "with child" monster keeps. Native
    /// TBeeQueen and TSpiderHouseMon both add zero fields of their own (both are
    /// size 1244, equal to this class), so the list cannot live on either child;
    /// it belongs here and is shared, which is why it is declared on this layer.
    ///
    /// The structural shell (parent link, band comment) is generated into
    /// NativeClassSkeleton.cs; this file carries the field, per the "field work in
    /// a separate partial" split the generated header documents.
    /// </summary>
    public partial class TWithChildMon
    {
        /// <summary>The spawned-child list at the native +4 own slot.</summary>
        protected readonly IList<TBaseObject> m_ChildList = new List<TBaseObject>();
    }
}
