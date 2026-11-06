namespace Cosmimera.GameData
{
    // Each enum mirrors a JSON enum in Data/schemas; the importer maps snake_case <-> PascalCase by name.
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
    public enum MoveShape
    {
        Line = 0, Cone = 1, Aoe = 2, Dash = 3,
    }

    /// <summary>Relation edge kind. REQ: docs/design/creatures.md#關係表 (closed enum).</summary>
    public enum RelationKind
    {
        EvolutionFamily = 0, MythKin = 1, RivalSymbiosis = 2, ToolKin = 3, Mentor = 4,
    }

    /// <summary>Planet size class. Numeric radius belongs to P3 and is not stored here.</summary>
    public enum PlanetSize
    {
        Small = 0, Medium = 1, Large = 2,
    }
}
