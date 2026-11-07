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
        [JsonProperty("power", Required = Required.Always)] public int Power;
        [JsonProperty("effect")] public RuleDto Effect;
        [JsonProperty("effect_duration_ms", Required = Required.DisallowNull)] public int? EffectDurationMs;
        [JsonProperty("field_duration_ms", Required = Required.DisallowNull)] public int? FieldDurationMs;
        [JsonProperty("cooldown_ms", Required = Required.Always)] public int CooldownMs;
        [JsonProperty("accuracy", Required = Required.Always)] public int Accuracy;
        [JsonProperty("range_class", Required = Required.Always)] public string RangeClass;
        [JsonProperty("shape", Required = Required.Always)] public string Shape;
        [JsonProperty("base_range_mm", Required = Required.Always)] public int BaseRangeMm;
        [JsonProperty("movement", Required = Required.DisallowNull)] public string Movement;
        [JsonProperty("origin", Required = Required.DisallowNull)] public string Origin;
    }

    internal sealed class TraitDto
    {
        [JsonProperty("id", Required = Required.Always)] public string Id;
        [JsonProperty("trigger", Required = Required.Always)] public RuleDto Trigger;
        [JsonProperty("effects", Required = Required.Always)] public List<RuleDto> Effects;
    }

    internal sealed class PlanetTypeDto
    {
        [JsonProperty("id", Required = Required.Always)] public string Id;
        [JsonProperty("size", Required = Required.Always)] public string Size;
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
        [JsonProperty("exp", Required = Required.Always)] public int Exp;
        [JsonProperty("materials", Required = Required.Always)] public List<MaterialDto> Materials;
    }

    internal sealed class StatsDto
    {
        [JsonProperty("con", Required = Required.Always)] public int Con;
        [JsonProperty("str", Required = Required.Always)] public int Str;
        [JsonProperty("spi", Required = Required.Always)] public int Spi;
        [JsonProperty("agi", Required = Required.Always)] public int Agi;
    }

    internal sealed class CreatureDto
    {
        [JsonProperty("id", Required = Required.Always)] public string Id;
        [JsonProperty("line_id", Required = Required.Always)] public string LineId;
        [JsonProperty("stage", Required = Required.Always)] public string Stage;
        [JsonProperty("size_class", Required = Required.Always)] public string SizeClass;
        [JsonProperty("ai_style", Required = Required.Always)] public string AiStyle;
        [JsonProperty("evolves_to", Required = Required.AllowNull)] public string EvolvesTo;
        [JsonProperty("evolution", Required = Required.AllowNull)] public EvolutionDto Evolution;
        [JsonProperty("resist", Required = Required.Always)] public Dictionary<string, int> Resist;
        [JsonProperty("mastery", Required = Required.Always)] public Dictionary<string, int> Mastery;
        [JsonProperty("stats", Required = Required.Always)] public StatsDto Stats;
        [JsonProperty("signature_move", Required = Required.Always)] public string SignatureMove;
        [JsonProperty("trait", Required = Required.Always)] public string Trait;
        [JsonProperty("concept", Required = Required.Always)] public JObject Concept;
    }

    internal sealed class EdgeDto
    {
        [JsonProperty("a", Required = Required.Always)] public string A;
        [JsonProperty("b", Required = Required.Always)] public string B;
        [JsonProperty("kind", Required = Required.Always)] public string Kind;
        [JsonProperty("note")] public string Note;
    }
}
