namespace Cosmimera.Creatures
{
    /// <summary>The capture-item category an individual was caught with. REQ: docs/design/lobby-meta.md#收服道具.</summary>
    /// <remarks>
    /// Pre: none. Post: stable serialized values (save files store these ints; never renumber).
    /// Side effects: none.
    /// </remarks>
    public enum VesselKind
    {
        /// <summary>符籙卡片 (<c>talisman_card</c>): as a sub-partner provides one move.</summary>
        TalismanCard = 0,

        /// <summary>琉璃珠 (<c>glass_bead</c>): as a sub-partner provides its trait.</summary>
        GlassBead = 1,

        /// <summary>魔術方塊 (<c>magic_cube</c>): as a sub-partner unlocks the main's own overload.</summary>
        MagicCube = 2,
    }
}
