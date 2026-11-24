using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;

namespace Cosmimera.Combat
{
    /// <summary>One crack of a quake: a straight fissure on the ground.</summary>
    /// <remarks>REQ: planet-world.md#地震噴發. Pre: built by Encounter. Post: immutable. Side effects: none.</remarks>
    public readonly struct QuakeCrack
    {
        internal QuakeCrack(float3 center, float3 along, float lengthM)
        {
            Center = center;
            Along = along;
            LengthM = lengthM;
        }

        /// <summary>Unit direction of the crack's midpoint.</summary>
        /// <remarks>Pre: none. Post: unit vector. Side effects: none.</remarks>
        public float3 Center { get; }

        /// <summary>Unit tangent at Center the crack runs along (both ways).</summary>
        /// <remarks>Pre: none. Post: unit, perpendicular to Center. Side effects: none.</remarks>
        public float3 Along { get; }

        /// <summary>End-to-end length in metres (15–40).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public float LengthM { get; }
    }

    /// <summary>One quake: its phases' times and its cracks.</summary>
    /// <remarks>
    /// REQ: planet-world.md#地震噴發. Pre: built by Encounter. Post: StartMs &lt; CrackMs &lt; EruptEndMs &lt; CoolEndMs;
    /// Cracks non-empty. Side effects: none.
    /// </remarks>
    public sealed class Quake
    {
        internal Quake(int region, long window, long startMs, int eruptMs, QuakeCrack[] cracks)
        {
            Region = region;
            Window = window;
            StartMs = startMs;
            CrackMs = startMs + QuakeSchedule.ForeshockMs;
            EruptEndMs = CrackMs + eruptMs;
            CoolEndMs = EruptEndMs + QuakeSchedule.LavaCoolMs;
            Cracks = cracks;
        }

        /// <summary>Region the quake belongs to.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int Region { get; }

        /// <summary>Schedule window it was rolled in.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public long Window { get; }

        /// <summary>Foreshock starts.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public long StartMs { get; }

        /// <summary>The cracks open and the eruption starts.</summary>
        /// <remarks>Pre: none. Post: StartMs + ForeshockMs. Side effects: none.</remarks>
        public long CrackMs { get; }

        /// <summary>The eruption stops; the lava starts cooling.</summary>
        /// <remarks>Pre: none. Post: CrackMs + 6–10 s. Side effects: none.</remarks>
        public long EruptEndMs { get; }

        /// <summary>The lava has cooled to harmless black rock.</summary>
        /// <remarks>Pre: none. Post: EruptEndMs + LavaCoolMs. Side effects: none.</remarks>
        public long CoolEndMs { get; }

        /// <summary>The cracks (1–4: placements off volcanic ground were dropped).</summary>
        /// <remarks>Pre: none. Post: non-empty; do not modify. Side effects: none.</remarks>
        public IReadOnlyList<QuakeCrack> Cracks { get; }
    }

    /// <summary>A volcanic bomb in flight: thrown from a crack, landing after its warning.</summary>
    /// <remarks>REQ: planet-world.md#地震噴發. Pre: built by Encounter. Post: immutable. Side effects: none.</remarks>
    public readonly struct VolcanicBomb
    {
        internal VolcanicBomb(float3 from, float3 direction, long landsAtMs)
        {
            From = from;
            Direction = direction;
            LandsAtMs = landsAtMs;
        }

        /// <summary>Unit direction of the point on the crack it was thrown from.</summary>
        /// <remarks>Pre: none. Post: unit vector. Side effects: none.</remarks>
        public float3 From { get; }

        /// <summary>Unit direction of the landing point (centre of the warning circle).</summary>
        /// <remarks>Pre: none. Post: unit vector. Side effects: none.</remarks>
        public float3 Direction { get; }

        /// <summary>CombatTime it lands; thrown QuakeSchedule.BombWarningMs earlier.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public long LandsAtMs { get; }
    }

    public sealed partial class Encounter
    {
        private ulong _quakeSeed;
        private bool _quakeLavaPlanet;
        private Func<float3, int> _quakeRegionAt;
        private Func<int, float3> _quakeRegionCenter;
        private Func<float3, bool> _volcanicAt;
        private float _quakeRegionRadiusM;
        private readonly List<Quake> _quakes = new List<Quake>(4);
        private readonly List<long> _quakeKeys = new List<long>(8);
        private readonly List<VolcanicBomb> _pendingBombs = new List<VolcanicBomb>(16);
        private readonly List<VolcanicBomb> _landedBombs = new List<VolcanicBomb>(8);
        private readonly List<QuakeCrack> _crackBuffer = new List<QuakeCrack>(QuakeSchedule.MaxCracks);
        private float3 _playerLaunch;

        /// <summary>Turns on rule-layer earthquakes and eruptions. REQ: planet-world.md#地震噴發.</summary>
        /// <remarks>
        /// Pre: lookups non-null and deterministic; regionRadiusM &gt; 0; called at most once, before the first Tick.
        ///      volcanicAt(direction) says whether a crack may open there (always true on a lava planet; volcanic
        ///      landforms elsewhere; false indoors).
        /// Post: each Tick builds the player's region's quakes from QuakeSchedule and runs them: cracks open
        ///       ForeshockMs after the start (throwing and hurting what stands on them, setting flammables on the
        ///       lava strip alight), bombs fall with warnings through the eruption, the lava hurts until it cools.
        /// Side effects: stores the references.
        /// </remarks>
        public void AttachQuakes(ulong seed, Func<float3, int> regionAt, Func<int, float3> regionCenter, float regionRadiusM,
            bool lavaPlanet, Func<float3, bool> volcanicAt)
        {
            Contract.Requires(regionAt != null && regionCenter != null && volcanicAt != null, "lookups required");
            Contract.Requires(regionRadiusM > 0f && math.isfinite(regionRadiusM), "regionRadiusM must be > 0");
            Contract.Requires(_volcanicAt == null, "quakes already attached");
            Contract.Requires(_lastTickMs == long.MinValue, "attach before the first Tick");
            _quakeSeed = seed;
            _quakeRegionAt = regionAt;
            _quakeRegionCenter = regionCenter;
            _quakeRegionRadiusM = regionRadiusM;
            _quakeLavaPlanet = lavaPlanet;
            _volcanicAt = volcanicAt;
        }

        /// <summary>Quakes from foreshock until their lava has cooled.</summary>
        /// <remarks>Pre: none. Post: ordered by StartMs; valid until the next Tick. Side effects: none.</remarks>
        public IReadOnlyList<Quake> Quakes => _quakes;

        /// <summary>Bombs in flight (each shows a warning circle).</summary>
        /// <remarks>Pre: none. Post: valid until the next Tick. Side effects: none.</remarks>
        public IReadOnlyList<VolcanicBomb> PendingBombs => _pendingBombs;

        /// <summary>Bombs that landed during the last Tick.</summary>
        /// <remarks>Pre: none. Post: valid until the next Tick. Side effects: none.</remarks>
        public IReadOnlyList<VolcanicBomb> LandedBombs => _landedBombs;

        /// <summary>Unit tangent the player was thrown along when a crack opened under them (with a Quaked event).</summary>
        /// <remarks>Pre: none. Post: zero unless the last Tick emitted Quaked for the player. Side effects: none.</remarks>
        public float3 PlayerLaunchDirection => _playerLaunch;

        /// <summary>Distance in metres from <paramref name="direction"/> to the nearest point of <paramref name="crack"/>.</summary>
        /// <remarks>Pre: direction unit; planetRadiusM &gt; 0. Post: ≥ 0. Side effects: none.</remarks>
        public static float CrackDistanceM(in QuakeCrack crack, float3 direction, float planetRadiusM)
        {
            // Within 40 m the sphere is flat to 1e-4 m: tangent-plane coordinates are exact enough.
            float3 d = (direction - crack.Center) * planetRadiusM;
            float a = math.dot(d, crack.Along);
            float3 across = d - crack.Along * a - crack.Center * math.dot(d, crack.Center);
            float over = math.max(0f, math.abs(a) - crack.LengthM * 0.5f);
            return math.sqrt(over * over + math.lengthsq(across));
        }

        private void StepQuakes(long previousMs, long nowMs, float3 playerPosition)
        {
            _landedBombs.Clear();
            _playerLaunch = float3.zero;
            if (_volcanicAt == null)
            {
                return;
            }

            int region = _quakeRegionAt(_planet.ToDirection(playerPosition));
            long w = QuakeSchedule.WindowMs(_quakeLavaPlanet);
            long window = nowMs / w;
            if (window > 0) Build(region, window - 1, nowMs);
            Build(region, window, nowMs);

            long from = previousMs < 0 ? -1 : previousMs;
            for (int q = 0; q < _quakes.Count; q++)
            {
                Quake quake = _quakes[q];
                if (from < quake.CrackMs && quake.CrackMs <= nowMs) OpenCracks(quake, nowMs);
                long firstSlot = math.max(from / QuakeSchedule.SlotMs + 1, quake.CrackMs / QuakeSchedule.SlotMs);
                for (long slot = firstSlot; slot <= nowMs / QuakeSchedule.SlotMs; slot++)
                {
                    long slotMs = slot * QuakeSchedule.SlotMs;
                    if (slotMs < quake.CrackMs) continue;
                    if (slotMs < quake.EruptEndMs) ThrowBombs(quake, slot, slotMs);
                    if (slotMs < quake.CoolEndMs) BurnInLava(quake, nowMs);
                }
            }

            for (int i = 0; i < _pendingBombs.Count; i++)
            {
                VolcanicBomb bomb = _pendingBombs[i];
                if (bomb.LandsAtMs > nowMs) continue;
                LandBomb(bomb, nowMs);
                _landedBombs.Add(bomb);
                _pendingBombs.RemoveAt(i);
                i--;
            }

            // Cooled quakes leave the rule layer; the black rock they left is presentation (planet-world.md#地震噴發).
            for (int q = _quakes.Count - 1; q >= 0; q--)
            {
                if (_quakes[q].CoolEndMs < nowMs) _quakes.RemoveAt(q);
            }
        }

        private void Build(int region, long window, long nowMs)
        {
            long key = region * 1_000_000_000L + window;
            if (_quakeKeys.Contains(key)) return;
            QuakeSchedule.Timing(_quakeSeed, region, window, _quakeLavaPlanet, out long startMs, out int eruptMs, out int wanted);
            // Not built before its foreshock is at most a window away: the region the player is in may still change.
            if (startMs - nowMs > QuakeSchedule.WindowMs(_quakeLavaPlanet)) return;
            _quakeKeys.Add(key);
            if (_quakeKeys.Count > 32) _quakeKeys.RemoveAt(0);
            if (startMs + QuakeSchedule.ForeshockMs + eruptMs + QuakeSchedule.LavaCoolMs < nowMs) return;

            _crackBuffer.Clear();
            float3 center = _quakeRegionCenter(region);
            for (int attempt = 0; attempt < QuakeSchedule.CrackAttempts && _crackBuffer.Count < wanted; attempt++)
            {
                QuakeSchedule.Crack(_quakeSeed, region, window, attempt, out int angle, out int reach, out int heading, out int length);
                float3 point = RollRegionPoint(center, angle, reach, _quakeRegionRadiusM);
                // Rejection keeps a region's cracks inside it and on volcanic ground, the same on every client.
                if (_quakeRegionAt(point) != region || !_volcanicAt(point)) continue;
                PlanetFrame frame = PlanetFrame.FromUp(point);
                float bearing = heading * (math.PI / 1800f);
                _crackBuffer.Add(new QuakeCrack(point, math.normalize(frame.East * math.cos(bearing) + frame.North * math.sin(bearing)), length));
            }

            if (_crackBuffer.Count == 0) return;
            var quake = new Quake(region, window, startMs, eruptMs, _crackBuffer.ToArray());
            int at = 0;
            while (at < _quakes.Count && _quakes[at].StartMs <= startMs) at++;
            _quakes.Insert(at, quake);
        }

        private float3 RollRegionPoint(float3 center, int angle, int reach, float radiusM)
        {
            PlanetFrame frame = PlanetFrame.FromUp(center);
            float bearing = angle * (2f * math.PI / 1000f);
            float3 tangent = frame.East * math.cos(bearing) + frame.North * math.sin(bearing);
            float arc = radiusM * math.sqrt(reach / 1000f) / _planet.Radius;
            return math.normalize(center * math.cos(arc) + tangent * math.sin(arc));
        }

        // REQ: planet-world.md#地震噴發 裂開 — whoever stands on a crack is thrown clear and hurt; flammables on the lava
        // strip catch at once.
        private void OpenCracks(Quake quake, long nowMs)
        {
            float reach = QuakeSchedule.CrackHalfWidthM;
            for (int i = 0; i < _units.Count; i++)
            {
                EncounterUnit u = _units[i];
                if (!u.IsActive) continue;
                float3 dir = _planet.ToDirection(u.Position);
                if (!NearestCrack(quake, dir, reach + u.BodyRadiusM, out QuakeCrack crack)) continue;
                int dealt = u.Combatant.ReceiveEnvironmentDamage(Element.Earth, QuakeSchedule.CrackPermille, nowMs);
                _events.Add(new EncounterEvent(EncounterEventKind.Quaked, NoSource, u.Id, 0, dealt));
                if (!u.IsBoss)
                {
                    float3 away = AwayFromCrack(crack, dir);
                    float step = (QuakeSchedule.LavaHalfWidthM + 0.5f) / _planet.Radius;
                    PlaceIfAllowed(u, math.normalize(dir * math.cos(step) + away * math.sin(step)));
                }

                if (u.Combatant.IsFainted) _events.Add(new EncounterEvent(EncounterEventKind.Faint, NoSource, u.Id, 0, 0));
            }

            float3 player = _planet.ToDirection(_playerCenter);
            if (_player != null && !_player.IsDowned && NearestCrack(quake, player, reach + PlayerBodyRadiusM, out QuakeCrack under))
            {
                int amount = (int)(((long)_player.MaxHp * QuakeSchedule.CrackPermille + 500) / 1000);
                int dealt = _player.ApplyDamage(math.max(1, amount), nowMs);
                _playerLaunch = AwayFromCrack(under, player);
                _events.Add(new EncounterEvent(EncounterEventKind.Quaked, NoSource, PlayerCharacterId, 0, dealt));
                if (_player.IsDowned) _events.Add(new EncounterEvent(EncounterEventKind.PlayerDowned, NoSource, PlayerCharacterId, 0, 0));
            }

            if (_objectIndex == null || _objects == null) return;
            foreach (QuakeCrack crack in quake.Cracks)
            {
                float3 mid = _planet.ToWorld(crack.Center, _surface.GroundAltitude(crack.Center));
                _fireBuffer.Clear();
                _objectIndex.Query(mid, crack.LengthM * 0.5f + QuakeSchedule.LavaHalfWidthM, _fireBuffer);
                foreach (DestructibleCandidate c in _fireBuffer)
                {
                    if (_objects.IsDestroyed(c.Id) || c.Composition.Dominant.Flammable == Flammability.None) continue;
                    if (CrackDistanceM(crack, _planet.ToDirection(c.Center), _planet.Radius) > QuakeSchedule.LavaHalfWidthM) continue;
                    int index = _objects.IndexOf(c.Id);
                    if (index < 0) index = _objects.Register(c.Id, c.Composition, c.VolumeL, c.Center);
                    IgniteSlot(index, nowMs);
                }
            }
        }

        private void ThrowBombs(Quake quake, long slot, long slotMs)
        {
            for (int c = 0; c < quake.Cracks.Count; c++)
            {
                QuakeCrack crack = quake.Cracks[c];
                for (int b = 0; b < QuakeSchedule.BombsPerCrackSlot; b++)
                {
                    if (!QuakeSchedule.TryBomb(_quakeSeed, quake.Region, quake.Window, slot, c, b, out int along, out int sideDm)) continue;
                    float alongM = (along / 1000f - 0.5f) * crack.LengthM;
                    float3 from = Offset(crack.Center, crack.Along, alongM);
                    float3 across = math.cross(crack.Center, crack.Along);
                    float3 to = Offset(from, across, sideDm * 0.1f);
                    _pendingBombs.Add(new VolcanicBomb(from, to, slotMs + QuakeSchedule.BombWarningMs));
                }
            }
        }

        private float3 Offset(float3 direction, float3 tangent, float metres)
        {
            float a = metres / _planet.Radius;
            float3 t = math.normalize(tangent - direction * math.dot(tangent, direction));
            return math.normalize(direction * math.cos(a) + t * math.sin(a));
        }

        private void LandBomb(in VolcanicBomb bomb, long nowMs)
        {
            float reach = QuakeSchedule.BombRadiusM;
            for (int i = 0; i < _units.Count; i++)
            {
                EncounterUnit u = _units[i];
                if (!u.IsActive || SurfaceDistanceM(u.Position, bomb.Direction) > reach + u.BodyRadiusM) continue;
                int dealt = u.Combatant.ReceiveEnvironmentDamage(Element.Fire, QuakeSchedule.BombPermille, nowMs);
                ScorchEvents(u, dealt);
            }

            if (_player != null && !_player.IsDowned && SurfaceDistanceM(_playerCenter, bomb.Direction) <= reach + PlayerBodyRadiusM)
            {
                ScorchPlayer(QuakeSchedule.BombPermille, nowMs);
            }

            if (_objectIndex == null || _objects == null) return;
            _fireBuffer.Clear();
            _objectIndex.Query(_planet.ToWorld(bomb.Direction, _surface.GroundAltitude(bomb.Direction)), reach, _fireBuffer);
            foreach (DestructibleCandidate c in _fireBuffer)
            {
                if (_objects.IsDestroyed(c.Id) || c.Composition.Dominant.Flammable == Flammability.None) continue;
                int index = _objects.IndexOf(c.Id);
                if (index < 0) index = _objects.Register(c.Id, c.Composition, c.VolumeL, c.Center);
                IgniteSlot(index, nowMs);
            }
        }

        private void BurnInLava(Quake quake, long nowMs)
        {
            float reach = QuakeSchedule.LavaHalfWidthM;
            for (int i = 0; i < _units.Count; i++)
            {
                EncounterUnit u = _units[i];
                if (!u.IsActive || !NearestCrack(quake, _planet.ToDirection(u.Position), reach, out _)) continue;
                int dealt = u.Combatant.ReceiveEnvironmentDamage(Element.Fire, QuakeSchedule.LavaPermillePerS, nowMs);
                ScorchEvents(u, dealt);
            }

            if (_player != null && !_player.IsDowned && NearestCrack(quake, _planet.ToDirection(_playerCenter), reach, out _))
            {
                ScorchPlayer(QuakeSchedule.LavaPermillePerS, nowMs);
            }
        }

        private bool NearestCrack(Quake quake, float3 direction, float withinM, out QuakeCrack nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            foreach (QuakeCrack crack in quake.Cracks)
            {
                float d = CrackDistanceM(crack, direction, _planet.Radius);
                if (d < best)
                {
                    best = d;
                    nearest = crack;
                }
            }

            return best <= withinM;
        }

        private static float3 AwayFromCrack(in QuakeCrack crack, float3 direction)
        {
            float3 across = math.cross(crack.Center, crack.Along);
            // Standing exactly on the line has no side: fall to a fixed one rather than to a zero vector.
            float side = math.dot(direction - crack.Center, across);
            float3 away = side >= 0f ? across : -across;
            float3 tangent = away - direction * math.dot(away, direction);
            return math.normalize(tangent);
        }
    }
}
