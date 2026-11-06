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
    /// <remarks>Pre: imported from S|M|L|XL. Post: stable serialized values. Side effects: none.</remarks>
    public enum CreatureSizeClass
    {
        S = 0, M = 1, L = 2, XL = 3,
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
    }

    /// <summary>What a trait effect or move effect does. REQ: common.schema.json#/$defs/effect (closed in P2-01).</summary>
    public enum EffectType
    {
        MasteryMod = 0, ResistMod = 1, CdModPercent = 2, EvasionModPercent = 3, MoveSpeedModPercent = 4,
        RangeModPercent = 5, HealPercentMaxHp = 6,
    }

    /// <summary>Planet size class. Numeric radius belongs to P3 and is not stored here.</summary>
    public enum PlanetSize
    {
        Small = 0, Medium = 1, Large = 2,
    }
}
