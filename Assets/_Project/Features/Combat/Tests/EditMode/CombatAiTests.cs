using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Combat.Tests
{
    // Self stands on an R = 640 m planet with an off-origin center; everything else is placed by geodesic distance and
    // heading from self, the coordinates CombatAi promises to measure in.
    public sealed class CombatAiTests
    {
        private const float R = 640f;
        private const double TolM = 0.01;
        private PlanetConfig _planet;
        private AiTuning _tuning;
        private float3 _up;
        private float3 _forward;
        private float3 _self;

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(R, new float3(10f, -20f, 30f));
            _tuning = new AiTuning(800, 500, 900, 6000);
            _up = math.normalize(new float3(0.3f, 0.8f, -0.5f));
            _self = _planet.Center + _up * R;
            _forward = math.normalize(math.cross(_up, new float3(1f, 0f, 0f)));
        }

        private float3 At(float distanceM, float headingDeg)
        {
            float3 right = math.cross(_up, _forward);
            float h = math.radians(headingDeg);
            float3 planar = math.cos(h) * _forward + math.sin(h) * right;
            float a = distanceM / R;
            return _planet.Center + (math.cos(a) * _up + math.sin(a) * planar) * R;
        }

        private float3 Dir(float3 world) => math.normalize(world - _planet.Center);

        private double DistM(float3 dirA, float3 dirB)
        {
            double3 a = dirA, b = dirB;
            return math.atan2(math.length(math.cross(a, b)), math.dot(a, b)) * R;
        }

        // Unit tangent at `pos` pointing toward `toward`.
        private float3 Facing(float3 pos, float3 toward)
        {
            float3 up = Dir(pos);
            float3 d = toward - pos;
            return math.normalize(d - up * math.dot(d, up));
        }

        private AiEnemy Enemy(int id, float3 pos, bool alive = true) => new AiEnemy(id, pos, Facing(pos, _self), alive);

        private static AiMoveOption Hostile(int slot, int reachMm, int power, bool ready = true) =>
            new AiMoveOption(slot, AiMoveTargeting.Hostile, reachMm, power, ready);

        private AiInput Input(AiStyle style = AiStyle.Hold, AiRole role = AiRole.Main, AiCommand? command = null,
            int mainTarget = CombatAi.NoTarget, float3? player = null, float3? partner = null)
        {
            return new AiInput(_self, style, role, command ?? new AiCommand(AiCommandKind.Free, CombatAi.NoTarget),
                mainTarget, player ?? _self, partner.HasValue, partner ?? default);
        }

        private AiDecision Decide(in AiInput input, IReadOnlyList<AiEnemy> enemies, IReadOnlyList<AiMoveOption> moves)
        {
            AiDecision d = CombatAi.Decide(_planet, _tuning, input, enemies, moves);
            Assert.AreEqual(1.0, math.length((double3)d.DestinationDirection), 1e-5, "destination must be unit length");
            return d;
        }

        private static AiCommand Cmd(AiCommandKind kind, int target = CombatAi.NoTarget) => new AiCommand(kind, target);

        // ---- command / role ----

        [Test]
        public void Retreat_walks_to_the_player_and_never_casts()
        {
            float3 player = At(30f, 90f);
            AiDecision d = Decide(Input(command: Cmd(AiCommandKind.Retreat), player: player),
                new[] { Enemy(1, At(5f, 0f)) }, new[] { Hostile(0, 12000, 50) });
            Assert.AreEqual(CombatAi.NoTarget, d.TargetId);
            Assert.AreEqual(CombatAi.NoCast, d.CastSlot);
            Assert.Less(DistM(d.DestinationDirection, Dir(player)), 1e-3);
        }

        [Test]
        public void Support_ignores_the_player_command()
        {
            AiDecision d = Decide(Input(role: AiRole.Support, command: Cmd(AiCommandKind.Retreat)),
                new[] { Enemy(1, At(5f, 0f)) }, new[] { Hostile(0, 12000, 50) });
            Assert.AreEqual(1, d.TargetId);
            Assert.AreEqual(0, d.CastSlot);
        }

        [Test]
        public void Free_targets_the_nearest_living_enemy_and_ties_go_to_the_smaller_id()
        {
            float3 tie = At(10f, 0f);
            AiEnemy[] enemies = { Enemy(7, tie), Enemy(3, At(4f, 180f), alive: false), Enemy(5, tie), Enemy(9, At(20f, 0f)) };
            AiDecision d = Decide(Input(), enemies, new[] { Hostile(0, 2000, 10) });
            Assert.AreEqual(5, d.TargetId);
        }

        [Test]
        public void Focus_targets_the_commanded_enemy_while_it_lives()
        {
            AiEnemy near = Enemy(1, At(3f, 0f));
            AiDecision d = Decide(Input(command: Cmd(AiCommandKind.Focus, 2)), new[] { near, Enemy(2, At(30f, 0f)) },
                new[] { Hostile(0, 2000, 10) });
            Assert.AreEqual(2, d.TargetId);
            Assert.AreEqual(CombatAi.NoCast, d.CastSlot, "focused target is out of reach");

            d = Decide(Input(command: Cmd(AiCommandKind.Focus, 2)), new[] { near, Enemy(2, At(30f, 0f), alive: false) },
                new[] { Hostile(0, 2000, 10) });
            Assert.AreEqual(1, d.TargetId, "dead focus falls back to Free");

            d = Decide(Input(command: Cmd(AiCommandKind.Focus, 42)), new[] { near }, new[] { Hostile(0, 2000, 10) });
            Assert.AreEqual(1, d.TargetId, "absent focus falls back to Free");
        }

        [Test]
        public void Support_prefers_the_main_target_and_main_ignores_it()
        {
            AiEnemy[] enemies = { Enemy(1, At(3f, 0f)), Enemy(2, At(30f, 0f)) };
            Assert.AreEqual(2, Decide(Input(role: AiRole.Support, mainTarget: 2), enemies, new[] { Hostile(0, 2000, 1) }).TargetId);
            Assert.AreEqual(1, Decide(Input(role: AiRole.Main, mainTarget: 2), enemies, new[] { Hostile(0, 2000, 1) }).TargetId);
            Assert.AreEqual(1, Decide(Input(role: AiRole.Support), enemies, new[] { Hostile(0, 2000, 1) }).TargetId);

            AiEnemy[] mainDead = { Enemy(1, At(3f, 0f)), Enemy(2, At(30f, 0f), alive: false) };
            Assert.AreEqual(1, Decide(Input(role: AiRole.Support, mainTarget: 2), mainDead, new[] { Hostile(0, 2000, 1) }).TargetId);
        }

        [Test]
        public void Support_with_a_focus_command_still_does_not_hear_it()
        {
            AiEnemy[] enemies = { Enemy(1, At(3f, 0f)), Enemy(2, At(30f, 0f)) };
            AiDecision d = Decide(Input(role: AiRole.Support, command: Cmd(AiCommandKind.Focus, 2)), enemies,
                new[] { Hostile(0, 2000, 1) });
            Assert.AreEqual(1, d.TargetId);
        }

        // ---- cast choice ----

        [Test]
        public void Cast_picks_the_strongest_ready_hostile_in_reach()
        {
            AiEnemy[] e = { Enemy(1, At(10f, 0f)) };
            AiMoveOption[] moves =
            {
                Hostile(0, 12000, 40), Hostile(1, 12000, 90, ready: false), Hostile(2, 9000, 200), Hostile(3, 24000, 60),
            };
            Assert.AreEqual(3, Decide(Input(), e, moves).CastSlot);
        }

        [Test]
        public void Equal_power_goes_to_the_smaller_slot_regardless_of_order()
        {
            AiEnemy[] e = { Enemy(1, At(1f, 0f)) };
            Assert.AreEqual(2, Decide(Input(), e, new[] { Hostile(5, 2000, 30), Hostile(2, 2000, 30) }).CastSlot);
            Assert.AreEqual(2, Decide(Input(), e, new[] { Hostile(2, 2000, 30), Hostile(5, 2000, 30) }).CastSlot);
        }

        [Test]
        public void Reach_gate_is_inclusive()
        {
            Assert.AreEqual(0, Decide(Input(), new[] { Enemy(1, At(11.99f, 0f)) }, new[] { Hostile(0, 12000, 1) }).CastSlot);
            Assert.AreEqual(CombatAi.NoCast,
                Decide(Input(), new[] { Enemy(1, At(12.02f, 0f)) }, new[] { Hostile(0, 12000, 1) }).CastSlot);
        }

        [Test]
        public void Support_moves_wait_for_no_hostile_option_and_need_a_target()
        {
            var self = new AiMoveOption(4, AiMoveTargeting.Self, 0, 0, true);
            var ally = new AiMoveOption(1, AiMoveTargeting.Ally, 12000, 0, true);
            AiEnemy[] near = { Enemy(1, At(1f, 0f)) };
            AiEnemy[] far = { Enemy(1, At(30f, 0f)) };

            Assert.AreEqual(0, Decide(Input(), near, new[] { self, Hostile(0, 2000, 0) }).CastSlot, "hostile first, even power 0");
            Assert.AreEqual(4, Decide(Input(), far, new[] { self, Hostile(0, 2000, 10) }).CastSlot);
            Assert.AreEqual(4, Decide(Input(), far, new[] { self, ally }).CastSlot, "ally without partner is skipped");
            Assert.AreEqual(1, Decide(Input(partner: At(11.99f, 90f)), far, new[] { self, ally }).CastSlot, "smaller slot");
            Assert.AreEqual(4, Decide(Input(partner: At(12.05f, 90f)), far, new[] { self, ally }).CastSlot, "partner out of reach");
            Assert.AreEqual(CombatAi.NoCast, Decide(Input(), new AiEnemy[0], new[] { self, ally }).CastSlot, "no target, no cast");
            Assert.AreEqual(CombatAi.NoCast,
                Decide(Input(), far, new[] { new AiMoveOption(4, AiMoveTargeting.Self, 0, 0, false) }).CastSlot);
        }

        // ---- positioning ----

        [Test]
        public void Keep_distance_stands_at_its_fraction_of_the_longest_reach_on_its_own_side()
        {
            float3 target = At(5f, 0f);
            AiDecision d = Decide(Input(AiStyle.KeepDistance), new[] { Enemy(1, target) },
                new[] { Hostile(0, 2000, 1), Hostile(1, 24000, 1, ready: false) });
            Assert.AreEqual(19.2, DistM(d.DestinationDirection, Dir(target)), TolM);
            Assert.AreEqual(14.2, DistM(d.DestinationDirection, Dir(_self)), TolM, "on the great circle, behind self");
        }

        [Test]
        public void Charge_closes_to_its_fraction_of_the_shortest_reach()
        {
            float3 target = At(20f, 45f);
            AiDecision d = Decide(Input(AiStyle.Charge), new[] { Enemy(1, target) },
                new[] { Hostile(0, 4000, 1), Hostile(1, 24000, 1) });
            Assert.AreEqual(2.0, DistM(d.DestinationDirection, Dir(target)), TolM);
            Assert.AreEqual(18.0, DistM(d.DestinationDirection, Dir(_self)), TolM);
        }

        [Test]
        public void Flank_stands_behind_the_target()
        {
            float3 target = At(20f, 0f);
            // Target faces away from self, so "behind" it is the side toward self.
            var awayFacing = new AiEnemy(1, target, -Facing(target, _self), true);
            AiDecision d = Decide(Input(AiStyle.Flank), new[] { awayFacing }, new[] { Hostile(0, 4000, 1) });
            Assert.AreEqual(2.0, DistM(d.DestinationDirection, Dir(target)), TolM);
            Assert.AreEqual(18.0, DistM(d.DestinationDirection, Dir(_self)), TolM);

            // Facing self: behind is the far side.
            d = Decide(Input(AiStyle.Flank), new[] { Enemy(1, target) }, new[] { Hostile(0, 4000, 1) });
            Assert.AreEqual(2.0, DistM(d.DestinationDirection, Dir(target)), TolM);
            Assert.AreEqual(22.0, DistM(d.DestinationDirection, Dir(_self)), TolM);
        }

        [Test]
        public void Hold_stays_inside_its_fraction_and_closes_in_from_outside()
        {
            AiMoveOption[] moves = { Hostile(0, 12000, 1) };
            AiDecision d = Decide(Input(AiStyle.Hold), new[] { Enemy(1, At(10.79f, 0f)) }, moves);
            Assert.Less(DistM(d.DestinationDirection, Dir(_self)), 1e-3);

            float3 far = At(20f, 0f);
            d = Decide(Input(AiStyle.Hold), new[] { Enemy(1, far) }, moves);
            Assert.AreEqual(10.8, DistM(d.DestinationDirection, Dir(far)), TolM);
            Assert.AreEqual(9.2, DistM(d.DestinationDirection, Dir(_self)), TolM);
        }

        [Test]
        public void Standing_on_the_target_falls_back_to_its_facing()
        {
            var onSelf = new AiEnemy(1, _self, _forward, true);
            AiDecision d = Decide(Input(AiStyle.KeepDistance), new[] { onSelf }, new[] { Hostile(0, 10000, 1) });
            Assert.AreEqual(8.0, DistM(d.DestinationDirection, Dir(_self)), TolM);
            Assert.Greater(math.dot(d.DestinationDirection - Dir(_self), _forward), 0f, "in front of the target");
        }

        [Test]
        public void Follow_stays_in_the_leash_and_casts_only_what_reaches()
        {
            float3 nearPlayer = At(5f, 90f);
            AiEnemy[] e = { Enemy(1, At(1f, 0f)), Enemy(2, At(40f, 0f)) };
            AiDecision d = Decide(Input(AiStyle.Charge, command: Cmd(AiCommandKind.Follow), player: nearPlayer), e,
                new[] { Hostile(0, 2000, 1) });
            Assert.AreEqual(1, d.TargetId);
            Assert.AreEqual(0, d.CastSlot);
            Assert.Less(DistM(d.DestinationDirection, Dir(_self)), 1e-3, "inside leash: stay");

            float3 farPlayer = At(30f, 90f);
            d = Decide(Input(AiStyle.Charge, command: Cmd(AiCommandKind.Follow), player: farPlayer), e,
                new[] { Hostile(0, 2000, 1) });
            Assert.AreEqual(3.0, DistM(d.DestinationDirection, Dir(farPlayer)), TolM, "half the leash from the player");
            Assert.AreEqual(27.0, DistM(d.DestinationDirection, Dir(_self)), TolM);
        }

        [Test]
        public void Without_targets_or_hostile_moves_the_creature_keeps_to_the_leash()
        {
            float3 farPlayer = At(30f, 90f);
            AiDecision d = Decide(Input(AiStyle.Charge, player: farPlayer), new[] { Enemy(1, At(1f, 0f), alive: false) },
                new[] { Hostile(0, 2000, 1) });
            Assert.AreEqual(CombatAi.NoTarget, d.TargetId);
            Assert.AreEqual(CombatAi.NoCast, d.CastSlot);
            Assert.AreEqual(3.0, DistM(d.DestinationDirection, Dir(farPlayer)), TolM);

            d = Decide(Input(AiStyle.Charge, player: farPlayer), new[] { Enemy(1, At(40f, 0f)) },
                new[] { new AiMoveOption(0, AiMoveTargeting.Self, 0, 0, true) });
            Assert.AreEqual(1, d.TargetId);
            Assert.AreEqual(0, d.CastSlot);
            Assert.AreEqual(3.0, DistM(d.DestinationDirection, Dir(farPlayer)), TolM);
        }

        [Test]
        public void Same_inputs_give_the_same_decision_and_enemy_order_does_not_matter()
        {
            AiEnemy a = Enemy(1, At(8f, 10f)), b = Enemy(2, At(9f, -40f)), c = Enemy(3, At(15f, 100f));
            AiMoveOption[] moves = { Hostile(0, 12000, 5), Hostile(1, 24000, 9) };
            foreach (AiStyle style in new[] { AiStyle.KeepDistance, AiStyle.Flank, AiStyle.Charge, AiStyle.Hold })
            {
                AiDecision x = Decide(Input(style), new[] { a, b, c }, moves);
                AiDecision y = Decide(Input(style), new[] { c, b, a }, moves);
                Assert.AreEqual(x.TargetId, y.TargetId);
                Assert.AreEqual(x.CastSlot, y.CastSlot);
                Assert.AreEqual(x.DestinationDirection, y.DestinationDirection);
            }
        }

        // ---- ReachMmOf ----

        [Test]
        public void Reach_of_each_volume_family()
        {
            var stats = new DerivedStats(BalanceFixture.Coefficients());
            HitVolumeDefaults d = BalanceFixture.HitDefaults();
            HitVolume Build(MoveShape s, RangeClass rc, int mm, BurstOrigin o = BurstOrigin.Caster) =>
                MoveGeometry.Build(s, MoveMovement.None, rc, o, mm, 50, stats, d);

            Assert.AreEqual(0, CombatAi.ReachMmOf(Build(MoveShape.Self, RangeClass.Melee, 0)));
            HitVolume ally = Build(MoveShape.Ally, RangeClass.Mid, 12000);
            Assert.AreEqual(ally.CastDistanceMm, CombatAi.ReachMmOf(ally));
            HitVolume line = Build(MoveShape.Line, RangeClass.Long, 24000);
            double lineHalf = line.WidthMm * 0.5;
            Assert.AreEqual((int)math.ceil(math.sqrt((double)line.ReachMm * line.ReachMm + lineHalf * lineHalf)),
                CombatAi.ReachMmOf(line));
            HitVolume cone = Build(MoveShape.Cone, RangeClass.Mid, 12000);
            Assert.AreEqual(cone.ReachMm, CombatAi.ReachMmOf(cone));
            HitVolume field = Build(MoveShape.Field, RangeClass.Long, 24000);
            Assert.AreEqual(field.CastDistanceMm + field.ReachMm, CombatAi.ReachMmOf(field));
            HitVolume burst = Build(MoveShape.Burst, RangeClass.Mid, 3000, BurstOrigin.Target);
            Assert.AreEqual(HitVolumeAnchor.AimPoint, burst.Anchor);
            Assert.AreEqual(burst.CastDistanceMm + burst.ReachMm, CombatAi.ReachMmOf(burst));
            HitVolume own = Build(MoveShape.Burst, RangeClass.Melee, 3000);
            Assert.AreEqual(own.ReachMm, CombatAi.ReachMmOf(own));
        }

        [Test]
        public void Shipped_tuning_loads()
        {
            AiTuning t = BalanceFixture.AiTuning();
            Assert.That(t.KeepDistancePermille, Is.InRange(1, 1000));
            Assert.That(t.FollowLeashMm, Is.GreaterThan(0));
        }

        // ---- SOL P2-10 r1 witnesses ----

        [Test]
        public void A1_rect_gate_covers_its_far_corners()
        {
            var stats = new DerivedStats(BalanceFixture.Coefficients());
            HitVolume front = MoveGeometry.Build(MoveShape.Front, MoveMovement.None, RangeClass.Melee, BurstOrigin.Caster,
                2000, 0, stats, BalanceFixture.HitDefaults());
            double half = front.WidthMm * 0.5;
            Assert.AreEqual((int)math.ceil(math.sqrt((double)front.ReachMm * front.ReachMm + half * half)),
                CombatAi.ReachMmOf(front));

            float x = (float)(half * 0.93 / 1000.0), z = front.ReachMm * 0.995f / 1000f;
            float3 corner = At(math.sqrt(x * x + z * z), math.degrees(math.atan2(x, z)));
            Assert.Greater(DistM(Dir(corner), Dir(_self)), front.ReachMm / 1000.0, "beyond the plain length");
            Assert.IsTrue(MoveGeometry.Overlaps(front, _planet, _self, _forward, corner, 0f), "oracle: it connects");
            AiDecision d = Decide(Input(), new[] { Enemy(1, corner) },
                new[] { Hostile(0, CombatAi.ReachMmOf(front), 10) });
            Assert.AreEqual(0, d.CastSlot);
        }

        [Test]
        public void A1_reach_saturates()
        {
            var huge = new HitVolume(HitVolumeKind.Circle, HitVolumeAnchor.AimPoint, int.MaxValue, 0, 0, 1, int.MaxValue, 0);
            Assert.AreEqual(int.MaxValue, CombatAi.ReachMmOf(huge));
            var rect = new HitVolume(HitVolumeKind.Rect, HitVolumeAnchor.Caster, int.MaxValue, int.MaxValue, 0, 1, 0, 0);
            Assert.AreEqual(int.MaxValue, CombatAi.ReachMmOf(rect));
        }

        [Test]
        public void A3_stand_off_is_clamped_to_a_quarter_circumference()
        {
            float3 target = At(5f, 0f);
            AiDecision d = Decide(Input(AiStyle.KeepDistance), new[] { Enemy(1, target) }, new[] { Hostile(0, 3000000, 1) });
            Assert.AreEqual(math.PI_DBL * 0.5 * R, DistM(d.DestinationDirection, Dir(target)), TolM);
            Assert.AreEqual(math.PI_DBL * 0.5 * R - 5.0, DistM(d.DestinationDirection, Dir(_self)), TolM, "on self's side");
        }

        [Test]
        public void A4_a_tiny_but_real_offset_keeps_its_great_circle()
        {
            var planet = new PlanetConfig(R, float3.zero);
            var self = new float3(0f, R, 0f);
            var enemy = new AiEnemy(1, new float3(0f, R, 1e-10f), new float3(1f, 0f, 0f), true);
            var input = new AiInput(self, AiStyle.KeepDistance, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget,
                self, false, default);
            AiDecision d = CombatAi.Decide(planet, _tuning, input, new[] { enemy }, new[] { Hostile(0, 10000, 1) });
            Assert.Less(math.abs(d.DestinationDirection.x), 1e-6f, "not sent along the target's facing");
            Assert.Less(d.DestinationDirection.z, -0.012f, "8 m toward self's side of the target");
        }

        [Test]
        public void A5_decide_does_not_allocate()
        {
            AiEnemy[] e = { Enemy(1, At(5f, 0f)), Enemy(2, At(9f, 40f)) };
            AiMoveOption[] m = { Hostile(0, 12000, 5), new AiMoveOption(1, AiMoveTargeting.Ally, 12000, 0, true) };
            AiInput input = Input(AiStyle.Flank, partner: At(3f, 90f));
            for (int i = 0; i < 100; i++) CombatAi.Decide(_planet, _tuning, input, e, m);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) CombatAi.Decide(_planet, _tuning, input, e, m);
            // Read the counter before Assert boxes its arguments, or the boxing itself is measured (SOL r2).
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
        }

        [Test]
        public void A7_tuning_values_drive_the_result()
        {
            _tuning = new AiTuning(400, 250, 500, 2000);
            float3 target = At(5f, 0f);
            AiDecision d = Decide(Input(AiStyle.KeepDistance), new[] { Enemy(1, target) }, new[] { Hostile(0, 24000, 1) });
            Assert.AreEqual(9.6, DistM(d.DestinationDirection, Dir(target)), TolM);
            d = Decide(Input(AiStyle.Charge), new[] { Enemy(1, At(20f, 0f)) }, new[] { Hostile(0, 4000, 1) });
            Assert.AreEqual(1.0, DistM(d.DestinationDirection, Dir(At(20f, 0f))), TolM);
            d = Decide(Input(AiStyle.Hold), new[] { Enemy(1, At(8f, 0f)) }, new[] { Hostile(0, 12000, 1) });
            Assert.AreEqual(6.0, DistM(d.DestinationDirection, Dir(At(8f, 0f))), TolM);
            float3 player = At(5f, 90f);
            d = Decide(Input(command: Cmd(AiCommandKind.Follow), player: player), new AiEnemy[0], new[] { Hostile(0, 1, 1) });
            Assert.AreEqual(1.0, DistM(d.DestinationDirection, Dir(player)), TolM);
        }

        [Test]
        public void A7_reach_and_hold_boundaries()
        {
            Assert.AreEqual(0, Decide(Input(), new[] { new AiEnemy(1, _self, _forward, true) }, new[] { Hostile(0, 0, 1) }).CastSlot,
                "zero reach still hits an enemy on the same spot (inclusive gate)");
            Assert.AreEqual(CombatAi.NoCast,
                Decide(Input(), new[] { Enemy(1, At(12.002f, 0f)) }, new[] { Hostile(0, 12000, 1) }).CastSlot);
            AiDecision d = Decide(Input(AiStyle.Hold), new[] { Enemy(1, At(10.802f, 0f)) }, new[] { Hostile(0, 12000, 1) });
            Assert.Greater(DistM(d.DestinationDirection, Dir(_self)), 1e-3, "just outside the hold zone: move");
        }

        // SOL r2: the 2 mm offsets above let a gate widened by 1 mm survive; these sit inside that 1 mm.
        [Test]
        public void A7_sub_millimetre_boundaries()
        {
            float3 justOut = At(12.0005f, 0f);
            double dOut = DistM(Dir(justOut), Dir(_self));
            Assert.That(dOut, Is.GreaterThan(12.0).And.LessThan(12.001), "fixture precondition");
            Assert.AreEqual(CombatAi.NoCast, Decide(Input(), new[] { Enemy(1, justOut) }, new[] { Hostile(0, 12000, 1) }).CastSlot);

            float3 holdOut = At(10.8005f, 0f);
            double dHold = DistM(Dir(holdOut), Dir(_self));
            Assert.That(dHold, Is.GreaterThan(10.8).And.LessThan(10.801), "fixture precondition");
            AiDecision d = Decide(Input(AiStyle.Hold), new[] { Enemy(1, holdOut) }, new[] { Hostile(0, 12000, 1) });
            Assert.Greater(DistM(d.DestinationDirection, Dir(_self)), 1e-5, "outside the hold zone by < 1 mm: still moves");
        }

        [Test]
        public void A4_a_real_offset_across_radii_keeps_its_great_circle()
        {
            var planet = new PlanetConfig(R, float3.zero);
            var self = new float3(0f, R, 0f);
            var enemy = new AiEnemy(1, new float3(0f, 2f * R, 1e-10f), new float3(1f, 0f, 0f), true);
            var input = new AiInput(self, AiStyle.KeepDistance, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget,
                self, false, default);
            AiDecision d = CombatAi.Decide(planet, _tuning, input, new[] { enemy }, new[] { Hostile(0, 10000, 1) });
            Assert.Less(math.abs(d.DestinationDirection.x), 1e-6f, "not sent along the target's facing");
            Assert.Less(d.DestinationDirection.z, -0.012f);
        }

        [Test]
        public void A7_large_stand_off_multiplies_in_double()
        {
            float r2 = 2f * R;
            float3 self = _planet.Center + _up * r2;
            float a = 20f / r2;
            float3 target = _planet.Center + (math.cos(a) * _up + math.sin(a) * _forward) * r2;
            var input = new AiInput(self, AiStyle.Charge, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget, self, false, default);
            AiDecision d = CombatAi.Decide(_planet, _tuning, input, new[] { new AiEnemy(1, target, Facing(target, self), true) },
                new[] { Hostile(0, 5000000, 1) });
            double3 dd = d.DestinationDirection, td = Dir(target);
            double angle = math.atan2(math.length(math.cross(dd, td)), math.dot(dd, td));
            Assert.AreEqual(math.PI_DBL * 0.5, angle, 1e-5, "5000 km × 0.5 clamps to a quarter circle, no int overflow");
        }

        // SOL r2 M32/M33: subtracting a 16 km center in float flips these reach gates.
        [TestCase(1.00425875f, 38.0317574f, 37.29118f, 48402, 0)]
        [TestCase(1.22399712f, 38.99483f, -31.5966225f, 51279, CombatAi.NoCast)]
        public void A7_far_center_gates_subtract_in_double(float tx, float ty, float tz, int reachMm, int expectedSlot)
        {
            var planet = new PlanetConfig(8000f, new float3(16000f, 0f, 0f));
            var self = new float3(0.003008881f, 2.503535f, 4.424855f);
            var target = new float3(tx, ty, tz);
            float3 up = planet.ToDirection(target);
            float3 toward = self - target;
            float3 fwd = math.normalize(toward - up * math.dot(toward, up));
            var input = new AiInput(self, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget, self, false, default);
            AiDecision d = CombatAi.Decide(planet, _tuning, input, new[] { new AiEnemy(1, target, fwd, true) },
                new[] { Hostile(0, reachMm, 1) });
            Assert.AreEqual(expectedSlot, d.CastSlot);
        }

        // SOL r3: an exactly antipodal or radially aligned target left only an almost radial projection residual.
        [TestCase(-1f, -640f, AiStyle.KeepDistance, 8.0)]
        [TestCase(-1f, -640f, AiStyle.Charge, 5.0)]
        [TestCase(-1f, -640f, AiStyle.Hold, 9.0)]
        [TestCase(0.5f, 320f, AiStyle.KeepDistance, 8.0)]
        [TestCase(0.5f, 320f, AiStyle.Charge, 5.0)]
        public void A4_degenerate_alignment_still_keeps_the_stand_off(float tx, float ty, AiStyle style, double expectedM)
        {
            var planet = new PlanetConfig(R, float3.zero);
            var self = new float3(1f, 640f, 0f);
            var enemy = new AiEnemy(1, new float3(tx, ty, 0f), new float3(0f, 0f, 1f), true);
            var input = new AiInput(self, style, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget, self, false, default);
            AiDecision d = CombatAi.Decide(planet, _tuning, input, new[] { enemy }, new[] { Hostile(0, 10000, 1) });
            double3 dd = d.DestinationDirection, td = math.normalize((double3)enemy.Position);
            double angle = math.atan2(math.length(math.cross(dd, td)), math.dot(dd, td));
            Assert.AreEqual(expectedM, angle * math.length((double3)self), TolM);
            Assert.AreEqual(1.0, math.length(dd), 1e-6);
        }

        // SOL r4: normalizing the target before the cross product picked the wrong great circle (11 m off).
        [TestCase(640f, 3f, 640f, 1.5f, 320f, 1e-20f)]
        [TestCase(640f, 3f, 640f, -3f, -640f, 1e-20f)]
        [TestCase(8000f, 7874f, 12272f, 3937f, 6136f, 1e-10f)]
        public void A4_tiny_offsets_stay_on_the_true_great_circle(float planetR, float sx, float sy, float tx, float ty, float tz)
        {
            var planet = new PlanetConfig(planetR, float3.zero);
            var self = new float3(sx, sy, 0f);
            var enemy = new AiEnemy(1, new float3(tx, ty, tz), new float3(0f, 0f, 1f), true);
            var input = new AiInput(self, AiStyle.KeepDistance, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget,
                self, false, default);
            AiDecision d = CombatAi.Decide(planet, _tuning, input, new[] { enemy }, new[] { Hostile(0, 10000, 1) });

            double3 n = math.normalize(math.cross((double3)enemy.Position, (double3)self));
            double r = math.length((double3)self);
            Assert.Less(math.abs(math.dot((double3)d.DestinationDirection, n)) * r, 1e-3, "on the self/target plane");
            double3 dd = d.DestinationDirection, td = math.normalize((double3)enemy.Position);
            Assert.AreEqual(8.0, math.atan2(math.length(math.cross(dd, td)), math.dot(dd, td)) * r, TolM);
        }

        [Test]
        public void A1_rect_corner_is_an_exact_ceiling()
        {
            var rect = new HitVolume(HitVolumeKind.Rect, HitVolumeAnchor.Caster, 1000000000, 1, 0, 1, 0, 0);
            Assert.AreEqual(1000000001, CombatAi.ReachMmOf(rect));
            var square = new HitVolume(HitVolumeKind.Rect, HitVolumeAnchor.Caster, 3000, 8000, 0, 1, 0, 0);
            Assert.AreEqual(5000, CombatAi.ReachMmOf(square), "3-4-5: exact root is not bumped");
        }

        [Test]
        public void A7_distances_use_the_caster_radius()
        {
            float r2 = 2f * R;
            float3 self = _planet.Center + _up * r2;
            float a = 20f / r2;
            float3 target = _planet.Center + (math.cos(a) * _up + math.sin(a) * _forward) * r2;
            var input = new AiInput(self, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget, self, false, default);
            AiDecision d = CombatAi.Decide(_planet, _tuning, input, new[] { new AiEnemy(1, target, Facing(target, self), true) },
                new[] { Hostile(0, 12000, 1) });
            Assert.AreEqual(CombatAi.NoCast, d.CastSlot, "20 m at 1280 m radius is out of a 12 m reach");
        }

        [Test]
        public void A7_retreat_still_validates_inputs()
        {
            float3 pos = At(5f, 0f);
            Assert.Throws<ContractException>(() => Decide(Input(command: Cmd(AiCommandKind.Retreat)),
                new[] { new AiEnemy(1, pos, Dir(pos), true) }, new[] { Hostile(0, 1, 1) }));
            Assert.Throws<ContractException>(() => Decide(Input(command: Cmd(AiCommandKind.Retreat)),
                new[] { Enemy(1, pos) }, new[] { Hostile(0, 1, 1), Hostile(0, 2, 1) }));
        }

        [Test]
        public void A7_remaining_constructor_bounds()
        {
            Assert.Throws<ContractException>(() => _ = new AiTuning(800, 1001, 900, 1));
            Assert.Throws<ContractException>(() => _ = new AiTuning(800, 500, 0, 1));
            Assert.Throws<ContractException>(() => _ = new AiMoveOption(0, (AiMoveTargeting)(-1), 1, 1, true));
            Assert.Throws<ContractException>(() => _ = new AiCommand((AiCommandKind)(-1), CombatAi.NoTarget));
            Assert.Throws<ContractException>(() => _ = Input(style: (AiStyle)(-1)));
            Assert.DoesNotThrow(() => _ = new AiMoveOption(MoveCooldowns.MaxSlots - 1, AiMoveTargeting.Hostile, int.MaxValue,
                DamageModel.MaxPower, false));
        }

        // ---- preconditions ----

        [Test]
        public void Value_constructors_reject_out_of_contract_arguments()
        {
            Assert.Throws<ContractException>(() => _ = new AiCommand(AiCommandKind.Focus, CombatAi.NoTarget));
            Assert.Throws<ContractException>(() => _ = new AiCommand(AiCommandKind.Free, 3));
            Assert.Throws<ContractException>(() => _ = new AiCommand((AiCommandKind)9, CombatAi.NoTarget));
            Assert.Throws<ContractException>(() => _ = new AiMoveOption(-1, AiMoveTargeting.Hostile, 1, 1, true));
            Assert.Throws<ContractException>(() => _ = new AiMoveOption(MoveCooldowns.MaxSlots, AiMoveTargeting.Hostile, 1, 1, true));
            Assert.Throws<ContractException>(() => _ = new AiMoveOption(0, (AiMoveTargeting)3, 1, 1, true));
            Assert.Throws<ContractException>(() => _ = new AiMoveOption(0, AiMoveTargeting.Hostile, -1, 1, true));
            Assert.Throws<ContractException>(() => _ = new AiMoveOption(0, AiMoveTargeting.Self, 1, 1, true));
            Assert.Throws<ContractException>(() => _ = new AiMoveOption(0, AiMoveTargeting.Hostile, 1, -1, true));
            Assert.Throws<ContractException>(() => _ = new AiMoveOption(0, AiMoveTargeting.Hostile, 1, DamageModel.MaxPower + 1, true));
            Assert.Throws<ContractException>(() => _ = new AiEnemy(-1, _self, _forward, true));
            Assert.Throws<ContractException>(() => _ = new AiTuning(0, 500, 900, 1));
            Assert.Throws<ContractException>(() => _ = new AiTuning(1001, 500, 900, 1));
            Assert.Throws<ContractException>(() => _ = new AiTuning(800, 0, 900, 1));
            Assert.Throws<ContractException>(() => _ = new AiTuning(800, 500, 1001, 1));
            Assert.Throws<ContractException>(() => _ = new AiTuning(800, 500, 900, 0));
            Assert.DoesNotThrow(() => _ = new AiTuning(1, 1000, 1, 1));
            Assert.Throws<ContractException>(() => _ = Input(style: (AiStyle)4));
            Assert.Throws<ContractException>(() => _ = Input(role: (AiRole)2));
            Assert.Throws<ContractException>(() => _ = Input(mainTarget: -2));
        }

        [Test]
        public void Decide_rejects_out_of_contract_inputs()
        {
            AiEnemy[] e = { Enemy(1, At(5f, 0f)) };
            AiMoveOption[] m = { Hostile(0, 2000, 1) };
            Assert.Throws<ContractException>(() => CombatAi.Decide(_planet, null, Input(), e, m));
            Assert.Throws<ContractException>(() => CombatAi.Decide(_planet, _tuning, Input(), null, m));
            Assert.Throws<ContractException>(() => CombatAi.Decide(_planet, _tuning, Input(), e, null));

            var small = new PlanetConfig(MoveGeometry.MinSupportedRadiusM - 1f, _planet.Center);
            Assert.Throws<ContractException>(() => CombatAi.Decide(small, _tuning, Input(), new AiEnemy[0], m));

            float3 deep = _planet.Center + _up * (R * 0.4f);
            Assert.Throws<ContractException>(() => CombatAi.Decide(_planet, _tuning,
                new AiInput(deep, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget, _self, false, default), e, m));
            Assert.Throws<ContractException>(() => Decide(Input(player: deep), e, m));
            Assert.Throws<ContractException>(() => Decide(Input(partner: deep), e, m));
            Assert.Throws<ContractException>(() => Decide(Input(), new[] { Enemy(1, deep, alive: false) }, m));

            Assert.Throws<ContractException>(() => Decide(Input(), new[] { Enemy(1, At(5f, 0f)), Enemy(1, At(6f, 0f)) }, m));
            Assert.Throws<ContractException>(() => Decide(Input(), e, new[] { Hostile(2, 1, 1), Hostile(2, 5, 1) }));
            var nine = new AiMoveOption[MoveCooldowns.MaxSlots + 1];
            for (int i = 0; i < nine.Length; i++) nine[i] = Hostile(i % MoveCooldowns.MaxSlots, 1, 1);
            Assert.Throws<ContractException>(() => Decide(Input(), e, nine));

            float3 pos = At(5f, 0f);
            Assert.Throws<ContractException>(() => Decide(Input(), new[] { new AiEnemy(1, pos, Dir(pos), true) }, m), "radial forward");
            Assert.Throws<ContractException>(() => Decide(Input(), new[] { new AiEnemy(1, pos, Facing(pos, _self) * 2f, true) }, m), "non-unit");
            Assert.Throws<ContractException>(() => Decide(Input(), new[] { new AiEnemy(1, pos, new float3(float.NaN), true) }, m));
            Assert.DoesNotThrow(() => Decide(Input(), new[] { new AiEnemy(1, pos, float3.zero, false) }, m), "dead forward unchecked");
        }

        [Test]
        public void Heal_triggers_without_overflow_and_always_below_half_hp_even_out_of_combat()
        {
            var heal = new AiMoveOption(0, AiMoveTargeting.Self, 0, 0, true, AiMoveCategory.Heal, 30);
            var attack = Hostile(1, 12000, 60);
            var moves = new[] { heal, attack };
            AiEnemy[] inCombat = { Enemy(1, At(5f, 0f)) };
            AiEnemy[] outOfCombat = System.Array.Empty<AiEnemy>();

            // Full HP: does not heal.
            var full = new AiInput(_self, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget,
                _self, false, default, 100, 100, 1, 1);
            Assert.AreEqual(1, Decide(full, inCombat, moves).CastSlot);
            Assert.AreEqual(CombatAi.NoCast, Decide(full, outOfCombat, moves).CastSlot);

            // 80/100 HP with 30% heal would overflow: does not heal.
            var eighty = new AiInput(_self, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget,
                _self, false, default, 80, 100, 1, 1);
            Assert.AreEqual(1, Decide(eighty, inCombat, moves).CastSlot);
            Assert.AreEqual(CombatAi.NoCast, Decide(eighty, outOfCombat, moves).CastSlot);

            // 70/100 HP with 30% heal (exact 100/100): heals even in combat and out of combat.
            var seventy = new AiInput(_self, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget,
                _self, false, default, 70, 100, 1, 1);
            Assert.AreEqual(0, Decide(seventy, inCombat, moves).CastSlot);
            Assert.AreEqual(0, Decide(seventy, outOfCombat, moves).CastSlot);

            // 49/100 HP (< 50%): always heals even if a 80% heal would overflow.
            var bigHeal = new AiMoveOption(0, AiMoveTargeting.Self, 0, 0, true, AiMoveCategory.Heal, 80);
            var low = new AiInput(_self, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget,
                _self, false, default, 49, 100, 1, 1);
            Assert.AreEqual(0, Decide(low, inCombat, new[] { bigHeal, attack }).CastSlot);
            Assert.AreEqual(0, Decide(low, outOfCombat, new[] { bigHeal, attack }).CastSlot);
        }

        [Test]
        public void Ally_heal_triggers_when_partner_is_low_and_in_reach()
        {
            var allyHeal = new AiMoveOption(0, AiMoveTargeting.Ally, 8000, 0, true, AiMoveCategory.Heal, 30);
            var attack = Hostile(1, 12000, 60);
            var moves = new[] { allyHeal, attack };

            var inReach = new AiInput(_self, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget,
                _self, true, At(5f, 90f), 100, 100, 40, 100);
            Assert.AreEqual(0, Decide(inReach, System.Array.Empty<AiEnemy>(), moves).CastSlot);

            var outOfReach = new AiInput(_self, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free), CombatAi.NoTarget,
                _self, true, At(15f, 90f), 100, 100, 40, 100);
            Assert.AreEqual(CombatAi.NoCast, Decide(outOfReach, System.Array.Empty<AiEnemy>(), moves).CastSlot);
        }

        [Test]
        public void Qc22_contracts_reject_invalid_heal_and_hp_inputs()
        {
            Assert.Throws<ContractException>(() => _ = new AiMoveOption(0, AiMoveTargeting.Self, 0, 0, true, AiMoveCategory.Heal, 0));
            Assert.Throws<ContractException>(() => _ = new AiMoveOption(0, AiMoveTargeting.Self, 0, 0, true, (AiMoveCategory)9, 0));
            Assert.Throws<ContractException>(() => _ = new AiInput(_self, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free),
                CombatAi.NoTarget, _self, false, default, 101, 100, 1, 1));
            Assert.Throws<ContractException>(() => _ = new AiInput(_self, AiStyle.Hold, AiRole.Main, Cmd(AiCommandKind.Free),
                CombatAi.NoTarget, _self, true, _self, 10, 100, 0, 0));
        }
    }
}
