namespace Cosmimera.GameData
{
    // Enums mirror Data/schemas; size classes retain uppercase labels, others use snake_case <-> PascalCase.
    // Values are explicit because CreatureDef stores resist as an array indexed by Element.

    /// <summary>The ten elements. REQ: docs/design/creatures.md#十大屬性 (order = common.schema.json).</summary>
    public enum Element
    {
        Metal = 0, Wood = 1, Water = 2, Fire = 3, Earth = 4, Ice = 5, Wind = 6, Thunder = 7, Light = 8, Shadow = 9,
    }

    /// <summary>Evolution stage of a creature form.</summary>
    public enum CreatureStage
    {
        Juvenile = 0, Adolescent = 1, Adult = 2,
    }

    /// <summary>Whether a move deals damage, applies an effect, or both.</summary>
    public enum MoveKind
    {
        Damage = 0, Effect = 1, Both = 2,
    }

    /// <summary>Coarse range band of a move.</summary>
    public enum RangeClass
    {
        Melee = 0, Mid = 1, Long = 2,
    }

    /// <summary>Hit shape of a move. Extend together with move.schema.json.</summary>
    /// <remarks>Pre: imported from the nine shape tokens. Post: stable serialized values. Side effects: none.</remarks>
    public enum MoveShape
    {
        // Retired Aoe=2 and Dash=3 cannot be reused by newly imported shapes.
        Line = 0, Cone = 1, Front = 4, Sweep = 5, Burst = 6, Arc = 7, Self = 8, Ally = 9, Field = 10,
    }

    /// <summary>Movement accompanying a move, independent of its hit shape.</summary>
    /// <remarks>Pre: imported from none|dash. Post: stable serialized values. Side effects: none.</remarks>
    public enum MoveMovement
    {
        None = 0, Dash = 1,
    }

    /// <summary>Where a burst is centered.</summary>
    /// <remarks>Pre: used only by burst moves. Post: stable serialized values. Side effects: none.</remarks>
    public enum BurstOrigin
    {
        Caster = 0, Target = 1,
    }

    /// <summary>Creature size for animation pairing and art budgets.</summary>
    /// <remarks>
    /// Pre: imported from XS|S|M|L|XL. Post: stable serialized values. Side effects: none.
    /// XS is appended as 4 instead of renumbered to 0 because imported assets store the integer; numeric order is
    /// therefore NOT size order — never compare these values with &lt; / &gt;.
    /// </remarks>
    public enum CreatureSizeClass
    {
        S = 0, M = 1, L = 2, XL = 3, XS = 4,
    }

    /// <summary>Combat AI positioning style. REQ: docs/design/combat.md#戰鬥-ai (Q-C21, closed enum).</summary>
    /// <remarks>Pre: imported from keep_distance|flank|charge|hold. Post: stable serialized values. Side effects: none.</remarks>
    public enum AiStyle
    {
        KeepDistance = 0, Flank = 1, Charge = 2, Hold = 3,
    }

    /// <summary>
    /// Species temperament: sets a wild creature's territory radius. REQ: docs/design/combat.md#野生領域 (D0-E2, closed
    /// enum; kinds provisional, Q-C35).
    /// </summary>
    /// <remarks>
    /// Pre: imported from timid|cautious|normal|territorial|fierce|apex. Post: stable serialized values; Normal is 0 so an asset imported before
    /// the field existed reads as the middle temperament rather than as one that never fights.
    /// Side effects: none.
    /// </remarks>
    public enum Temperament
    {
        Normal = 0, Timid = 1, Fierce = 2, Cautious = 3, Territorial = 4, Apex = 5,
    }

    /// <summary>Relation edge kind. REQ: docs/design/creatures.md#關係表 (closed enum).</summary>
    public enum RelationKind
    {
        EvolutionFamily = 0, MythKin = 1, RivalSymbiosis = 2, ToolKin = 3, Mentor = 4,
    }

    /// <summary>When a trait fires. REQ: common.schema.json#/$defs/trigger (closed in P2-01).</summary>
    public enum TriggerType
    {
        Always = 0, OnHpBelow = 1, OnHpAbove = 2, OnSummon = 3, OnHitTaken = 4, OnMoveUsed = 5, OnAllySwap = 6,
        // Appended, not inserted: values are serialized into imported .asset files.
        OnHpCrossBelow = 7,
    }

    /// <summary>What a trait effect or move effect does. REQ: common.schema.json#/$defs/effect (closed in P2-01).</summary>
    public enum EffectType
    {
        MasteryMod = 0, ResistMod = 1, CdModPercent = 2, EvasionModPercent = 3, MoveSpeedModPercent = 4,
        RangeModPercent = 5, HealPercentMaxHp = 6,
    }

    /// <summary>Planet size class: asteroid / moon / planet / supergiant = 320 / 640 / 960 / 1280 m. Numeric radius is not stored here.</summary>
    /// <remarks>REQ: docs/design/planet-world.md#星球尺度 — one size per creature form (juvenile, adolescent, adult, overload).</remarks>
    public enum PlanetSize
    {
        Asteroid = 0, Moon = 1, Planet = 2, Supergiant = 3,
    }
}
