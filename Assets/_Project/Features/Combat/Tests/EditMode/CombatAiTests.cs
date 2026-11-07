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
            Assert.AreEqual(line.ReachMm, CombatAi.ReachMmOf(line));
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

            var small = new PlanetConfig(320f, _planet.Center);
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
    }
}
