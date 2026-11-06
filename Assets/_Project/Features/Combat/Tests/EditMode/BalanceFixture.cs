using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // Reads the real Data/balance.json so the golden-vector test proves the shipped coefficients, not a copy.
    internal static class BalanceFixture
    {
        public static string RepoPath(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(root, Path.Combine(parts));
        }

        public static JObject BalanceJson() => JObject.Parse(File.ReadAllText(RepoPath("Data", "balance.json")));

        public static DerivedStatsCoefficients Coefficients() => FromJson(BalanceJson());

        public static HitVolumeDefaults HitDefaults()
        {
            JToken h = BalanceJson()["hit_volume"];
            return new HitVolumeDefaults((int)h["height_mm"], (int)h["front_width_mm"], (int)h["sweep_angle_deg"],
                (int)h["cone_angle_deg"], (int)h["line_width_mm"], (int)h["arc_landing_radius_mm"],
                (int)h["field_radius_mm"], (int)h["target_cast_mid_mm"], (int)h["target_cast_long_mm"]);
        }

        public static EffectRulesConfig EffectConfig()
        {
            JObject b = BalanceJson();
            return new EffectRulesConfig((int)b["effects"]["multiplier_min_percent"],
                (int)b["effects"]["multiplier_max_percent"], (int)b["field"]["tick_ms"]);
        }

        public static int SwitchCooldownMs() => (int)BalanceJson()["team"]["switch_cooldown_ms"];

        public static PlayerVitalsConfig PlayerConfig()
        {
            JToken p = BalanceJson()["player"];
            return new PlayerVitalsConfig((int)p["max_hp"], (int)p["roll_duration_ms"],
                (int)p["roll_invulnerable_ms"], (int)p["roll_cooldown_ms"]);
        }

        public static DerivedStatsCoefficients FromJson(JObject b)
        {
            return new DerivedStatsCoefficients(
                (int)b["model_version"], (int)b["stat_effective_cap"],
                (int)b["hp"]["base"], (int)b["hp"]["gain"], (int)b["hp"]["half_stat"],
                (int)b["melee_volume"]["gain_permille"], (int)b["melee_volume"]["half_stat"],
                (int)b["ranged_distance"]["gain_permille"], (int)b["ranged_distance"]["half_stat"],
                (int)b["ranged_distance"]["cap_mm"],
                (int)b["cd"]["min_permille"], (int)b["cd"]["half_stat"], (int)b["cd"]["floor_ms"],
                (int)b["speed"]["base_mm_s"], (int)b["speed"]["gain_mm_s"], (int)b["speed"]["half_stat"],
                (int)b["evasion"]["cap_permille"], (int)b["evasion"]["half_stat"],
                (int)b["hit"]["floor_permille"]);
        }
    }
}
