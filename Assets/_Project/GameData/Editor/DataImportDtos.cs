using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cosmimera.GameData.Editor
{
    // JSON shapes of Data/schemas/*.schema.json. Enum-valued fields stay strings here so the importer
    // reports an unknown value with file + id instead of a bare converter exception.
    // Required.Always / AllowNull mirror each schema's "required" list; unknown members are rejected by
    // the serializer settings, matching additionalProperties: false.

    internal sealed class RuleDto
    {
        [JsonProperty("type", Required = Required.Always)] public string Type;
        [JsonProperty("params", Required = Required.DisallowNull)] public JObject Params;
        // Effects only (common.schema.json#/$defs/effect); the importer rejects it on triggers.
        [JsonProperty("max_stacks", Required = Required.DisallowNull)] public int? MaxStacks;
    }

    internal sealed class MoveDto
    {
        [JsonProperty("id", Required = Required.Always)] public string Id;
        [JsonProperty("element", Required = Required.Always)] public string Element;
        [JsonProperty("kind", Required = Required.Always)] public string Kind;
        [JsonProperty("star", Required = Required.Always)] public int Star;
        [JsonProperty("polarity", Required = Required.DisallowNull)] public string Polarity;
        [JsonProperty("downgraded", Required = Required.DisallowNull)] public MoveDowngradedDto Downgraded;
        [JsonProperty("power", Required = Required.Always)] public int Power;
        [JsonProperty("effect")] public RuleDto Effect;
        [JsonProperty("effect_duration_ms", Required = Required.DisallowNull)] public int? EffectDurationMs;
        [JsonProperty("field_duration_ms", Required = Required.DisallowNull)] public int? FieldDurationMs;
        [JsonProperty("cooldown_ms", Required = Required.Always)] public int CooldownMs;
        [JsonProperty("windup_ms", Required = Required.Always)] public int WindupMs;
        [JsonProperty("accuracy", Required = Required.Always)] public int Accuracy;
        [JsonProperty("range_class", Required = Required.Always)] public string RangeClass;
        [JsonProperty("shape", Required = Required.Always)] public string Shape;
        [JsonProperty("base_range_mm", Required = Required.Always)] public int BaseRangeMm;
        [JsonProperty("movement", Required = Required.DisallowNull)] public string Movement;
        [JsonProperty("origin", Required = Required.DisallowNull)] public string Origin;
    }

    internal sealed class MoveDowngradedDto
    {
        [JsonProperty("power", Required = Required.DisallowNull)] public int? Power;
        [JsonProperty("cooldown_ms", Required = Required.DisallowNull)] public int? CooldownMs;
        [JsonProperty("accuracy", Required = Required.DisallowNull)] public int? Accuracy;
        [JsonProperty("effect", Required = Required.DisallowNull)] public RuleDto Effect;
        [JsonProperty("effect_duration_ms", Required = Required.DisallowNull)] public int? EffectDurationMs;
    }

    internal sealed class TraitDowngradedDto
    {
        [JsonProperty("effects", Required = Required.Always)] public List<RuleDto> Effects;
    }

    internal sealed class TraitDto
    {
        [JsonProperty("id", Required = Required.Always)] public string Id;
        [JsonProperty("star", Required = Required.Always)] public int Star;
        [JsonProperty("downgraded", Required = Required.DisallowNull)] public TraitDowngradedDto Downgraded;
        [JsonProperty("trigger", Required = Required.Always)] public RuleDto Trigger;
        [JsonProperty("effects", Required = Required.Always)] public List<RuleDto> Effects;
    }

    internal sealed class PlanetTypeDto
    {
        [JsonProperty("id", Required = Required.Always)] public string Id;
        [JsonProperty("terrain_tags", Required = Required.Always)] public List<string> TerrainTags;
        [JsonProperty("element_bias", Required = Required.Always)] public List<string> ElementBias;
    }

    internal sealed class MaterialDto
    {
        [JsonProperty("element", Required = Required.Always)] public string Element;
        [JsonProperty("tier", Required = Required.Always)] public int Tier;
        [JsonProperty("qty", Required = Required.Always)] public int Qty;
    }

    internal sealed class EvolutionDto
    {
        [JsonProperty("level", Required = Required.Always)] public int Level;
        [JsonProperty("materials", Required = Required.Always)] public List<MaterialDto> Materials;
    }

    internal sealed class StatsDto
    {
        [JsonProperty("hp", Required = Required.Always)] public int Hp;
        [JsonProperty("speed", Required = Required.Always)] public int Speed;
        [JsonProperty("yin_atk", Required = Required.Always)] public int YinAtk;
        [JsonProperty("yang_atk", Required = Required.Always)] public int YangAtk;
        [JsonProperty("yin_def", Required = Required.Always)] public int YinDef;
        [JsonProperty("yang_def", Required = Required.Always)] public int YangDef;
    }

    internal sealed class CreatureDto
    {
        [JsonProperty("id", Required = Required.Always)] public string Id;
        [JsonProperty("line_id", Required = Required.Always)] public string LineId;
        [JsonProperty("stage", Required = Required.Always)] public string Stage;
        [JsonProperty("overload_of")] public string OverloadOf;
        [JsonProperty("size_class", Required = Required.Always)] public string SizeClass;
        [JsonProperty("ai_style", Required = Required.Always)] public string AiStyle;
        [JsonProperty("niches", Required = Required.Always)] public List<NicheCandidateDto> Niches;
        [JsonProperty("evolves_to", Required = Required.AllowNull)] public string EvolvesTo;
        [JsonProperty("evolution", Required = Required.AllowNull)] public EvolutionDto Evolution;
        [JsonProperty("resist", Required = Required.Always)] public Dictionary<string, int> Resist;
        [JsonProperty("mastery", Required = Required.Always)] public Dictionary<string, int> Mastery;
        [JsonProperty("stats", Required = Required.Always)] public StatsDto Stats;
        [JsonProperty("signature_move", Required = Required.Always)] public string SignatureMove;
        [JsonProperty("trait", Required = Required.Always)] public string Trait;
        [JsonProperty("field_utility", Required = Required.DisallowNull)] public string FieldUtility;
        [JsonProperty("concept", Required = Required.Always)] public JObject Concept;
    }

    internal sealed class NicheCandidateDto
    {
        [JsonProperty("niche", Required = Required.Always)] public string Niche;
        [JsonProperty("weight", Required = Required.Always)] public int Weight;
    }

    internal sealed class DistributionDto
    {
        [JsonProperty("mean", Required = Required.Always)] public int Mean;
        [JsonProperty("individual_sd", Required = Required.Always)] public int IndividualSd;
        [JsonProperty("action_sd", Required = Required.Always)] public int ActionSd;
        [JsonProperty("min", Required = Required.Always)] public int Min;
        [JsonProperty("max", Required = Required.Always)] public int Max;
    }

    internal sealed class WanderDto
    {
        [JsonProperty("mode", Required = Required.Always)] public string Mode;
        [JsonProperty("radius_mm", Required = Required.AllowNull)] public int? RadiusMm;
        [JsonProperty("exclusion_radius_mm")] public int? ExclusionRadiusMm;
        [JsonProperty("speed_permille", Required = Required.Always)] public DistributionDto SpeedPermille;
        [JsonProperty("move_ms", Required = Required.Always)] public DistributionDto MoveMs;
        [JsonProperty("pause_ms", Required = Required.Always)] public DistributionDto PauseMs;
    }

    internal sealed class NicheDto
    {
        [JsonProperty("id", Required = Required.Always)] public string Id;
        [JsonProperty("name_zh", Required = Required.Always)] public string NameZh;
        [JsonProperty("resource_zh", Required = Required.Always)] public string ResourceZh;
        [JsonProperty("trophic_tier", Required = Required.Always)] public int TrophicTier;
        [JsonProperty("trophic_group", Required = Required.Always)] public string TrophicGroup;
        [JsonProperty("habitat_kinds", Required = Required.Always)] public List<string> HabitatKinds;
        // Key order is not meaningful; Write sorts by habit value so asset YAML does not churn.
        [JsonProperty("habit_weights", Required = Required.Always)] public Dictionary<string, int> HabitWeights;
        [JsonProperty("wander", Required = Required.Always)] public WanderDto Wander;
    }

    internal sealed class EdgeDto
    {
        [JsonProperty("a", Required = Required.Always)] public string A;
        [JsonProperty("b", Required = Required.Always)] public string B;
        [JsonProperty("kind", Required = Required.Always)] public string Kind;
        [JsonProperty("note")] public string Note;
    }
}
