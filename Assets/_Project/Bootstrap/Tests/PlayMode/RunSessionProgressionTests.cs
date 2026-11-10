#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.Save;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    /// <summary>
    /// XP-flow regressions drive StepCombat on a real planet. Staged faints exercise settlement ordering; a separate
    /// combat case verifies the real Tick dispatch.
    /// REQ: docs/design/level-progression.md#經驗分配.
    /// </summary>
    public sealed class RunSessionProgressionTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";
        private const float Radius = 640f;
        private const int BaseWild = 80;
        private const int BaseQuestStep = 80;
        private const int PartySize = 6;

        private sealed class Env
        {
            public DemoCopy Copy;
            public CombatBalance Balance;
            public Progression Progression;
            public InputActionAsset Actions;
            public List<CreatureDef> Wild = new List<CreatureDef>();
            public List<CreatureDef> Starters = new List<CreatureDef>();
            public PlanetTypeDef Type;
            public RunPlanVocabulary Vocab;
        }

        private sealed class Run
        {
            public RunSession Session;
        }

        private readonly List<GameObject> _live = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _live)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _live.Clear();
        }

        // ---------------------------------------------------------------------------------------------------------
        // P2-05 model-replacement seam (art-asset-pipeline.md#佔位模型)
        // ---------------------------------------------------------------------------------------------------------

        private sealed class RecordingViews : ICreatureViewFactory
        {
            private readonly PlaceholderCreatureViewFactory _inner = new PlaceholderCreatureViewFactory(null);
            public readonly List<CreatureViewRequest> Requests = new List<CreatureViewRequest>();
            public readonly List<GameObject> Roots = new List<GameObject>();
            public int Disposed;
            public int Fainted;
            public readonly HashSet<GameObject> FaintedRoots = new HashSet<GameObject>();

            public ICreatureView Create(in CreatureViewRequest request)
            {
                ICreatureView view = _inner.Create(request);
                Requests.Add(request);
                Roots.Add(view.Root.gameObject);
                return new Counting(view, this);
            }

            private sealed class Counting : ICreatureView
            {
                private readonly ICreatureView _inner;
                private readonly RecordingViews _owner;

                public Counting(ICreatureView inner, RecordingViews owner)
                {
                    _inner = inner;
                    _owner = owner;
                }

                public Transform Root => _inner.Root;

                public void ShowFainted()
                {
                    _owner.Fainted++;
                    _owner.FaintedRoots.Add(Root.gameObject);
                    _inner.ShowFainted();
                }

                public void Dispose()
                {
                    _owner.Disposed++;
                    _inner.Dispose();
                }
            }
        }

        [UnityTest]
        public IEnumerator ViewFactory_BuildsEveryUnitView_AndViewsDieWithTheRun()
        {
            Env env = LoadEnv();
            var views = new RecordingViews();
            var run = new Run();
            yield return Launch(env, 2026, env.Progression.XpThreshold(env.Progression.StarterLevel), run, views);
            IReadOnlyList<RunSession.UnitSnapshot> units = run.Session.UnitsForTests;

            Assert.That(views.Requests.Count, Is.EqualTo(units.Count), "one view per encounter unit");
            for (int i = 0; i < units.Count; i++)
            {
                RunSession.UnitSnapshot u = units[i];
                Assert.That(views.Requests[i].Def.Id, Is.EqualTo(u.CreatureId));
                Assert.That(views.Requests[i].Ally, Is.EqualTo(u.Side == EncounterSide.Player));
                Assert.That(views.Requests[i].BodyRadiusM, Is.EqualTo(u.BodyRadiusM));
            }

            Assert.That(views.Requests.Count(r => !r.Ally && r.Role == SpawnRole.Boss), Is.EqualTo(1), "the boss spawn");
            Assert.Throws<Core.Contracts.ContractException>(() => run.Session.UseViewFactory(new RecordingViews()),
                "the factory is fixed once the run is configured");

            int ally = run.Session.PartyUnitsForTests[0];
            Kill(run.Session, env, ally);
            run.Session.SyncViewsForTests();
            Assert.That(views.Fainted, Is.GreaterThan(0), "SyncViews shows the fielded fainted ally");
            Assert.That(views.Roots[ally].GetComponent<Renderer>().sharedMaterial.color, Is.EqualTo(Color.gray));

            Object.Destroy(run.Session.gameObject);
            yield return null;
            Assert.That(views.Disposed, Is.EqualTo(views.Requests.Count), "OnDestroy calls Dispose on every view");
            Assert.That(views.Roots.All(r => r == null), Is.True, "every view root is gone");
        }

        [UnityTest]
        public IEnumerator Enabled_run_Update_shows_a_fielded_fainted_unit_on_the_next_frame()
        {
            Env env = LoadEnv();
            var views = new RecordingViews();
            var run = new Run();
            yield return Launch(env, 2026, env.Progression.XpThreshold(10), run, views, keepEnabled: true);
            RunSession session = run.Session;
            int ally = session.PartyUnitsForTests[0];
            RunSession.UnitSnapshot before = session.UnitForTests(ally);
            GameObject root = views.Roots[ally];
            Assert.That(session.isActiveAndEnabled, Is.True);
            Assert.That(before.Fielded && !before.IsFainted, Is.True);
            Assert.That(root.GetComponent<Renderer>().sharedMaterial.color, Is.Not.EqualTo(Color.gray));
            Assert.That(views.FaintedRoots.Contains(root), Is.False);

            Kill(session, env, ally);
            long now = session.NowMsForTests;
            float deadline = Time.realtimeSinceStartup + 2f;
            // Only Unity drives the enabled session: no StepCombatForTests or SyncViewsForTests.
            do
            {
                yield return null;
            }
            while (session.NowMsForTests == now && Time.realtimeSinceStartup < deadline);

            Assert.That(session.isActiveAndEnabled, Is.True);
            Assert.That(session.NowMsForTests, Is.GreaterThan(now), "Unity Update advanced combat time");
            RunSession.UnitSnapshot after = session.UnitForTests(ally);
            Assert.That(after.Fielded && after.IsFainted, Is.True, "the fainted ally remains fielded");
            Assert.That(before.IsFainted, Is.False, "the earlier unit snapshot stays detached");
            Assert.That(views.FaintedRoots.Contains(root), Is.True, "Unity Update must show this ally fainted");
            Assert.That(root.activeSelf, Is.True, "the fielded fainted ally stays visible");
            Assert.That(root.GetComponent<Renderer>().sharedMaterial.color, Is.EqualTo(Color.gray));
            Assert.That(Vector3.Distance(root.transform.position, after.Position), Is.LessThan(1e-5f));
        }

        // ---------------------------------------------------------------------------------------------------------
        // R1 — SOL P2-12 R1
        // ---------------------------------------------------------------------------------------------------------

        // REQ: level-progression.md#經驗分配 — "每場戰鬥獨立記錄出場資格，下一場戰鬥重置".
        // Battle 1 fields slots {0,1}; the party then swaps slot 0 for slot 2 outside combat; battle 2 is a wild unit
        // that is already down when its first tick runs, so AnyEngaged() is false after Tick and the in-loop BeginCombat
        // never runs. The knock-out must still use battle 2's fielded set {1,2}, not battle 1's {0,1}.
        // Pool = floor(80 × C(10) / C(10)) = 80 for a tier-0 normal (LV10). Weights: appeared 4, others 2 → W = 4+4+4×2 = 16.
        //   battle 1 {0,1}: 80×4/16 = 20 each for slots 0,1; 80×2/16 = 10 for slots 2..5 → [20,20,10,10,10,10]
        //   battle 2 {1,2}: slots 1,2 get 20; slots 0,3,4,5 get 10                        → [10,20,20,10,10,10]
        //   (the old code would pay battle 2 with battle 1's record → [20,20,10,10,10,10]).
        [UnityTest]
        public IEnumerator R1_a_battle_cleared_on_its_first_tick_pays_the_units_fielded_in_that_battle()
        {
            Env env = LoadEnv();
            ulong seed = FindSeed(env, p => Tier0Normals(p, null).Count >= 2);
            RunPlan plan = PlanFor(env, seed);
            List<int> normals = Tier0Normals(plan, null);
            int e1 = normals[0];
            int e2 = normals[1];
            var run = new Run();
            yield return Launch(env, seed, env.Progression.XpThreshold(env.Progression.StarterLevel), run);
            RunSession s = run.Session;
            AssertSpawnUnits(s, plan, e1, e2);
            Assert.That(Pool(env.Progression, BaseWild, WildLevelOf(env, plan, e1), false), Is.EqualTo(80), "tier-0 normal pool");
            int questRegion = plan.Quest[0].Region;

            EngageAt(s, plan, e1);
            Step(s);
            Assert.That(s.InCombatForTests, Is.True, "battle 1 starts through real engagement");
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 0, 1 }), "opening pair");

            Kill(s, env, e1);
            ParkAt(s, Quiet(s, questRegion));
            int[] before = Exp(s);
            int playerBefore = s.PlayerExpForTests;
            s.InjectFaintForTests(e1);
            s.InjectFaintForTests(e1);
            Step(s);
            Assert.That(s.InCombatForTests, Is.False, "battle 1 over");
            Assert.That(Delta(before, Exp(s)), Is.EqualTo(new[] { 20, 20, 10, 10, 10, 10 }), "duplicate faint pays {0,1} once");
            // REQ: level-progression.md#玩家等級 — the player earns the sum the mains actually gained.
            Assert.That(s.PlayerExpForTests - playerBefore, Is.EqualTo(80), "player XP = sum of main gains");

            s.SwitchMainForTests();
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 1, 2 }), "slot 0 swapped for slot 2 out of combat");
            Kill(s, env, e2);
            ParkAt(s, Quiet(s, questRegion));
            before = Exp(s);
            s.InjectFaintForTests(e2);
            Step(s);
            Assert.That(s.InCombatForTests, Is.False, "battle 2 opened and closed inside one step");
            Assert.That(Delta(before, Exp(s)), Is.EqualTo(new[] { 10, 20, 20, 10, 10, 10 }), "battle 2 pays {1,2}, not battle 1's {0,1}");
        }

        // REQ: combat.md#指定目標 (D0-W4) — CycleTarget walks candidates by (distance, id) and wraps without provoking;
        // Focus attacks the lock; cycling under Focus retargets; a knocked-out lock is dropped.
        [UnityTest]
        public IEnumerator W4_cycle_locks_by_distance_and_focus_attacks_the_lock()
        {
            Env env = LoadEnv();
            ulong seed = FindSeed(env, p => PackPair(p) != null);
            RunPlan plan = PlanFor(env, seed);
            int a = PackPair(plan).Value.Item1;
            var run = new Run();
            yield return Launch(env, seed, env.Progression.XpThreshold(env.Progression.StarterLevel), run);
            RunSession s = run.Session;

            ParkAt(s, Beside(s.DirectionForTests(s.UnitForTests(a).Position)));
            float3 player = s.PlayerPositionForTests;
            List<int> expected = s.UnitsForTests
                .Where(u => u.Side == EncounterSide.Wild && !u.IsFainted && math.distance(u.Position, player) <= Encounter.ProvokeRadiusM)
                .OrderBy(u => math.distance(u.Position, player)).ThenBy(u => u.Id)
                .Select(u => u.Id).ToList();
            Assert.That(expected.Count, Is.GreaterThanOrEqualTo(2), "a pack pair sits within provoke reach");
            Assert.That(s.LockedTargetForTests, Is.EqualTo(-1), "nothing locked at landing");

            for (int i = 0; i < expected.Count; i++)
            {
                s.CycleTargetForTests();
                Assert.That(s.LockedTargetForTests, Is.EqualTo(expected[i]), "cycle step " + i);
                Assert.That(s.CommandForTests, Is.Not.EqualTo(AiCommandKind.Focus), "cycling alone does not command");
            }

            s.CycleTargetForTests();
            Assert.That(s.LockedTargetForTests, Is.EqualTo(expected[0]), "wraps to the nearest");

            s.FocusNearestForTests();
            Assert.That(s.CommandForTests, Is.EqualTo(AiCommandKind.Focus));
            Assert.That(s.CommandTargetForTests, Is.EqualTo(expected[0]), "Focus attacks the lock");

            s.CycleTargetForTests();
            Assert.That(s.LockedTargetForTests, Is.EqualTo(expected[1]));
            Assert.That(s.CommandTargetForTests, Is.EqualTo(expected[1]), "cycling under Focus retargets");

            Step(s);
            Assert.That(s.InCombatForTests, Is.True, "the retargeted lock was provoked");

            Kill(s, env, expected[1]);
            s.InjectFaintForTests(expected[1]);
            Step(s);
            Assert.That(s.LockedTargetForTests, Is.EqualTo(-1), "a knocked-out lock is dropped");
        }

        // REQ: combat.md#實體碰撞 (D0-W2) — the party fielded on the character's own spot is pushed apart from it, and the
        // character's share reaches the physics body (Encounter.PlayerPush applied by RunSession).
        // REQ: combat.md#近身互動 (D0-W3) — an idle wild within reach is offered; the open menu freezes the character's
        // input; Observe fills the detail; Provoke closes the menu, locks the target and starts the fight.
        [UnityTest]
        public IEnumerator W2_W3_party_is_pushed_off_the_character_and_close_interaction_provokes()
        {
            Env env = LoadEnv();
            // Tier-0 juveniles are timid (D0-E2), so standing 2 m away does not start a fight by itself.
            ulong seed = FindSeed(env, p => Tier0Normals(p, null).Count >= 1);
            RunPlan plan = PlanFor(env, seed);
            int a = Tier0Normals(plan, null)[0];
            var run = new Run();
            yield return Launch(env, seed, env.Progression.XpThreshold(env.Progression.StarterLevel), run);
            RunSession s = run.Session;

            float3 wild = s.DirectionForTests(s.UnitForTests(a).Position);
            float angle = 2f / Radius;
            float3 spot = math.normalize(wild * math.cos(angle) + PlanetFrame.FromUp(wild).North * math.sin(angle));
            ParkAt(s, spot);
            Step(s);

            // Smallest body (XS 0.2 m) + character 0.35 m, less float slack: before D0-W2 the party stood on the spot.
            float3 player = s.DirectionForTests(s.PlayerPositionForTests);
            foreach (int id in s.ActiveUnitsForTests)
            {
                float3 d = s.DirectionForTests(s.UnitForTests(id).Position);
                Assert.That(math.atan2(math.length(math.cross(player, d)), math.dot(player, d)) * Radius, Is.GreaterThan(0.54f),
                    "party unit " + id + " pushed off the character");
            }

            Assert.That(s.InCombatForTests, Is.False, "parking next to a timid wild starts no fight");

            // The push above moved the character up to ~2 m; stand 1.5 m south of the wild (the party is north of it).
            wild = s.DirectionForTests(s.UnitForTests(a).Position);
            MovePlayer(s, math.normalize(wild * math.cos(1.5f / Radius) - PlanetFrame.FromUp(wild).North * math.sin(1.5f / Radius)));
            Step(s);
            int offered = s.InteractableForTests;
            Assert.That(offered, Is.GreaterThanOrEqualTo(0), "an idle wild 1.5 m away is offered");
            RunSession.UnitSnapshot target = s.UnitForTests(offered);
            Assert.That(target.Side, Is.EqualTo(EncounterSide.Wild));

            s.OpenInteractForTests();
            Assert.That(s.InteractTargetForTests, Is.EqualTo(offered));
            Assert.That(s.InteractableForTests, Is.EqualTo(-1), "no prompt while the menu is open");
            Assert.That(s.CharacterInputEnabledForTests, Is.False, "the character stands still");

            s.ChooseInteractForTests(0);
            Assert.That(s.ObserveTextForTests, Is.Not.Null.And.Not.Contains("{"), "observe fills every placeholder");
            Assert.That(s.InteractTargetForTests, Is.EqualTo(offered), "observe keeps the menu open");

            s.ChooseInteractForTests(1);
            Assert.That(s.InteractTargetForTests, Is.EqualTo(-1), "provoke closes the menu");
            Assert.That(s.CharacterInputEnabledForTests, Is.True);
            Assert.That(s.LockedTargetForTests, Is.EqualTo(offered), "the provoked wild is locked");

            Step(s);
            Assert.That(s.InCombatForTests, Is.True, "provoke starts the fight");
            Assert.That(s.InteractableForTests, Is.Not.EqualTo(offered), "an engaged wild is no longer offered");
        }

        // Two Normal spawns of one pack (RunPlanner scatters pack members ≤ 5 m from its centre). Unit id == spawn index.
        private static (int, int)? PackPair(RunPlan p)
        {
            for (int i = 0; i < p.Spawns.Count; i++)
            {
                for (int j = i + 1; j < p.Spawns.Count; j++)
                {
                    if (p.Spawns[i].Role == SpawnRole.Normal && p.Spawns[j].Role == SpawnRole.Normal &&
                        p.Spawns[i].Group == p.Spawns[j].Group)
                    {
                        return (i, j);
                    }
                }
            }

            return null;
        }

        // ---------------------------------------------------------------------------------------------------------
        // P2-05 — material drops (Q-C32 option B)
        // ---------------------------------------------------------------------------------------------------------

        // REQ: creatures.md#進化 — one roll per knocked-out spawn on the run's own 'D' stream, tier by role, element from the
        // planet type's bias. The oracle replays the same knock-out order on a fresh stream; the boss is injected twice
        // before one step to show a re-sent faint cannot roll a second drop (tech-debt R5-01).
        [UnityTest]
        public IEnumerator Knockout_excludes_fainted_member_and_breaks_ties_by_persistent_id()
        {
            Env env = LoadEnv();
            ulong seed = FindSeed(env, p => Tier0Normals(p, null).Count > 0);
            RunPlan plan = PlanFor(env, seed);
            int target = Tier0Normals(plan, null)[0];
            var run = new Run();
            yield return Launch(env, seed, env.Progression.XpThreshold(10), run, ids: new[] { 6, 5, 4, 3, 2, 1 });
            RunSession s = run.Session;
            ParkAt(s, Quiet(s, plan.Quest[0].Region));
            Kill(s, env, s.PartyUnitsForTests[0]);
            Kill(s, env, target);
            int[] before = Exp(s);
            s.InjectFaintForTests(target);
            Step(s);
            Assert.That(Delta(before, Exp(s)), Is.EqualTo(new[] { 0, 27, 13, 13, 13, 14 }));
        }

        [UnityTest]
        public IEnumerator Real_tick_knockout_pays_xp()
        {
            Env env = LoadEnv();
            MaterialDrops rules = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            ulong seed = FindSeed(env, p =>
            {
                var rng = new Pcg32(p.Seed, 'D');
                return Tier0Normals(p, null).Count > 0 &&
                    rules.TryRoll(SpawnRole.Normal, env.Type.ElementBias, ref rng, out _);
            });
            RunPlan plan = PlanFor(env, seed);
            int target = Tier0Normals(plan, null)[0];
            var run = new Run();
            yield return Launch(env, seed, env.Progression.XpThreshold(50), run);
            RunSession s = run.Session;
            EngageAt(s, plan, target);
            for (int i = 0; i < 2000; i++)
            {
                int[] before = Exp(s);
                Step(s);
                if (s.EventsForTests.Any(e => e.Kind == EncounterEventKind.Faint && e.TargetId == target))
                {
                    var events = (EncounterEvent[])s.EventsForTests;
                    int faint = System.Array.FindIndex(events, e => e.Kind == EncounterEventKind.Faint && e.TargetId == target);
                    events[faint] = default;
                    Assert.That(s.EventsForTests[faint].Kind, Is.EqualTo(EncounterEventKind.Faint), "event snapshot is detached");
                    var rng = new Pcg32(seed, 'D');
                    Assert.That(rules.TryRoll(SpawnRole.Normal, env.Type.ElementBias, ref rng, out MaterialStack expected), Is.True);
                    Assert.That(s.MaterialsForTests.Count, Is.EqualTo(1), "real faint dispatch awards the drop");
                    Assert.That(s.MaterialsForTests[0].Key, Is.EqualTo(expected.Key));
                    Assert.That(s.MaterialsForTests[0].Qty, Is.EqualTo(expected.Qty));
                    Assert.That(Delta(before, Exp(s)).Sum(), Is.GreaterThan(0), "real faint dispatch pays XP");
                    yield break;
                }
            }

            Assert.Fail("the wild creature was not knocked out by Encounter.Tick");
        }

        [UnityTest]
        public IEnumerator Elite_knockout_drops_elite_material()
        {
            Env env = LoadEnv();
            MaterialDrops rules = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            ulong seed = FindSeed(env, p =>
            {
                var rng = new Pcg32(p.Seed, 'D');
                return p.Spawns.Any(sp => sp.Role == SpawnRole.Elite) &&
                    rules.TryRoll(SpawnRole.Elite, env.Type.ElementBias, ref rng, out _);
            });
            RunPlan plan = PlanFor(env, seed);
            int elite = plan.Spawns.ToList().FindIndex(sp => sp.Role == SpawnRole.Elite);
            var run = new Run();
            yield return Launch(env, seed, env.Progression.XpThreshold(10), run);
            RunSession s = run.Session;
            Kill(s, env, elite);
            ParkAt(s, Quiet(s, plan.Quest[0].Region));
            s.InjectFaintForTests(elite);
            Step(s);

            var oracle = new Pcg32(seed, 'D');
            Assert.That(rules.TryRoll(SpawnRole.Elite, env.Type.ElementBias, ref oracle, out MaterialStack expected), Is.True);
            Assert.That(s.MaterialsForTests.Count, Is.EqualTo(1));
            Assert.That(s.MaterialsForTests[0].Key, Is.EqualTo(expected.Key));
            Assert.That(s.MaterialsForTests[0].Qty, Is.EqualTo(expected.Qty));
            Assert.That(s.MaterialsForTests[0].Key.Tier, Is.EqualTo(rules.TierFor(SpawnRole.Elite)));
            var snapshot = (MaterialStack[])s.MaterialsForTests;
            snapshot[0] = default;
            Assert.That(s.MaterialsForTests[0].Key, Is.EqualTo(expected.Key), "the snapshot cannot alter the run");
            var party = (int[])s.PartyUnitsForTests;
            int first = party[0];
            party[0] = -1;
            Assert.That(s.PartyUnitsForTests[0], Is.EqualTo(first), "the party snapshot cannot alter the run");
            var units = (RunSession.UnitSnapshot[])s.UnitsForTests;
            string creature = units[elite].CreatureId;
            units[elite] = default;
            Assert.That(s.UnitForTests(elite).CreatureId, Is.EqualTo(creature), "unit snapshot is detached");
            var spawns = (RunSpawn[])s.SpawnsForTests;
            spawns[elite] = default;
            Assert.That(s.SpawnsForTests[elite].CreatureId, Is.EqualTo(plan.Spawns[elite].CreatureId), "plan snapshot is detached");
            var active = (int[])s.ActiveUnitsForTests;
            int activeId = active[0];
            active[0] = -1;
            Assert.That(s.ActiveUnitsForTests[0], Is.EqualTo(activeId), "team snapshot is detached");
        }

        [UnityTest]
        public IEnumerator Knockouts_drop_one_roll_per_spawn_from_the_planet_bias()
        {
            Env env = LoadEnv();
            ulong seed = FindSeed(env, p => Tier0Normals(p, null).Count >= 2);
            RunPlan plan = PlanFor(env, seed);
            List<int> normals = Tier0Normals(plan, null);
            int boss = plan.Spawns.ToList().FindIndex(sp => sp.Role == SpawnRole.Boss);
            Assert.That(boss, Is.GreaterThanOrEqualTo(0), "plan has a boss");
            var run = new Run();
            yield return Launch(env, seed, env.Progression.XpThreshold(env.Progression.StarterLevel), run);
            RunSession s = run.Session;
            int questRegion = plan.Quest[0].Region;
            var order = new List<int> { normals[0], normals[1], boss };
            foreach (int unit in order)
            {
                Kill(s, env, unit);
                ParkAt(s, Quiet(s, questRegion));
                s.InjectFaintForTests(unit);
                if (unit == boss)
                {
                    s.InjectFaintForTests(unit);
                }

                Step(s);
            }

            MaterialDrops rules = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            var oracle = new Pcg32(seed, 'D');
            var expected = new List<MaterialStack>();
            foreach (int unit in order)
            {
                if (rules.TryRoll(plan.Spawns[unit].Role, env.Type.ElementBias, ref oracle, out MaterialStack drop))
                {
                    expected.Add(drop);
                }
            }

            IReadOnlyList<MaterialStack> got = s.MaterialsForTests;
            Assert.That(got.Count, Is.EqualTo(expected.Count), "one roll per spawn; the boss's re-sent faint adds nothing");
            for (int i = 0; i < got.Count; i++)
            {
                Assert.That(got[i].Key, Is.EqualTo(expected[i].Key), "drop " + i);
                Assert.That(got[i].Qty, Is.EqualTo(expected[i].Qty), "drop " + i);
                CollectionAssert.Contains(env.Type.ElementBias, got[i].Key.Element);
            }

            Assert.That(got[got.Count - 1].Key.Tier, Is.EqualTo(rules.TierFor(SpawnRole.Boss)), "the boss always drops (100%)");
        }

        // ---------------------------------------------------------------------------------------------------------
        // R3-01 — SOL P2-12 r3
        // ---------------------------------------------------------------------------------------------------------

        // REQ: level-progression.md#經驗分配 — level-up effects settle once the battle is over, so growth earned outside
        // combat must be applied before a new battle can lock it out.
        // Everyone starts at T(11)−1 = 1261 XP (LV10, one point short of LV11). Entering the ReachPoi region outside
        // combat pays one quest step to an all-reserve record (weights 2 each): pool/6 each, ≥ 1 → LV11 for all six.
        // In the same step a wild pack next to the arrival point engages, so that step starts a new battle. The old code
        // applied level-ups only after Tick, saw InCombat, and left every unit at LV10 for the whole fight.
        // REQ: docs/reviews/P2-05-wiring-sol.md W-03 — every end path hands its drops to the lobby (Q-C32: win, loss and
        // abandon all keep materials). The seed makes the boss's knock-out the stream's first, successful roll, so the
        // test does not depend on balance.json's chance values.
        public enum EndPath { Victory, Defeat, Abandon, Downed }

        [UnityTest]
        public IEnumerator Every_end_path_hands_the_run_materials_to_the_lobby(
            [Values(EndPath.Victory, EndPath.Defeat, EndPath.Abandon, EndPath.Downed)] EndPath path)
        {
            Env env = LoadEnv();
            MaterialDrops rules = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            ulong seed = FindSeed(env, p =>
            {
                var rng = new Pcg32(p.Seed, 'D');
                return rules.TryRoll(SpawnRole.Boss, env.Type.ElementBias, ref rng, out _);
            });
            RunPlan plan = PlanFor(env, seed);
            int boss = plan.Spawns.ToList().FindIndex(sp => sp.Role == SpawnRole.Boss);
            RunOutcome outcome = null;
            int ends = 0;
            var run = new Run();
            yield return Launch(env, seed, env.Progression.XpThreshold(10), run, onEnd: o => { outcome = o; ends++; });
            RunSession s = run.Session;
            Kill(s, env, boss);
            ParkAt(s, Quiet(s, plan.Quest[0].Region));
            s.InjectFaintForTests(boss);
            Step(s);
            MaterialStack[] dropped = s.MaterialsForTests.ToArray();
            Assert.That(dropped.Length, Is.EqualTo(1), "the boss drop is banked in the run");
            Assert.That(s.EndedForTests, Is.False, "the boss stays capturable for its knocked-out window");

            switch (path)
            {
                case EndPath.Victory:
                    // The window closes with no capture throw: the boss is gone and the run is won.
                    for (int i = 0; i < 2000 && !s.EndedForTests; i++)
                    {
                        Step(s);
                    }

                    break;
                case EndPath.Defeat:
                    foreach (int id in s.PartyUnitsForTests)
                    {
                        Kill(s, env, id);
                    }

                    Step(s);
                    break;
                case EndPath.Abandon:
                    s.AbandonForTests();
                    Assert.That(s.EndedForTests, Is.False, "the first press only arms");
                    s.AbandonForTests();
                    break;
                case EndPath.Downed:
                    // Q-C33: a real wild hit through Encounter.Tick (not a seam) must raise PlayerDowned and settle the
                    // run as a defeat. The party stays parked >40 m away so the player is the only intruder in reach.
                    RunSession.UnitSnapshot target = s.UnitsForTests.First(u =>
                        u.Side == EncounterSide.Wild && u.Fielded && !u.IsFainted && u.Id != boss);
                    float3 wildUp = s.DirectionForTests(target.Position);
                    MovePlayer(s, math.normalize(wildUp * Radius + PlanetFrame.FromUp(wildUp).East * 3f));
                    s.WoundPlayerForTests(1);
                    s.ProvokeForTests(target.Id);
                    for (int i = 0; i < 2000 && !s.EndedForTests; i++)
                    {
                        Step(s);
                    }

                    Assert.That(s.PlayerHpForTests, Is.EqualTo(0), "the run ended because the player was downed");
                    break;
            }

            Assert.That(ends, Is.EqualTo(1), "End fires exactly once");
            Assert.That(outcome.Victory, Is.EqualTo(path == EndPath.Victory));
            Assert.That(outcome.Abandoned, Is.EqualTo(path == EndPath.Abandon));
            Assert.That(outcome.Materials, Is.EqualTo(dropped), "the outcome carries exactly what dropped");
            Assert.That(outcome.PartyExp.Count, Is.EqualTo(PartySize));

            // DemoGame.OnRunEnded banks every outcome through LobbyTransactions.Settle; the same call here proves the
            // drop reaches the save on this path (the Settle rules themselves are LobbyTransactionsTests' job).
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cosmimera-endpath-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                var live = new LobbySave();
                live.ChooseStarter(env.Starters[0].LineId, env.Starters[0].Id, env.Progression.XpThreshold(10));
                for (int i = 1; i < PartySize; i++)
                {
                    live.Add(env.Starters[i % env.Starters.Count].Id, VesselKind.TalismanCard, env.Progression.XpThreshold(10));
                }

                List<int> ids = live.Owned.Select(o => o.InstanceId).Take(PartySize).ToList();
                CommitStatus st = LobbyTransactions.Settle(live, ids, outcome, System.IO.Path.Combine(dir, "lobby.json"),
                    out LobbySave next, out _, out System.Exception error);
                Assert.That(st, Is.EqualTo(CommitStatus.Committed), error?.ToString());
                Assert.That(next.Materials.Count(dropped[0].Key), Is.EqualTo(dropped[0].Qty));
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        [UnityTest]
        public IEnumerator R3_01_a_level_earned_outside_combat_is_applied_even_when_the_same_step_starts_a_battle()
        {
            Env env = LoadEnv();
            ulong seed = FindSeed(env, p => NormalInQuestRegion(p) >= 0);
            RunPlan plan = PlanFor(env, seed);
            int spawn = NormalInQuestRegion(plan);
            Progression progression = env.Progression;
            int startExp = progression.XpThreshold(11) - 1;
            var run = new Run();
            yield return Launch(env, seed, startExp, run);
            RunSession s = run.Session;
            foreach (int id in s.PartyUnitsForTests)
            {
                Assert.That(s.UnitForTests(id).Level, Is.EqualTo(10), "starts one XP short of LV11");
            }

            int pool = Pool(progression, BaseQuestStep, QuestLevel(env, plan, 0), false);
            int[] before = Exp(s);
            int[] levelsAtTick = null;
            s.BeforeTickForTests = () => levelsAtTick = s.PartyUnitsForTests.Select(id => s.UnitForTests(id).Level).ToArray();
            EngageAt(s, plan, spawn);
            Step(s);
            Assert.That(levelsAtTick, Is.EqualTo(Enumerable.Repeat(11, PartySize).ToArray()), "levels at the first Tick");

            Assert.That(s.InCombatForTests, Is.True, "this step starts a battle");
            Assert.That(Delta(before, Exp(s)), Is.EqualTo(Split(pool, new int[0])),
                "ReachPoi paid once, all-reserve: pool " + pool + " over weights 2×6");
            for (int slot = 0; slot < PartySize; slot++)
            {
                Assert.That(progression.LevelOf(s.PartyExpForTests(slot)), Is.EqualTo(11), "XP crossed T(11), slot " + slot);
                Assert.That(s.UnitForTests(s.PartyUnitsForTests[slot]).Level, Is.EqualTo(11),
                    "level-up applied before the battle began, slot " + slot);
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // R3-02 — SOL P2-12 r3
        // ---------------------------------------------------------------------------------------------------------

        // REQ: level-progression.md#經驗分配 — "中途替換不清除已出場標記"; quest XP of a region step uses the battle in
        // progress. Battle {0,1} is running; slot 0 is swapped for slot 2 and, in that same step, the ReachPoi region is
        // entered. The swapped-in unit must already count, and the swapped-out one stays.
        // Record {0,1,2}: weights 4,4,4,2,2,2 → W = 18. For pool 80 (tier-0 region): floors 17,17,17,8,8,8 = 75, remainder 5;
        // reserve remainders 160 mod 18 = 16 beat appeared 320 mod 18 = 14, so the 3 reserves take +1 → 9 each, then the
        // last 2 go to the lowest ids of the appeared → [18,18,17,9,9,9]. The old code used {0,1} → [20,20,10,10,10,10].
        // The test derives the same split for whatever tier the quest region has.
        [UnityTest]
        public IEnumerator R3_02_a_swap_and_a_region_step_in_one_step_credit_the_swapped_in_unit()
        {
            Env env = LoadEnv();
            ulong seed = FindSeed(env, p => Tier0Normals(p, null).Count >= 1);
            RunPlan plan = PlanFor(env, seed);
            int e1 = Tier0Normals(plan, null)[0];
            var run = new Run();
            yield return Launch(env, seed, env.Progression.XpThreshold(env.Progression.StarterLevel), run);
            RunSession s = run.Session;
            AssertSpawnUnits(s, plan, e1);

            EngageAt(s, plan, e1);
            Step(s);
            Assert.That(s.InCombatForTests, Is.True, "battle in progress");
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 0, 1 }));

            s.SwitchMainForTests();
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 1, 2 }), "slot 2 swapped in for slot 0");
            // Only the player walks into the region; the fielded pair stays on the engaged pack, so the battle goes on.
            MovePlayer(s, plan.Regions[plan.Quest[0].Region].Center);
            int pool = Pool(env.Progression, BaseQuestStep, QuestLevel(env, plan, 0), false);
            int[] before = Exp(s);
            Step(s);

            Assert.That(s.InCombatForTests, Is.True, "still the same battle");
            Assert.That(Delta(before, Exp(s)), Is.EqualTo(Split(pool, new[] { 0, 1, 2 })),
                "ReachPoi pays the record {0,1,2}: pool " + pool);
        }

        // ---------------------------------------------------------------------------------------------------------
        // R3-03 — SOL P2-12 r3
        // ---------------------------------------------------------------------------------------------------------

        // REQ: level-progression.md#經驗分配 — "紀錄在擊倒時快照，之後的戰鬥不會改寫它" and Q-E1 (an elite knocked out before its
        // step is credited when the step becomes current).
        // Battle A {0,1} knocks the elite out before the ReachPoi step. After a swap, battle B {1,2} enters the region and
        // knocks out the third matching wild unit; the tracker then completes DefeatWild and immediately credits the
        // elite step. That last step must be paid to A's snapshot {0,1}; the old code paid it to B's {1,2}.
        // The step it completes in B is split as in R3-02. The expected delta of the final kill is the sum of three
        // independent settlements: the wild knock-out (B), the DefeatWild step (B), the DefeatElite step (A).
        [UnityTest]
        public IEnumerator R3_03_a_retroactive_elite_step_pays_the_elites_knockout_snapshot()
        {
            Env env = LoadEnv();
            ulong seed = FindSeed(env, p => ElitePlan(env, p, out _, out _, out _));
            RunPlan plan = PlanFor(env, seed);
            ElitePlan(env, plan, out List<int> matching, out int a, out int b);
            int elite = plan.Quest[2].TargetSpawn;
            var run = new Run();
            yield return Launch(env, seed, env.Progression.XpThreshold(env.Progression.StarterLevel), run);
            RunSession s = run.Session;
            AssertSpawnUnits(s, plan, a, b, elite);
            AssertSpawnUnits(s, plan, matching.ToArray());
            int questRegion = plan.Quest[0].Region;
            Progression progression = env.Progression;

            // Battle A: real engagement, then the elite falls inside it.
            EngageAt(s, plan, a);
            Step(s);
            Assert.That(s.InCombatForTests, Is.True, "battle A");
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 0, 1 }));
            Kill(s, env, elite);
            s.InjectFaintForTests(elite);
            Step(s);
            Assert.That(s.InCombatForTests, Is.True, "battle A continues after the elite fell");
            Assert.That(s.QuestIndexForTests, Is.EqualTo(0), "elite fell before the ReachPoi step");

            // Battle A ends; outside combat slot 0 is swapped for slot 2.
            Kill(s, env, a);
            ParkAt(s, Quiet(s, questRegion));
            s.InjectFaintForTests(a);
            Step(s);
            Assert.That(s.InCombatForTests, Is.False, "battle A over");
            s.SwitchMainForTests();
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 1, 2 }));

            // Battle B: real engagement on another pack, then the party enters the quest region.
            EngageAt(s, plan, b);
            Step(s);
            Assert.That(s.InCombatForTests, Is.True, "battle B");
            MovePlayer(s, plan.Regions[questRegion].Center);
            int[] before = Exp(s);
            Step(s);
            Assert.That(s.InCombatForTests, Is.True, "battle B continues in the region");
            Assert.That(s.QuestIndexForTests, Is.EqualTo(1), "ReachPoi done, DefeatWild current");
            int[] recordB = { 1, 2 };
            int[] recordA = { 0, 1 };
            Assert.That(Delta(before, Exp(s)),
                Is.EqualTo(Split(Pool(progression, BaseQuestStep, QuestLevel(env, plan, 0), false), recordB)),
                "ReachPoi pays battle B's record");

            int count = plan.Quest[1].Count;
            for (int k = 0; k < count - 1; k++)
            {
                Kill(s, env, matching[k]);
                s.InjectFaintForTests(matching[k]);
            }

            Step(s);
            Assert.That(s.QuestIndexForTests, Is.EqualTo(1));
            Assert.That(s.QuestProgressForTests, Is.EqualTo(count - 1), "all but the last matching knock-out counted");

            int last = matching[count - 1];
            Kill(s, env, last);
            s.InjectFaintForTests(last);
            before = Exp(s);
            Step(s);
            Assert.That(s.QuestEarnedForTests(1), Is.True, "DefeatWild completed by the last knock-out");
            Assert.That(s.QuestEarnedForTests(2), Is.True, "DefeatElite credited retroactively (Q-E1)");
            Assert.That(s.QuestIndexForTests, Is.EqualTo(3));

            int wildPool = Pool(progression, BaseWild, WildLevelOf(env, plan, last), false);
            int wildStepPool = Pool(progression, BaseQuestStep, QuestLevel(env, plan, 1), false);
            int eliteStepPool = Pool(progression, BaseQuestStep, QuestLevel(env, plan, 2), false);
            int[] knockout = Split(wildPool, recordB);
            int[] defeatWild = Split(wildStepPool, recordB);
            int[] defeatElite = Split(eliteStepPool, recordA);
            int[] expected = new int[PartySize];
            for (int i = 0; i < PartySize; i++)
            {
                expected[i] = knockout[i] + defeatWild[i] + defeatElite[i];
            }

            Assert.That(Delta(before, Exp(s)), Is.EqualTo(expected),
                "knock-out " + Show(knockout) + " + DefeatWild " + Show(defeatWild) + " (record B) + DefeatElite " +
                Show(defeatElite) + " (record A, the elite's snapshot)");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Plan selection
        // ---------------------------------------------------------------------------------------------------------

        private static bool QuestShape(RunPlan p) =>
            p.Quest.Count == 5 && p.Quest[0].Kind == QuestStepKind.ReachPoi && p.Quest[1].Kind == QuestStepKind.DefeatWild &&
            p.Quest[2].Kind == QuestStepKind.DefeatElite && p.Quest[2].TargetSpawn >= 0;

        private static bool InQuestRegion(RunPlan p, float3 direction) => RunPlanner.RegionAt(p, direction) == p.Quest[0].Region;

        // Normal LV10 wild units outside the quest region: standing there can never complete ReachPoi by accident.
        private static List<int> Tier0Normals(RunPlan p, ICollection<int> except)
        {
            var list = new List<int>();
            if (!QuestShape(p))
            {
                return list;
            }

            for (int i = 0; i < p.Spawns.Count; i++)
            {
                RunSpawn sp = p.Spawns[i];
                if (sp.Role == SpawnRole.Normal && p.Regions[sp.Region].Tier == 0 && !InQuestRegion(p, sp.Direction) &&
                    (except == null || !except.Contains(i)))
                {
                    list.Add(i);
                }
            }

            return list;
        }

        private static int NormalInQuestRegion(RunPlan p)
        {
            if (!QuestShape(p))
            {
                return -1;
            }

            for (int i = 0; i < p.Spawns.Count; i++)
            {
                if (p.Spawns[i].Role == SpawnRole.Normal && InQuestRegion(p, p.Spawns[i].Direction))
                {
                    return i;
                }
            }

            return -1;
        }

        // The wild units that count for DefeatWild (normal role, the step's element) plus two quiet packs for the battles.
        private static bool ElitePlan(Env env, RunPlan p, out List<int> matching, out int a, out int b)
        {
            matching = new List<int>();
            a = -1;
            b = -1;
            if (!QuestShape(p))
            {
                return false;
            }

            RunQuestStep step = p.Quest[1];
            for (int i = 0; i < p.Spawns.Count && matching.Count < step.Count; i++)
            {
                RunSpawn sp = p.Spawns[i];
                if (sp.Role == SpawnRole.Normal && RunPlanner.MainElement(FormOf(env, sp.CreatureId)) == step.Element)
                {
                    matching.Add(i);
                }
            }

            if (matching.Count < step.Count)
            {
                return false;
            }

            List<int> packs = Tier0Normals(p, matching);
            if (packs.Count < 2)
            {
                return false;
            }

            a = packs[0];
            b = packs[1];
            return true;
        }

        private static CreatureDef FormOf(Env env, string id)
        {
            CreatureDef def = env.Wild.Find(c => c.Id == id);
            Assert.That(def, Is.Not.Null, id);
            return def;
        }

        private static RunPlan PlanFor(Env env, ulong seed) =>
            RunPlanner.Generate(new PlanetConfig(Radius, float3.zero), seed, env.Type, CreatureStage.Adult, env.Wild, env.Vocab, null);

        // Same inputs as RunSession.Configure with no geo-templates, so the session builds exactly this plan.
        private static ulong FindSeed(Env env, System.Func<RunPlan, bool> suitable)
        {
            for (ulong seed = 1; seed <= 600; seed++)
            {
                if (suitable(PlanFor(env, seed)))
                {
                    return seed;
                }
            }

            Assert.Fail("no seed in 1..600 gives a plan with the required spawns");
            return 0;
        }

        // Golden vectors for the synthetic fixture, calculated independently of Progression.
        private static int WildLevelOf(Env env, RunPlan plan, int spawn) =>
            10 + 2 * plan.Regions[plan.Spawns[spawn].Region].Tier +
            (plan.Spawns[spawn].Role == SpawnRole.Elite ? 3 : plan.Spawns[spawn].Role == SpawnRole.Boss ? 5 : 0);

        private static int QuestLevel(Env env, RunPlan plan, int step) => 10 + 2 * plan.Regions[plan.Quest[step].Region].Tier;

        private static int Pool(Progression progression, int baseXp, int level, bool elite)
        {
            int[] costs = { 156, 163, 172, 180, 189, 198, 208, 219, 230, 241 };
            return baseXp * costs[level - 10] * (elite ? 2 : 1) / 156;
        }

        private static int[] Split(int pool, int[] appeared)
        {
            string key = pool + ":" + string.Join(",", appeared);
            return key switch
            {
                "80:" => new[] { 14, 14, 13, 13, 13, 13 },
                "80:0,1" => new[] { 20, 20, 10, 10, 10, 10 },
                "80:1,2" => new[] { 10, 20, 20, 10, 10, 10 },
                "80:0,1,2" => new[] { 18, 18, 17, 9, 9, 9 },
                "88:" => new[] { 15, 15, 15, 15, 14, 14 },
                "88:0,1" => new[] { 22, 22, 11, 11, 11, 11 },
                "88:1,2" => new[] { 11, 22, 22, 11, 11, 11 },
                "88:0,1,2" => new[] { 20, 19, 19, 10, 10, 10 },
                "96:" => new[] { 16, 16, 16, 16, 16, 16 },
                "96:0,1" => new[] { 24, 24, 12, 12, 12, 12 },
                "96:1,2" => new[] { 12, 24, 24, 12, 12, 12 },
                "96:0,1,2" => new[] { 21, 21, 21, 11, 11, 11 },
                _ => throw new System.InvalidOperationException("No golden split for " + key),
            };
        }

        private static string Show(int[] values) => "[" + string.Join(",", values) + "]";

        // ---------------------------------------------------------------------------------------------------------
        // Staging
        // ---------------------------------------------------------------------------------------------------------

        private static void Step(RunSession s) => s.StepCombatForTests();

        private static int[] Exp(RunSession s)
        {
            var exp = new int[PartySize];
            for (int i = 0; i < PartySize; i++)
            {
                exp[i] = s.PartyExpForTests(i);
            }

            return exp;
        }

        private static int[] Delta(int[] before, int[] after)
        {
            var delta = new int[before.Length];
            for (int i = 0; i < delta.Length; i++)
            {
                delta[i] = after[i] - before[i];
            }

            return delta;
        }

        private static int[] ActiveSlots(RunSession s)
        {
            var slots = new List<int>();
            foreach (int unit in s.ActiveUnitsForTests)
            {
                slots.Add(s.PartyUnitsForTests.ToList().IndexOf(unit));
            }

            slots.Sort();
            return slots.ToArray();
        }

        // Wild units are added first, in plan order, so a spawn index is also its encounter unit id.
        private static void AssertSpawnUnits(RunSession s, RunPlan plan, params int[] spawns)
        {
            foreach (int i in spawns)
            {
                Assert.That(s.UnitForTests(i).CreatureId, Is.EqualTo(plan.Spawns[i].CreatureId), "spawn " + i);
            }
        }

        private static void MovePlayer(RunSession s, float3 direction)
        {
            s.MovePlayerForTests(direction);
        }

        // Parks beside a spawn and starts the fight with it the way the Focus input does. D0-E2 territory: tier-0
        // juveniles are timid (0 m), so standing next to them no longer starts a fight by itself. Unit id == spawn
        // index (AssertSpawnUnits).
        private static void EngageAt(RunSession s, RunPlan plan, int spawn)
        {
            ParkAt(s, Beside(plan.Spawns[spawn].Direction));
            s.ProvokeForTests(spawn);
        }

        // A point 6 m north of a wild spawn, close enough to provoke (Encounter.ProvokeRadiusM 18 m) but not on top of
        // it. Parking exactly on the spawn direction puts the target straight above/below the caster; the tangent toward
        // it is then float noise and trips MoveGeometry's unit-tangent Pre (see tech-debt D0-E4).
        private static float3 Beside(float3 spawnDirection)
        {
            float3 up = math.normalize(spawnDirection);
            float angle = 6f / Radius;
            return math.normalize(up * math.cos(angle) + PlanetFrame.FromUp(up).North * math.sin(angle));
        }

        // Moves the fielded party and the player together; nothing steps in between, so physics never sees the jump.
        private static void ParkAt(RunSession s, float3 direction)
        {
            s.RepositionPartyForTests(direction);
            MovePlayer(s, direction);
        }

        // Farthest point from every standing wild unit that is outside the quest region. Engaged wild units let go only
        // beyond Encounter.DisengageRadiusM (40 m), so it must beat that with margin.
        private static float3 Quiet(RunSession s, int excludedRegion)
        {
            var wild = new List<float3>();
            foreach (RunSession.UnitSnapshot u in s.UnitsForTests)
            {
                if (u.Side == EncounterSide.Wild && !u.IsFainted)
                {
                    wild.Add(s.DirectionForTests(u.Position));
                }
            }

            const int Candidates = 600;
            float golden = math.PI * (3f - math.sqrt(5f));
            float3 best = float3.zero;
            float bestDistance = -1f;
            for (int i = 0; i < Candidates; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / Candidates;
                float r = math.sqrt(1f - y * y);
                var d = new float3(math.cos(golden * i) * r, y, math.sin(golden * i) * r);
                if (s.RegionAtForTests(d) == excludedRegion)
                {
                    continue;
                }

                float nearest = float.MaxValue;
                foreach (float3 w in wild)
                {
                    nearest = math.min(nearest, math.acos(math.clamp(math.dot(d, w), -1f, 1f)) * Radius);
                }

                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = d;
                }
            }

            Assert.That(bestDistance, Is.GreaterThan(Encounter.DisengageRadiusM + 5f), "a quiet spot exists");
            return best;
        }

        // Takes a wild unit to 0 HP through the public combat API (an overwhelming hit in an element it does not resist).
        // The Faint event is then supplied by InjectFaintForTests because Encounter only emits it from inside Tick.
        private static void Kill(RunSession s, Env env, int unitId)
        {
            long now = s.NowMsForTests;
            var mastery = new int[10];
            for (int i = 0; i < mastery.Length; i++)
            {
                mastery[i] = DamageModel.MaxMasteryPercent;
            }

            Pcg32 rng = new Pcg32(99UL, 'T');
            for (int e = 0; e < 10 && !s.UnitForTests(unitId).IsFainted; e++)
            {
                var attacker = new Combatant(env.Balance.Stats, env.Balance.Effects, 0, 0, 0, mastery, new int[10], new EffectBook(), 1);
                var move = new CastMove("test_nuke", (Element)e, MoveKind.Damage, DamageModel.MaxPower, 100, 0, false, default, 0);
                Assert.That(attacker.BeginCast(0, move, now, out CastContext cast), Is.EqualTo(CastResult.Accepted));
                for (int hit = 0; hit < 64 && !s.UnitForTests(unitId).IsFainted; hit++)
                {
                    s.ReceiveHostileForTests(unitId, cast, ref rng);
                }
            }

            Assert.That(s.UnitForTests(unitId).IsFainted, Is.True, "unit " + unitId + " taken to 0 HP");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Session setup (same data and actions as RunSessionSmokeTests; no geo-templates so the plan is reproducible)
        // ---------------------------------------------------------------------------------------------------------

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        private static Env LoadEnv()
        {
            var db = Load<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var env = new Env
            {
                Copy = new DemoCopy("zh-TW", Load<TextAsset>(Copy + "strings.json").text, Load<TextAsset>(Copy + "names.json").text,
                    Load<TextAsset>(Copy + "affixes.json").text, Load<TextAsset>(Copy + "region-themes.json").text,
                    Load<TextAsset>(Copy + "quest-templates.json").text, Load<TextAsset>(Copy + "boss-epithets.json").text),
                Balance = CombatBalance.FromJson(Load<TextAsset>(Copy + "balance.json").text),
                Progression = Progression.FromJson(@"{""progression"":{
                    ""level_max"":100,""starter_level"":10,""xp_first_level_cost"":100,
                    ""xp_growth_numerator"":105,""xp_growth_denominator"":100,
                    ""evolution_level_bands"":{""juvenile"":[20,30],""adolescent"":[35,50]},
                    ""migration_stage_floor_levels"":[10,20,35],""appeared_weight"":4,""reserve_weight"":2,
                    ""rewards"":{""base_xp"":{""wild"":80,""capture"":40,""quest_step"":80,""quest_chain"":160,""boss"":640},
                    ""reference_level"":10,""elite_multiplier"":2,
                    ""wild_level"":{""boss_level_min"":19,""boss_level_max"":100,""per_tier"":2,
                    ""role_bonus"":{""normal"":0,""elite"":3,""boss"":5}},""sidekick_share_divisor"":2},
                    ""player"":{""xp_multiplier"":6,""boss_stage_unlock_levels"":[1,20,35]}}}"),
                Actions = Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions"),
            };
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c)
                {
                    (c.LineId.StartsWith("starter_") ? env.Starters : env.Wild).Add(c);
                }
                else if (d is PlanetTypeDef t && (env.Type == null || t.Id == "verdant_planet"))
                {
                    env.Type = t;
                }
            }

            env.Starters.RemoveAll(c => c.Stage != CreatureStage.Adult);
            Assert.That(env.Starters.Count, Is.GreaterThan(0), "adult starters");
            env.Vocab = new RunPlanVocabulary(env.Copy.ThemeIds, env.Copy.GenericAffixIds, env.Copy.ChainIds);
            return env;
        }

        // Staged-step cases disable the session after landing; the per-frame wiring regression keeps it enabled.
        private IEnumerator Launch(Env env, ulong seed, int startExp, Run run, ICreatureViewFactory views = null, int[] ids = null,
            System.Action<RunOutcome> onEnd = null, bool keepEnabled = false)
        {
            var party = new List<CreatureDef>();
            for (int i = 0; i < PartySize; i++)
            {
                party.Add(env.Starters[i % env.Starters.Count]);
            }

            var session = new GameObject("RunProgression").AddComponent<RunSession>();
            _live.Add(session.gameObject);
            run.Session = session;
            if (views != null)
            {
                session.UseViewFactory(views);
            }

            session.Configure(env.Copy, env.Balance, env.Actions, Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                new RunSetup
                {
                    PlanetType = env.Type, Seed = seed, Radius = Radius, BossStage = CreatureStage.Adult, Party = party,
                    PartyIds = (ids ?? Enumerable.Range(1, PartySize).ToArray()).ToList(),
                    PartyExp = Enumerable.Repeat(startExp, PartySize).ToList(),
                    Progression = env.Progression, Materials = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text),
                }, env.Wild, onEnd ?? (o => { }), Load<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader"),
                Load<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader"), Load<Shader>("Assets/_Project/Art/Shaders/Stars.shader"));

            float deadline = Time.realtimeSinceStartup + 60f;
            while (!session.StartedForTests && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(session.StartedForTests, Is.True, "terrain colliders under the landing point within 60 s");
            session.enabled = keepEnabled;
            // The tests teleport the character across the planet between frames; the camera's one-frame continuity
            // contract (ViewOrbit: up turns < 90° per step) would then fire in the frame between a test's end and the
            // deferred Destroy, and be reported against the next test.
            session.SuspendCameraForTests();
        }
    }
}
#endif
