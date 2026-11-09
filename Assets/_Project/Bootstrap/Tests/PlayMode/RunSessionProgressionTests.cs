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
            Encounter encounter = run.Session.EncounterForTests;

            Assert.That(views.Requests.Count, Is.EqualTo(encounter.Units.Count), "one view per encounter unit");
            for (int i = 0; i < encounter.Units.Count; i++)
            {
                EncounterUnit u = encounter.Units[i];
                Assert.That(views.Requests[i].Def, Is.SameAs(u.Def));
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

            ParkAt(s, Beside(plan.Spawns[e1].Direction));
            Step(s);
            Assert.That(s.TeamForTests.InCombat, Is.True, "battle 1 starts through real engagement");
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 0, 1 }), "opening pair");

            Kill(s, env, e1);
            ParkAt(s, Quiet(s, questRegion));
            int[] before = Exp(s);
            s.InjectFaintForTests(e1);
            s.InjectFaintForTests(e1);
            Step(s);
            Assert.That(s.TeamForTests.InCombat, Is.False, "battle 1 over");
            Assert.That(Delta(before, Exp(s)), Is.EqualTo(new[] { 20, 20, 10, 10, 10, 10 }), "duplicate faint pays {0,1} once");

            s.SwitchMainForTests();
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 1, 2 }), "slot 0 swapped for slot 2 out of combat");
            Kill(s, env, e2);
            ParkAt(s, Quiet(s, questRegion));
            before = Exp(s);
            s.InjectFaintForTests(e2);
            Step(s);
            Assert.That(s.TeamForTests.InCombat, Is.False, "battle 2 opened and closed inside one step");
            Assert.That(Delta(before, Exp(s)), Is.EqualTo(new[] { 10, 20, 20, 10, 10, 10 }), "battle 2 pays {1,2}, not battle 1's {0,1}");
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
            ParkAt(s, Beside(plan.Spawns[target].Direction));
            for (int i = 0; i < 2000; i++)
            {
                int[] before = Exp(s);
                Step(s);
                if (s.EncounterForTests.Events.Any(e => e.Kind == EncounterEventKind.Faint && e.TargetId == target))
                {
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
            Encounter enc = s.EncounterForTests;
            foreach (int id in s.PartyUnitsForTests)
            {
                Assert.That(enc.Units[id].Level, Is.EqualTo(10), "starts one XP short of LV11");
            }

            int pool = Pool(progression, BaseQuestStep, QuestLevel(env, plan, 0), false);
            int[] before = Exp(s);
            int[] levelsAtTick = null;
            s.BeforeTickForTests = () => levelsAtTick = s.PartyUnitsForTests.Select(id => enc.Units[id].Level).ToArray();
            ParkAt(s, Beside(plan.Spawns[spawn].Direction));
            Step(s);
            Assert.That(levelsAtTick, Is.EqualTo(Enumerable.Repeat(11, PartySize).ToArray()), "levels at the first Tick");

            Assert.That(s.TeamForTests.InCombat, Is.True, "this step starts a battle");
            Assert.That(Delta(before, Exp(s)), Is.EqualTo(Split(pool, new int[0])),
                "ReachPoi paid once, all-reserve: pool " + pool + " over weights 2×6");
            for (int slot = 0; slot < PartySize; slot++)
            {
                Assert.That(progression.LevelOf(s.PartyExpForTests(slot)), Is.EqualTo(11), "XP crossed T(11), slot " + slot);
                Assert.That(enc.Units[s.PartyUnitsForTests[slot]].Level, Is.EqualTo(11),
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

            ParkAt(s, Beside(plan.Spawns[e1].Direction));
            Step(s);
            Assert.That(s.TeamForTests.InCombat, Is.True, "battle in progress");
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 0, 1 }));

            s.SwitchMainForTests();
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 1, 2 }), "slot 2 swapped in for slot 0");
            // Only the player walks into the region; the fielded pair stays on the engaged pack, so the battle goes on.
            MovePlayer(s, plan.Regions[plan.Quest[0].Region].Center);
            int pool = Pool(env.Progression, BaseQuestStep, QuestLevel(env, plan, 0), false);
            int[] before = Exp(s);
            Step(s);

            Assert.That(s.TeamForTests.InCombat, Is.True, "still the same battle");
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
            ParkAt(s, Beside(plan.Spawns[a].Direction));
            Step(s);
            Assert.That(s.TeamForTests.InCombat, Is.True, "battle A");
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 0, 1 }));
            Kill(s, env, elite);
            s.InjectFaintForTests(elite);
            Step(s);
            Assert.That(s.TeamForTests.InCombat, Is.True, "battle A continues after the elite fell");
            Assert.That(s.QuestForTests.StepIndex, Is.EqualTo(0), "elite fell before the ReachPoi step");

            // Battle A ends; outside combat slot 0 is swapped for slot 2.
            Kill(s, env, a);
            ParkAt(s, Quiet(s, questRegion));
            s.InjectFaintForTests(a);
            Step(s);
            Assert.That(s.TeamForTests.InCombat, Is.False, "battle A over");
            s.SwitchMainForTests();
            Assert.That(ActiveSlots(s), Is.EqualTo(new[] { 1, 2 }));

            // Battle B: real engagement on another pack, then the party enters the quest region.
            ParkAt(s, Beside(plan.Spawns[b].Direction));
            Step(s);
            Assert.That(s.TeamForTests.InCombat, Is.True, "battle B");
            MovePlayer(s, plan.Regions[questRegion].Center);
            int[] before = Exp(s);
            Step(s);
            Assert.That(s.TeamForTests.InCombat, Is.True, "battle B continues in the region");
            Assert.That(s.QuestForTests.StepIndex, Is.EqualTo(1), "ReachPoi done, DefeatWild current");
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
            Assert.That(s.QuestForTests.StepIndex, Is.EqualTo(1));
            Assert.That(s.QuestForTests.Progress, Is.EqualTo(count - 1), "all but the last matching knock-out counted");

            int last = matching[count - 1];
            Kill(s, env, last);
            s.InjectFaintForTests(last);
            before = Exp(s);
            Step(s);
            Assert.That(s.QuestForTests.IsEarned(1), Is.True, "DefeatWild completed by the last knock-out");
            Assert.That(s.QuestForTests.IsEarned(2), Is.True, "DefeatElite credited retroactively (Q-E1)");
            Assert.That(s.QuestForTests.StepIndex, Is.EqualTo(3));

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
            RunPlanner.Generate(new PlanetConfig(Radius, float3.zero), seed, env.Type, env.Wild, env.Vocab, null);

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
            foreach (int unit in s.TeamForTests.Active)
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
                Assert.That(s.EncounterForTests.Units[i].Def.Id, Is.EqualTo(plan.Spawns[i].CreatureId), "spawn " + i);
            }
        }

        private static void MovePlayer(RunSession s, float3 direction)
        {
            PlanetRig rig = s.RigForTests;
            var rb = rig.Character.GetComponent<Rigidbody>();
            Vector3 at = rig.Config.ToWorld(direction, rig.Height.Sample(direction) + 1f);
            rb.position = at;
            rig.Character.transform.position = at;
        }

        // A point 6 m north of a wild spawn: inside Encounter.AggroRadiusM (18 m) so the pack engages, but not on top of
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
            Encounter enc = s.EncounterForTests;
            foreach (int id in s.PartyUnitsForTests)
            {
                if (!enc.Units[id].Fielded)
                {
                    continue;
                }

                enc.Unfield(id);
                enc.Field(id, direction, false, s.NowMsForTests);
            }

            MovePlayer(s, direction);
        }

        // Farthest point from every standing wild unit that is outside the quest region. Engaged wild units let go only
        // beyond Encounter.DisengageRadiusM (40 m), so it must beat that with margin.
        private static float3 Quiet(RunSession s, int excludedRegion)
        {
            PlanetRig rig = s.RigForTests;
            RunPlan plan = s.PlanForTests;
            var wild = new List<float3>();
            foreach (EncounterUnit u in s.EncounterForTests.Units)
            {
                if (u.Side == EncounterSide.Wild && !u.Combatant.IsFainted)
                {
                    wild.Add(rig.Config.ToDirection(u.Position));
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
                if (RunPlanner.RegionAt(plan, d) == excludedRegion)
                {
                    continue;
                }

                float nearest = float.MaxValue;
                foreach (float3 w in wild)
                {
                    nearest = math.min(nearest, math.acos(math.clamp(math.dot(d, w), -1f, 1f)) * rig.Config.Radius);
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
            Combatant target = s.EncounterForTests.Units[unitId].Combatant;
            long now = s.NowMsForTests;
            var mastery = new int[10];
            for (int i = 0; i < mastery.Length; i++)
            {
                mastery[i] = DamageModel.MaxMasteryPercent;
            }

            Pcg32 rng = new Pcg32(99UL, 'T');
            for (int e = 0; e < 10 && !target.IsFainted; e++)
            {
                var attacker = new Combatant(env.Balance.Stats, env.Balance.Effects, 0, 0, 0, mastery, new int[10], new EffectBook(), 1);
                var move = new CastMove("test_nuke", (Element)e, MoveKind.Damage, DamageModel.MaxPower, 100, 0, false, default, 0);
                Assert.That(attacker.BeginCast(0, move, now, out CastContext cast), Is.EqualTo(CastResult.Accepted));
                for (int hit = 0; hit < 64 && !target.IsFainted; hit++)
                {
                    target.ReceiveHostile(cast, true, ref rng, now);
                }
            }

            Assert.That(target.IsFainted, Is.True, "unit " + unitId + " taken to 0 HP");
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
                    ""wild_level"":{""base"":10,""per_difficulty"":10,""per_tier"":2,
                    ""role_bonus"":{""normal"":0,""elite"":3,""boss"":5}},""sidekick_share_divisor"":2}}}"),
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

        // Six main slots (the starters repeat if the data has fewer than six), equal starting XP. The session is
        // disabled as soon as it has landed: Update would otherwise run its own ticks between the test's steps.
        private IEnumerator Launch(Env env, ulong seed, int startExp, Run run, ICreatureViewFactory views = null, int[] ids = null)
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
                    PlanetType = env.Type, Seed = seed, Radius = Radius, Party = party,
                    PartyIds = (ids ?? Enumerable.Range(1, PartySize).ToArray()).ToList(),
                    PartyExp = Enumerable.Repeat(startExp, PartySize).ToList(),
                    Progression = env.Progression, Materials = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text),
                }, env.Wild, o => { }, Load<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader"),
                Load<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader"), Load<Shader>("Assets/_Project/Art/Shaders/Stars.shader"));

            float deadline = Time.realtimeSinceStartup + 60f;
            while (!session.StartedForTests && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(session.StartedForTests, Is.True, "terrain colliders under the landing point within 60 s");
            session.enabled = false;
            // The tests teleport the character across the planet between frames; the camera's one-frame continuity
            // contract (ViewOrbit: up turns < 90° per step) would then fire in the frame between a test's end and the
            // deferred Destroy, and be reported against the next test.
            session.RigForTests.CameraRig.enabled = false;
        }
    }
}
#endif
