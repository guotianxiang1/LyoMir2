namespace GameSvr
{
    /// <summary>
    /// Native <c>THumanKind</c>, VMT 0x0073BC34, instance size 1580.
    ///
    /// ── Why this layer has to exist
    /// The two humanoid actors are siblings under it natively:
    /// <code>
    ///   TObject -> TBaseObj -> TCreature 1104 -> THumanKind 1580 -> TPlayer  6472
    ///                                                            -> THeroAct 1756
    /// </code>
    /// The port had no equivalent, so every field the two humanoids share was
    /// declared on TPlayObject and the hero simply did without. That is what makes
    /// the hero legs of CM 4123/4124/4126 unimplementable today: the soul-wash
    /// window they read is a THumanKind field, not a TPlayer one.
    ///
    /// ── How field ownership is decided
    /// A class's own fields occupy exactly
    /// <c>[instance_size(parent), instance_size(self))</c>, and those bands do not
    /// overlap, so any offset recovered from the image is attributable with no
    /// judgement call:
    /// <code>
    ///   [0x00C, 0x450)  TCreature       [0x62C, 0x1948)  TPlayer
    ///   [0x450, 0x62C)  THumanKind      [0x62C, 0x6DC)   THeroAct
    /// </code>
    /// Known offsets that land in this class's band, i.e. that belong here rather
    /// than on TPlayObject: the equipment container pointer <c>+0x4C0</c>, and the
    /// soul-wash window <c>+0x59C</c> cap / <c>+0x5A0</c> base / <c>+0x5A4</c>
    /// current / <c>+0x5A8</c> word[10] slots / <c>+0x5BC</c> slot count /
    /// <c>+0x610</c> prereq. The hero pointer <c>+0xBB0</c> lands in TPlayer's
    /// band, which is why only players own a hero.
    ///
    /// ── State of the migration
    /// The native parent edge is now corrected: THumanKind derives from the
    /// TCreature port (TBaseObject), and the two humanoids no longer inherit the
    /// AnimalObject layer. Remaining equipment/cold-time storage work is tracked
    /// separately and must be moved only after each flattened method has an owner
    /// proof. The former player/hero compensation arm of
    /// <see cref="AnimalObject.IsNativeMagic43Target"/> was removed with this cut.
    ///
    /// <see cref="WalkToInBounds"/> was previously listed here as a second such
    /// compensation. It is not: the VMT shows THumanKind genuinely overriding the
    /// mover slot, so the override is real native behaviour and stays. What was
    /// wrong is only that the port wrote it twice — see the override below.
    /// </summary>
    public partial class THumanKind : TBaseObject
    {
        /// <summary>
        /// obj+0x5A4, the 24-byte soul-wash window, held as raw bytes because
        /// native moves it verbatim and never marshals its fields. 0x5A4 falls in
        /// this class's band, so it is shared by player and hero rather than owned
        /// by the player, which is the whole reason the layer exists.
        /// </summary>
        public byte[] m_NativeShenYouBlock;

        /// <summary>
        /// Native VMT slot <c>+0x1F0</c>: THumanKind overrides the TCreature slot
        /// with <c>sub_748130</c>. TPlayer and THeroAct inherit that same target;
        /// neither leaf declares another override.
        /// </summary>
        internal override bool SupportsNativeColdTime => true;

        /// <summary>
        /// The humanoid mover, VMT slot <c>+0x030</c>. This class is where native
        /// overrides it, and both humanoids inherit the override rather than
        /// declaring it:
        /// <code>
        ///   TCreature   +0x030 = 0x767568
        ///   TAnimal     +0x030 = 0x71F0F4   (monster, loose bounds)
        ///   THumanKind  +0x030 = 0x741224   &lt;-- overridden here
        ///   TPlayer     +0x030 = 0x741224   inherited
        ///   THeroAct    +0x030 = 0x741224   inherited
        /// </code>
        /// The humanoid bounds are strict on both edges (0x741276 <c>jle</c>,
        /// 0x741284 <c>jge</c>), so a humanoid cannot stand on row/column 0 the way
        /// a monster can. The port had this same body copied onto TPlayObject and
        /// HeroObject; one override on the class that actually owns the slot is the
        /// faithful shape.
        ///
        /// TFieldHero is deliberately unaffected: natively it descends from TAIMon
        /// with <c>+0x030 = 0x71F0F4</c>, and in C# it is <c>TFieldHero : AiMon</c>,
        /// outside this subtree, so it keeps the monster bounds correctly. The
        /// THeroAct subclasses that do sit under this layer — TWarHero, TTaosHero,
        /// TMagHero and their TSec* variants — all carry 0x741224 too.
        /// </summary>
        protected override bool WalkToInBounds(short nNX, short nNY)
        {
            return nNX > 0 && nNX < m_PEnvir.wWidth
                && nNY > 0 && nNY < m_PEnvir.wHeight;
        }

        public THumanKind() : base()
        {
        }
    }
}
