using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Planet.Tests
{
    public sealed class PlanetWallsTests
    {
        private const float Radius = 320f;
        private const float HeightM = 12f;
        private const float HalfM = 7f;
        private const float TopM = 2.5f;
        private const float PassInnerM = 5f;
        private const float PassOuterM = 9f;

        // Three sites on the equator 0.6 rad apart: cells 0|1 meet at +0.3 rad, 1|2 at +0.9 rad.
        private static readonly float3[] Sites = { At(0f), At(0.6f), At(1.2f) };
        private static readonly float3x2[] NoCorridors = new float3x2[0];

        private static float3 At(float lon, float lat = 0f) =>
            new float3(math.cos(lat) * math.cos(lon), math.sin(lat), math.cos(lat) * math.sin(lon));

        private static PlanetWalls Make(bool open01 = false, float3x2[] corridors = null, WallRing? ring = null)
        {
            var open = new bool[9];
            open[0 * 3 + 1] = open01;
            open[1 * 3 + 0] = open01;
            return PlanetWalls.Create(new PlanetConfig(Radius, float3.zero), Sites, open, corridors ?? NoCorridors, PassInnerM,
                PassOuterM, ring, HeightM, HalfM, TopM);
        }

        // Arena of radius 36 m around the middle of cell 1, gate on its west side.
        private static WallRing Ring() => new WallRing
        {
            Center = At(0.6f), RadiusM = 36f, Gate = At(0.6f - 36f / Radius), GateInnerM = 9f, GateOuterM = 13f,
        };

        [Test]
        public void Default_view_has_no_walls()
        {
            WallView view = default;
            Assert.That(view.Count, Is.EqualTo(0));
            Assert.That(view.Height(At(0.3f)), Is.EqualTo(0f));
            Assert.That(view.IsBlocked(At(0.3f), 0f, true), Is.False);
        }

        [Test]
        public void Wall_is_full_height_on_the_border_and_gone_beyond_the_half_width()
        {
            using PlanetWalls walls = Make();
            WallView view = walls.View;
            Assert.That(view.Height(At(0.3f)), Is.EqualTo(HeightM).Within(1e-3f));
            Assert.That(view.Height(At(0.3f + 2f / Radius)), Is.EqualTo(HeightM).Within(1e-3f), "flat top");
            float mid = view.Height(At(0.3f - 4.75f / Radius));
            Assert.That(mid, Is.GreaterThan(1f).And.LessThan(HeightM - 1f), "sloped face");
            Assert.That(view.Height(At(0.3f - 7.5f / Radius)), Is.EqualTo(0f));
            Assert.That(view.Height(At(0.3f + 7.5f / Radius)), Is.EqualTo(0f));
            Assert.That(view.Height(At(0f)), Is.EqualTo(0f));
            Assert.That(view.Height(At(0.3f, 0.4f)), Is.EqualTo(HeightM).Within(1e-2f), "the wall follows the whole border");
            Assert.That(walls.HeightM, Is.EqualTo(HeightM));
        }

        [Test]
        public void Open_pair_has_no_wall_but_other_borders_keep_theirs()
        {
            using PlanetWalls walls = Make(open01: true);
            Assert.That(walls.View.Height(At(0.3f)), Is.EqualTo(0f));
            Assert.That(walls.View.Height(At(0.9f)), Is.EqualTo(HeightM).Within(1e-3f));
        }

        [Test]
        public void Corridor_removes_the_wall_along_its_whole_length_and_only_within_its_width()
        {
            // A corridor crossing border 0|1 at a slant, as an oblique road does.
            var corridor = new float3x2(At(0.25f, -0.02f), At(0.35f, 0.02f));
            using PlanetWalls walls = Make(corridors: new[] { corridor });
            WallView view = walls.View;
            Assert.That(view.Height(At(0.3f)), Is.EqualTo(0f));
            Assert.That(view.Height(At(0.3f, 4f / Radius)), Is.EqualTo(0f), "inside the inner width");
            Assert.That(view.Height(At(0.3f, 14f / Radius)), Is.EqualTo(HeightM).Within(1e-2f), "beyond the outer width");
            Assert.That(view.IsBlocked(At(0.3f), 1f / Radius, true), Is.False);
            Assert.That(view.IsBlocked(At(0.3f, 6.5f / Radius), 1f / Radius, false), Is.True, "the wall is back between the two widths");
            Assert.That(view.Height(At(0.9f)), Is.EqualTo(HeightM).Within(1e-3f), "other borders are untouched");
        }

        [Test]
        public void Blocking_grows_with_the_body_and_starts_low_on_the_slope()
        {
            using PlanetWalls walls = Make();
            WallView view = walls.View;
            Assert.That(view.IsBlocked(At(0.3f), 0f, false), Is.True);
            Assert.That(view.IsBlocked(At(0.3f - 5.5f / Radius), 0f, false), Is.True, "≥ 10 % of the height blocks");
            Assert.That(view.IsBlocked(At(0.3f - 7.5f / Radius), 0f, false), Is.False);
            Assert.That(view.IsBlocked(At(0.3f - 7.5f / Radius), 2f / Radius, false), Is.True, "a 2 m body touches the slope");
            Assert.That(view.IsBlocked(At(0f), 2f / Radius, false), Is.False);
        }

        [Test]
        public void Ring_wall_stands_on_its_circle_and_opens_only_at_its_gate()
        {
            using PlanetWalls walls = Make(ring: Ring());
            WallView view = walls.View;
            Assert.That(view.Height(At(0.6f + 36f / Radius)), Is.EqualTo(HeightM).Within(1e-2f), "east side");
            Assert.That(view.Height(At(0.6f, 36f / Radius)), Is.EqualTo(HeightM).Within(1e-2f), "north side");
            Assert.That(view.Height(At(0.6f)), Is.EqualTo(0f), "inside the arena");
            Assert.That(view.Height(At(0.6f + 50f / Radius)), Is.EqualTo(0f), "outside the arena");
            Assert.That(view.Height(At(0.6f - 36f / Radius)), Is.EqualTo(0f), "gate");
        }

        [Test]
        public void Ring_gate_is_plugged_until_the_lock_opens_and_corridors_do_not_cut_the_ring()
        {
            // A corridor straight through the ring's east side must leave the ring wall standing.
            var corridor = new float3x2(At(0.6f + 20f / Radius), At(0.6f + 50f / Radius));
            using PlanetWalls walls = Make(corridors: new[] { corridor }, ring: Ring());
            WallView view = walls.View;
            float3 gate = At(0.6f - 36f / Radius);
            Assert.That(view.IsBlocked(gate, 0f, true), Is.True);
            Assert.That(view.IsBlocked(At(0.6f - 36f / Radius, 12f / Radius), 0f, true), Is.True, "plug covers the outer distance");
            Assert.That(view.IsBlocked(gate, 0f, false), Is.False);
            Assert.That(view.IsBlocked(At(0.6f), 0f, true), Is.False, "the arena floor is free");
            Assert.That(view.Height(At(0.6f + 36f / Radius)), Is.EqualTo(HeightM).Within(1e-2f));
        }

        [Test]
        public void Border_distance_is_zero_on_a_border_and_grows_inward()
        {
            using PlanetWalls walls = Make();
            Assert.That(walls.View.BorderDistance(At(0.3f)), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(walls.View.BorderDistance(At(0.2f)), Is.EqualTo(0.1f).Within(1e-3f));
        }

        [Test]
        public void Preconditions_are_checked()
        {
            var planet = new PlanetConfig(Radius, float3.zero);
            var open = new bool[9];
            Assert.Throws<ContractException>(() => PlanetWalls.Create(default, Sites, open, NoCorridors, 5f, 9f, null, HeightM, HalfM, TopM));
            Assert.Throws<ContractException>(() => PlanetWalls.Create(planet, new[] { Sites[0] }, new bool[1], NoCorridors, 5f, 9f, null, HeightM, HalfM, TopM));
            Assert.Throws<ContractException>(() => PlanetWalls.Create(planet, Sites, new bool[4], NoCorridors, 5f, 9f, null, HeightM, HalfM, TopM));
            Assert.Throws<ContractException>(() => PlanetWalls.Create(planet, Sites, open, null, 5f, 9f, null, HeightM, HalfM, TopM));
            Assert.Throws<ContractException>(() => PlanetWalls.Create(planet, Sites, open, NoCorridors, 9f, 5f, null, HeightM, HalfM, TopM));
            Assert.Throws<ContractException>(() => PlanetWalls.Create(planet, Sites, open, NoCorridors, 5f, 9f, null, 0f, HalfM, TopM));
            Assert.Throws<ContractException>(() => PlanetWalls.Create(planet, Sites, open, NoCorridors, 5f, 9f, null, HeightM, TopM, HalfM));
            var asymmetric = new bool[9];
            asymmetric[1] = true;
            Assert.Throws<ContractException>(() => PlanetWalls.Create(planet, Sites, asymmetric, NoCorridors, 5f, 9f, null, HeightM, HalfM, TopM));
            WallRing small = Ring();
            small.RadiusM = HalfM;
            Assert.Throws<ContractException>(() => PlanetWalls.Create(planet, Sites, open, NoCorridors, 5f, 9f, small, HeightM, HalfM, TopM));
            WallRing badGate = Ring();
            badGate.GateOuterM = badGate.GateInnerM;
            Assert.Throws<ContractException>(() => PlanetWalls.Create(planet, Sites, open, NoCorridors, 5f, 9f, badGate, HeightM, HalfM, TopM));
        }
    }
}
