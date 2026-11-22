#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class DirectCombatPlayModeTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        private static RunSession SpawnTestRun(out RunOutcome outcome, out List<CreatureDef> party, System.Action<RunOutcome> onEndOverride = null)
        {
            var db = Load<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var copy = new DemoCopy("zh-TW",
                Load<TextAsset>(Copy + "strings.json").text,
                Load<TextAsset>(Copy + "names.json").text,
                Load<TextAsset>(Copy + "affixes.json").text,
                Load<TextAsset>(Copy + "region-themes.json").text,
                Load<TextAsset>(Copy + "quest-templates.json").text,
                Load<TextAsset>(Copy + "boss-epithets.json").text);
            CombatBalance balance = CombatBalance.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            Progression progression = Progression.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            var actions = Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions");

            var wild = new List<CreatureDef>();
            party = new List<CreatureDef>();
            PlanetTypeDef type = null;
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c)
                {
                    (c.LineId.StartsWith("starter_") ? party : wild).Add(c);
                }
                else if (d is PlanetTypeDef t && (type == null || t.Id == "verdant_planet"))
                {
                    type = t;
                }
            }

            party.RemoveAll(c => c.Stage != CreatureStage.Adult);
            RunOutcome captured = null;
            var session = new GameObject("DirectCombatTestRun").AddComponent<RunSession>();
            session.Configure(copy, balance, actions,
                Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                new RunSetup
                {
                    PlanetType = type,
                    Seed = 2026,
                    Radius = 320f,
                    BossStage = CreatureStage.Juvenile,
                    Party = party,
                    PartyIds = Enumerable.Range(1, party.Count).ToList(),
                    PartyExp = Enumerable.Repeat(progression.XpThreshold(progression.StarterLevel), party.Count).ToList(),
                    Progression = progression,
                    Materials = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text),
                    EnvMaterials = EnvMaterials.FromJson(Load<TextAsset>(Copy + "env-materials.json").text),
                },
                wild,
                o =>
                {
                    captured = o;
                    onEndOverride?.Invoke(o);
                },
                Load<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader"),
                Load<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader"),
                Load<Shader>("Assets/_Project/Art/Shaders/Stars.shader"));
            outcome = captured;
            return session;
        }

        private static IEnumerator WaitForLanding(RunSession session)
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!session.StartedForTests && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(session.StartedForTests, Is.True, "RunSession did not finish landing within 60 s");
        }

        private static void KnockOut(RunSession s, CombatBalance balance, int unitId)
        {
            long now = s.NowMsForTests;
            var mastery = new int[10];
            for (int i = 0; i < mastery.Length; i++)
            {
                mastery[i] = DamageModel.MaxMasteryPercent;
            }

            var rng = new Pcg32(99UL, 'T');
            for (int e = 0; e < 10 && !s.UnitForTests(unitId).IsFainted; e++)
            {
                var attacker = new Combatant(balance.Stats, balance.Effects, new CreatureStats(0, 0, 10000, 10000, 0, 0),
                    mastery, new int[10], new EffectBook(), 1);
                var move = new CastMove("test_nuke", (Element)e, MoveKind.Damage, DamageModel.MaxPower, 100, 0, false, default, 0);
                Assert.That(attacker.BeginCast(0, move, now, out CastContext cast), Is.EqualTo(CastResult.Accepted));
                for (int hit = 0; hit < 64 && !s.UnitForTests(unitId).IsFainted; hit++)
                {
                    s.ReceiveHostileForTests(unitId, cast, ref rng);
                }
            }

            Assert.That(s.UnitForTests(unitId).IsFainted, Is.True, "unit " + unitId + " taken to 0 HP");
            s.QueueFaintForTests(unitId);
        }

        private static IEnumerator WaitForCombat(RunSession session)
        {
            for (int i = 0; i < 60 && !session.InCombatForTests; i++)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitForCombatEnd(RunSession session)
        {
            for (int i = 0; i < 60 && session.InCombatForTests; i++)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitForTicks(RunSession session, int minTicks = 2)
        {
            long start = session.NowMsForTests;
            for (int i = 0; i < 60 && session.NowMsForTests < start + minTicks * 50L; i++)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Capture_is_blocked_during_combat_and_remains_available_past_20s_until_region_leave()
        {
            RunOutcome outcome = null;
            CombatBalance balance = CombatBalance.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            RunSession session = SpawnTestRun(out _, out _, o => outcome = o);
            try
            {
                yield return WaitForLanding(session);

                var wilds = new List<RunSession.UnitSnapshot>();
                foreach (RunSession.UnitSnapshot u in session.UnitsForTests)
                {
                    if (u.Side == EncounterSide.Wild && !u.IsFainted && u.Id != session.BossUnitForTests())
                    {
                        wilds.Add(u);
                    }
                }

                Assert.That(wilds.Count, Is.GreaterThanOrEqualTo(2), "need at least 2 non-boss wilds");
                RunSession.UnitSnapshot w0 = wilds[0];
                RunSession.UnitSnapshot w1 = wilds[1];

                // Move player close to w0 and provoke both wilds so combat remains active after w0 faints.
                float3 w0Dir = session.DirectionForTests(w0.Position);
                session.SuspendCameraForTests();
                session.PlaceOnCurrentGroundForTests(w0Dir);
                session.ProvokeForTests(w0.Id);
                session.ProvokeForTests(w1.Id);
                yield return WaitForCombat(session);

                Assert.That(session.InCombatForTests, Is.True, "both provoked wilds put team in combat");
                Assert.That(session.IsDirectControllingForTests, Is.True, "direct control activates in combat");

                // Knock out w0 while w1 keeps combat open.
                KnockOut(session, balance, w0.Id);
                yield return WaitForTicks(session);

                Assert.That(session.UnitForTests(w0.Id).IsFainted, Is.True, "w0 is fainted");
                Assert.That(session.InCombatForTests, Is.True, "w1 is still engaged so combat continues");

                session.TryCaptureForTests();
                Assert.That(session.IsWildGoneForTests(w0.Id), Is.False,
                    "cannot capture w0 while another wild keeps combat active (Q-C55)");

                // Knock out w1 so combat ends, then advance clock past the legacy 20s window.
                KnockOut(session, balance, w1.Id);
                yield return WaitForCombatEnd(session);

                Assert.That(session.InCombatForTests, Is.False, "combat ends after w1 faints");
                session.AdvanceNowMsForTests(35000);

                // Re-place player next to w0 to ensure reach <= 8m even if w1 was far.
                session.PlaceOnCurrentGroundForTests(w0Dir);
                yield return WaitForTicks(session);

                Assert.That(session.FindCaptureTargetForTests(), Is.EqualTo(w0.Id),
                    "knocked-out wild remains capturable past 20s while in the same region");
                session.TryCaptureForTests();
                Assert.That(session.IsWildGoneForTests(w0.Id), Is.True,
                    "capture attempt proceeds once out of combat");

                // Boss knockout stays on the field until captured or the player leaves the boss region.
                int bossId = session.BossUnitForTests();
                Assert.That(bossId, Is.GreaterThanOrEqualTo(0));
                float3 bossDir = session.DirectionForTests(session.UnitForTests(bossId).Position);
                int bossRegion = session.RegionAtForTests(bossDir);
                session.PlaceOnCurrentGroundForTests(bossDir);
                session.ProvokeForTests(bossId);
                yield return WaitForCombat(session);

                KnockOut(session, balance, bossId);
                yield return WaitForTicks(session);

                Assert.That(outcome, Is.Null, "defeating boss does not end run while player remains in boss region");

                // Move player to a different region; run settles as victory.
                float3 otherDir = bossDir;
                for (int i = 0; i < 8; i++)
                {
                    float3 cand = math.normalize(new float3(i == 0 ? 1f : -1f, (i % 3) - 1f, (i % 2) == 0 ? 1f : -1f));
                    if (session.RegionAtForTests(cand) != bossRegion)
                    {
                        otherDir = cand;
                        break;
                    }
                }

                Assert.That(session.RegionAtForTests(otherDir), Is.Not.EqualTo(bossRegion), "found a non-boss region");
                session.PlaceOnCurrentGroundForTests(otherDir);
                for (int i = 0; i < 60 && outcome == null; i++)
                {
                    yield return null;
                }

                Assert.That(outcome, Is.Not.Null, "leaving boss region after boss faint settles the run");
                Assert.That(outcome.Victory, Is.True);
            }
            finally
            {
                if (session != null) Object.DestroyImmediate(session.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator Return_to_player_recalls_creatures_and_keeps_combat_active()
        {
            RunSession session = SpawnTestRun(out _, out _);
            try
            {
                yield return WaitForLanding(session);

                int wildId = -1;
                foreach (RunSession.UnitSnapshot u in session.UnitsForTests)
                {
                    if (u.Side == EncounterSide.Wild && !u.IsFainted)
                    {
                        wildId = u.Id;
                        break;
                    }
                }

                Assert.That(wildId, Is.GreaterThanOrEqualTo(0));
                float3 wildDir = session.DirectionForTests(session.UnitForTests(wildId).Position);
                session.SuspendCameraForTests();
                session.PlaceOnCurrentGroundForTests(wildDir);
                session.ProvokeForTests(wildId);
                yield return WaitForCombat(session);

                Assert.That(session.InCombatForTests, Is.True);
                Assert.That(session.IsDirectControllingForTests, Is.True);
                Assert.That(session.ActiveUnitsForTests.Count, Is.GreaterThan(0));

                session.ReturnToPlayerForTests();
                yield return null;

                Assert.That(session.ReturnedToPlayerForTests, Is.True);
                Assert.That(session.IsDirectControllingForTests, Is.False, "direct control yields back to character");
                Assert.That(session.ActiveUnitsForTests.Count, Is.EqualTo(0), "all fielded creatures recalled");
                Assert.That(session.InCombatForTests, Is.True, "returning to player does not disengage while wild is within 40m");

                // Advancing frames does not auto-refill the field while returned to player.
                session.AdvanceNowMsForTests(5000);
                yield return WaitForTicks(session);
                Assert.That(session.ActiveUnitsForTests.Count, Is.EqualTo(0),
                    "RefillField respects ReturnedToPlayer until combat ends");
            }
            finally
            {
                if (session != null) Object.DestroyImmediate(session.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator Party_wipeout_settles_run_as_defeat_while_player_stays_unharmed()
        {
            RunOutcome outcome = null;
            CombatBalance balance = CombatBalance.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            RunSession session = SpawnTestRun(out _, out _, o => outcome = o);
            try
            {
                yield return WaitForLanding(session);

                int wildId = -1;
                foreach (RunSession.UnitSnapshot u in session.UnitsForTests)
                {
                    if (u.Side == EncounterSide.Wild && !u.IsFainted)
                    {
                        wildId = u.Id;
                        break;
                    }
                }

                Assert.That(wildId, Is.GreaterThanOrEqualTo(0));
                float3 wildDir = session.DirectionForTests(session.UnitForTests(wildId).Position);
                session.SuspendCameraForTests();
                session.PlaceOnCurrentGroundForTests(wildDir);
                session.ProvokeForTests(wildId);
                yield return WaitForCombat(session);

                Assert.That(session.IsDirectControllingForTests, Is.True);
                Assert.That(session.PlayerHpForTests, Is.EqualTo(session.PlayerMaxHpForTests));

                // Knock out fielded player units one by one as RefillField deploys reserves until the pool is wiped out.
                for (int round = 0; round < 12 && outcome == null; round++)
                {
                    IReadOnlyList<int> active = session.ActiveUnitsForTests;
                    if (active.Count > 0)
                    {
                        KnockOut(session, balance, active[0]);
                    }

                    yield return WaitForTicks(session);
                }

                Assert.That(session.PlayerHpForTests, Is.EqualTo(session.PlayerMaxHpForTests),
                    "player character takes no damage under direct control");
                Assert.That(outcome, Is.Not.Null, "party wipeout settles the run");
                Assert.That(outcome.Victory, Is.False, "party wipeout settles as defeat");
            }
            finally
            {
                if (session != null) Object.DestroyImmediate(session.gameObject);
            }
        }
    }
}
#endif
