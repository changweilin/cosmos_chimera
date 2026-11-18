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
        Juvenile = 0, Adolescent = 1, Adult = 2, Overload = 3,
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
    /// Individual habit (習性): sets a wild creature's territory radius. Drawn per individual from its niche, never fixed
    /// per species. REQ: docs/design/creature-ecology.md#習性, combat.md#野生領域 (closed enum).
    /// </summary>
    /// <remarks>
    /// Pre: imported from avoidant|vigilant|tolerant|territorial|aggressive|dominant. Post: stable serialized values
    /// (kept from the former Temperament enum: Normal→Tolerant, Timid→Avoidant, Fierce→Aggressive, Cautious→Vigilant,
    /// Apex→Dominant), so assets and saves written before the rename keep their meaning.
    /// Side effects: none.
    /// </remarks>
    public enum Habit
    {
        Tolerant = 0, Avoidant = 1, Aggressive = 2, Vigilant = 3, Territorial = 4, Dominant = 5,
    }

    /// <summary>Idle wander pattern of a niche. REQ: docs/design/creature-ecology.md#四種遊蕩模式 (closed enum).</summary>
    /// <remarks>Pre: imported from shuttle|loop|bounded_random|unbounded_random. Post: stable values. Side effects: none.</remarks>
    public enum WanderMode
    {
        Shuttle = 0, Loop = 1, BoundedRandom = 2, UnboundedRandom = 3,
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

    /// <summary>Damage polarity of a move. REQ: docs/design/creatures.md#六大能力值 — yang = outside-in (cut, claw, burn),
    /// yin = inside-out (blunt, shock, radiation); picks the attack / defence pair in combat.md#傷害公式.</summary>
    public enum Polarity
    {
        Yin = 0, Yang = 1,
    }

    /// <summary>What a trait effect or move effect does. REQ: common.schema.json#/$defs/effect (closed in P2-01).</summary>
    public enum EffectType
    {
        MasteryMod = 0, ResistMod = 1, CdModPercent = 2, EvasionModPercent = 3, MoveSpeedModPercent = 4,
        RangeModPercent = 5, HealPercentMaxHp = 6,
        // Appended (serialized into imported .asset files). REQ: creatures.md#六大能力值, crafting.md#穿戴與數值 —
        // weapon / armor stats that only skills and equipment raise.
        AccuracyMod = 7, CritRateMod = 8, CritDamageMod = 9, StaggerCapacityModPercent = 10, BlockReductionMod = 11,
    }

    /// <summary>Planet size class: asteroid / moon / planet / supergiant = 320 / 640 / 960 / 1280 m. Numeric radius is not stored here.</summary>
    /// <remarks>REQ: docs/design/planet-world.md#星球尺度 — one size per creature form (juvenile, adolescent, adult, overload).</remarks>
    public enum PlanetSize
    {
        Asteroid = 0, Moon = 1, Planet = 2, Supergiant = 3,
    }

    /// <summary>Whether a <see cref="FieldUtility"/> is an active field move or a passive field trait.</summary>
    /// <remarks>Pre: none. Post: stable serialized values. Side effects: none.</remarks>
    public enum FieldUtilityKind
    {
        None = 0,
        Move = 1,
        Trait = 2,
    }

    /// <summary>
    /// Single out-of-combat life/field utility move or trait per creature form.
    /// REQ: common.schema.json#/$defs/fieldUtility.
    /// </summary>
    /// <remarks>Pre: imported from field_utility (default None). Post: stable serialized values. Side effects: none.</remarks>
    public enum FieldUtility
    {
        None = 0,
        ImmuneLaboratory = 1,
        ImmuneRainforest = 2,
        ImmuneDeepSea = 3,
        ImmuneVolcano = 4,
        ImmuneCave = 5,
        ImmuneIceCap = 6,
        ImmuneTower = 7,
        ImmuneSpace = 8,
        ImmuneLabyrinth = 9,
        ImmuneTemple = 10,
        OpenObstacle = 11,
        Dive = 12,
        Fly = 13,
        Levitate = 14,
        CatchRateUp = 15,
        StealItem = 16,
        PreventEscape = 17,
        QuickEscape = 18,
        DropRateUp = 19,
        ScoutPotential = 20,
        Mount = 21,
        ReturnVillage = 22,
        PartyStealth = 23,
        EggHatchBoost = 24,
        MagnetPickupDouble = 25,
        WeatherControl = 26,
        Fishing = 27,
        ForageBerries = 28,
        CookFire = 29,
        ProduceMilk = 30,
        CampfireRevive = 31,
        HuntGround = 32,
        HuntSky = 33,
        SetTrap = 34,
        CastNet = 35,
        HarvestCrops = 36,
        CulinaryPrep = 37,
    }

    /// <summary>Life-skill methods for acquiring raw food ingredients.</summary>
    /// <remarks>Pre: none. Post: stable serialized values. Side effects: none.</remarks>
    public enum FoodAcquisitionMethod
    {
        HuntGround = 0,
        HuntSky = 1,
        SetTrap = 2,
        Fishing = 3,
        CastNet = 4,
        ForageBerries = 5,
        HarvestCrops = 6,
        ProduceMilk = 7,
    }

    /// <summary>Culinary preparation methods for cooking ingredients into food.</summary>
    /// <remarks>Pre: none. Post: stable serialized values. Side effects: none.</remarks>
    public enum CookingMethod
    {
        Grill = 0,
        Stew = 1,
        StirFry = 2,
        DeepFry = 3,
        Steam = 4,
        ColdMix = 5,
        Grind = 6,
    }

    /// <summary>Broad culinary category of a raw food ingredient.</summary>
    /// <remarks>Pre: none. Post: stable serialized values. Side effects: none.</remarks>
    public enum IngredientCategory
    {
        MeatGround = 0,
        MeatSky = 1,
        Seafood = 2,
        BerryHerb = 3,
        GrainCrop = 4,
        Dairy = 5,
        SpiceOil = 6,
        Egg = 7,
    }

    /// <summary>Who receives the healing and buff effects when a food item is eaten.</summary>
    /// <remarks>Pre: none. Post: stable serialized values. Side effects: none.</remarks>
    public enum FoodTarget
    {
        Player = 0,
        ActiveCreatures = 1,
        Party = 2,
        PlayerAndParty = 3,
    }
}
