#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cosmimera.Combat;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.EditorTools
{
    /// <summary>Measures D0 starter matchups using the shipped Encounter and imported creature data.</summary>
    /// <remarks>
    /// Pre: run in batchmode with imported D0 data. Post: reports wins, losses and timeouts separately.
    /// Side effects: simulates fights and writes a JSON report; never changes balance or player saves.
    /// </remarks>
    public static class DemoBalanceProbe
    {
        private const int TickMs = 50;
        private const int MaxTicks = 12000;
        private const int Trials = 32;

        private sealed class FlatSurface : IEncounterSurface
        {
            /// <summary>Returns sea-level ground for isolated matchups.</summary>
            /// <remarks>Pre: direction unit length. Post: zero. Side effects: none.</remarks>
            public float GroundAltitude(float3 direction) => 0f;

            /// <summary>Isolated matchups have no sea.</summary>
            /// <remarks>Pre: direction unit length. Post: false. Side effects: none.</remarks>
            public bool IsSea(float3 direction) => false;
        }

        /// <summary>Batchmode entry that writes Logs/d0-balance.json and exits with an explicit status.</summary>
        /// <remarks>
        /// Pre: batchmode; D0 database and balance assets imported; no other Editor owns the project.
        /// Post: exit 0 only after every matchup is recorded; exit 1 on missing data or simulation errors.
        /// Side effects: allocates encounter objects, writes the report, exits the Editor.
        /// </remarks>
        public static void RunBatch()
        {
            Cosmimera.Core.Contracts.Contract.Requires(Application.isBatchMode, "DemoBalanceProbe requires batchmode");

            int code = 1;
            try
            {
                var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
                var data = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/Copy/balance.json");
                if (db == null || data == null)
                {
                    throw new InvalidOperationException("Import D0 data before running the balance probe");
                }

                CombatBalance balance = CombatBalance.FromJson(data.text);
                Progression progression = Progression.FromJson(data.text);
                List<CreatureDef> forms = db.All.OfType<CreatureDef>().OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
                List<CreatureDef> starters = forms.Where(c => c.LineId.StartsWith("starter_", StringComparison.Ordinal)
                    && c.Stage == CreatureStage.Juvenile).ToList();
                List<CreatureDef> wild = forms.Where(c => !c.LineId.StartsWith("starter_", StringComparison.Ordinal)).ToList();
                if (starters.Count != 3 || wild.Count == 0)
                {
                    throw new InvalidOperationException("Expected three juvenile starters and a nonempty wild pool");
                }

                var rows = new JArray();
                foreach (CreatureDef starter in starters)
                {
                    foreach (CreatureDef enemy in wild)
                    {
                        if (enemy.Stage == CreatureStage.Juvenile)
                        {
                            rows.Add(Matchup(balance, progression, starter, enemy, SpawnRole.Normal, 0, 1));
                            rows.Add(Matchup(balance, progression, starter, enemy, SpawnRole.Normal, 0, 2));
                        }
                        else if (enemy.Stage == CreatureStage.Adolescent)
                        {
                            rows.Add(Matchup(balance, progression, starter, enemy, SpawnRole.Elite, 0, 1));
                        }
                        else if (enemy.Stage == CreatureStage.Adult)
                        {
                            rows.Add(Matchup(balance, progression, starter, enemy, SpawnRole.Boss, 2, 1));
                        }
                    }
                }

                var report = new JObject
                {
                    ["version"] = 1,
                    ["trials_per_matchup"] = Trials,
                    ["tick_ms"] = TickMs,
                    ["timeout_ms"] = MaxTicks * TickMs,
                    ["starter_level"] = progression.StarterLevel,
                    ["difficulty"] = 1,
                    ["conditions"] = "R=640; flat terrain; fresh HP each fight; wild inserted before player as in RunSession; " +
                        "Free command; player stationary 4 m behind starter; enemy 10 m ahead; pack members 2 m apart; seeds 0..31",
                    ["limits"] = "Isolated fights, not a full run: no XP growth, capture recruits, attrition, affixes, " +
                        "terrain, manual commands or sidekicks. Stage/level combinations are scenario samples, not spawn frequencies. " +
                        "Only three starters; this does not establish ten-element balance or playthrough acceptance.",
                    ["matchups"] = rows,
                };
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/d0-balance.json", report.ToString());
                Debug.Log("DemoBalanceProbe: " + rows.Count + " matchups, " + rows.Count * Trials + " fights; Logs/d0-balance.json");
                code = 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                EditorApplication.Exit(code);
            }
        }

        private static JObject Matchup(CombatBalance balance, Progression progression, CreatureDef starter,
            CreatureDef enemy, SpawnRole role, int tier, int enemies)
        {
            var planet = new PlanetConfig(640f, float3.zero);
            var surface = new FlatSurface();
            var free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
            int level = progression.WildLevel(1, tier, role);
            int wins = 0;
            int losses = 0;
            var durations = new JArray();
            var remainingHp = new JArray();
            var timeouts = new JArray();
            for (ulong seed = 0; seed < Trials; seed++)
            {
                var encounter = new Encounter(planet, balance, surface, seed);
                for (int i = 0; i < enemies; i++)
                {
                    encounter.AddCreature(EncounterSide.Wild, enemy, null, Direction(10f + i * 2f, planet.Radius), level);
                }

                EncounterUnit mine = encounter.AddCreature(EncounterSide.Player, starter, null, Direction(0f, planet.Radius), progression.StarterLevel);
                encounter.Field(mine.Id, Direction(0f, planet.Radius), false, 0);
                bool finished = false;
                long lastHitMs = -1;
                for (int tick = 1; tick <= MaxTicks; tick++)
                {
                    encounter.Tick(tick * (long)TickMs, TickMs, planet.ToWorld(Direction(-4f, planet.Radius), 1f), free, mine.Id);
                    foreach (EncounterEvent ev in encounter.Events)
                    {
                        if (ev.Kind == EncounterEventKind.Hit)
                        {
                            lastHitMs = tick * (long)TickMs;
                        }
                    }
                    bool defeated = true;
                    for (int i = 0; i < enemies; i++)
                    {
                        defeated &= encounter.Units[i].Combatant.IsFainted;
                    }

                    if (!mine.Combatant.IsFainted && !defeated)
                    {
                        continue;
                    }

                    // RunSession checks party wipe before boss victory, so a mutual knockout is a loss.
                    bool won = !mine.Combatant.IsFainted && defeated;
                    wins += won ? 1 : 0;
                    losses += won ? 0 : 1;
                    durations.Add(tick * TickMs);
                    remainingHp.Add(mine.Combatant.Hp);
                    finished = true;
                    break;
                }

                if (!finished)
                {
                    durations.Add(MaxTicks * TickMs);
                    remainingHp.Add(mine.Combatant.Hp);
                    timeouts.Add(new JObject
                    {
                        ["seed"] = seed, ["last_hit_ms"] = lastHitMs,
                        ["player_hp"] = mine.Combatant.Hp,
                        ["enemy_hp"] = encounter.Units[0].Combatant.Hp,
                        ["distance_m"] = math.distance(mine.Position, encounter.Units[0].Position),
                        ["enemy_engaged"] = encounter.Units[0].Engaged,
                    });
                }
            }

            return new JObject
            {
                ["starter"] = starter.Id, ["enemy"] = enemy.Id, ["role"] = role.ToString(),
                ["tier"] = tier, ["enemy_level"] = level, ["enemy_count"] = enemies,
                ["wins"] = wins, ["losses"] = losses, ["timeouts"] = Trials - wins - losses,
                ["duration_ms"] = durations, ["remaining_hp"] = remainingHp,
                ["timeout_details"] = timeouts,
            };
        }

        private static float3 Direction(float meters, float radius) =>
            math.normalize(new float3(math.sin(meters / radius), math.cos(meters / radius), 0f));
    }
}
#endif
