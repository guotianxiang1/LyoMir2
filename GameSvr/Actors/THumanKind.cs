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
    /// This layer is introduced empty and still derives from AnimalObject, which is
    /// where the two humanoids sat before. Natively THumanKind derives from
    /// TCreature and has nothing to do with TAnimal; re-parenting it needs each
    /// AnimalObject member the humanoids currently rely on to be resolved against
    /// the image first, since some are real TCreature members and some only exist
    /// because of the wrong ancestry. Two known compensations for that wrong
    /// ancestry, both to be removed once the parent link is corrected, are
    /// <see cref="AnimalObject.WalkToInBounds"/> — which the humanoids each have to
    /// override back to the tighter humanoid bounds — and the
    /// <c>this is TPlayObject || this is HeroObject</c> arm of
    /// <see cref="AnimalObject.IsNativeMagic43Target"/>.
    ///
    /// Inserting the layer changes no behaviour on its own: it declares nothing and
    /// forwards construction, so both humanoids resolve exactly the members they
    /// resolved before.
    /// </summary>
    public partial class THumanKind : AnimalObject
    {
        public THumanKind() : base()
        {
        }
    }
}
