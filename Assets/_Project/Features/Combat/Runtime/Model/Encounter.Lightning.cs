using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;

namespace Cosmimera.Combat
{
    /// <summary>One rule-layer lightning strike: where and when it lands, and the object it hits (if any).</summary>
    /// <remarks>REQ: docs/design/weather.md#落雷. Pre: built by Encounter. Post: immutable. Side effects: none.</remarks>
    public readonly struct LightningStrike
    {
        internal LightningStrike(float3 direction, long landsAtMs, long objectId)
        {
            Direction = direction;
            LandsAtMs = landsAtMs;
            ObjectId = objectId;
        }

        /// <summary>Unit direction of the strike point (an object's centre when a rod took it).</summary>
        /// <remarks>Pre: none. Post: unit vector. Side effects: none.</remarks>
        public float3 Direction { get; }

        /// <summary>CombatTime the strike lands; the warning circle shows for LightningSchedule.WarningMs before it.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public long LandsAtMs { get; }

        /// <summary>Destructible id that drew the strike, or -1 for open ground.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public long ObjectId { get; }
    }

    public sealed partial class Encounter
    {
        private ulong _lightningSeed;
        private Func<float3, int> _lightningRegionAt;
        private Func<int, float3> _lightningRegionCenter;
        private Func<float3, long, int2> _stormAt;
        private float _lightningRegionRadiusM;
        private readonly List<LightningStrike> _pendingStrikes = new List<LightningStrike>(8);
        private readonly List<LightningStrike> _landedStrikes = new List<LightningStrike>(4);
        // Parallel to _pendingStrikes: the index has no lookup by id, so the rod is kept from when it was picked.
        private readonly List<DestructibleCandidate> _pendingRods = new List<DestructibleCandidate>(8);
        private readonly List<DestructibleCandidate> _rodBuffer = new List<DestructibleCandidate>(32);

        /// <summary>Turns on rule-layer lightning strikes. REQ: docs/design/weather.md#落雷.</summary>
        /// <remarks>
        /// Pre: regionAt, regionCenter, stormAt non-null and deterministic; regionRadiusM &gt; 0; called at most once,
        ///      before the first Tick. stormAt(direction, timeMs) returns (effective thunder, effective ion storm),
        ///      each 0..1000, and (0, 0) wherever the sky is not open (indoor dungeons).
        /// Post: each Tick decides strikes for the player's region in every LightningSchedule slot it crosses; a decided
        ///       strike lands LightningSchedule.WarningMs later on the best lightning rod near the rolled point (or the
        ///       open ground there), hurting units within LightningSchedule.WarningRadiusM, hitting the object with
        ///       thunder and igniting it if flammable.
        /// Side effects: stores the references.
        /// </remarks>
        public void AttachLightning(ulong seed, Func<float3, int> regionAt, Func<int, float3> regionCenter,
            float regionRadiusM, Func<float3, long, int2> stormAt)
        {
            Contract.Requires(regionAt != null && regionCenter != null && stormAt != null, "lookups required");
            Contract.Requires(regionRadiusM > 0f && math.isfinite(regionRadiusM), "regionRadiusM must be > 0");
            Contract.Requires(_stormAt == null, "lightning already attached");
            Contract.Requires(_lastTickMs == long.MinValue, "attach before the first Tick");
            _lightningSeed = seed;
            _lightningRegionAt = regionAt;
            _lightningRegionCenter = regionCenter;
            _lightningRegionRadiusM = regionRadiusM;
            _stormAt = stormAt;
        }

        /// <summary>Strikes decided but not yet landed (each shows a warning circle).</summary>
        /// <remarks>Pre: none. Post: ordered by LandsAtMs; valid until the next Tick. Side effects: none.</remarks>
        public IReadOnlyList<LightningStrike> PendingStrikes => _pendingStrikes;

        /// <summary>Strikes that landed during the last Tick.</summary>
        /// <remarks>Pre: none. Post: valid until the next Tick. Side effects: none.</remarks>
        public IReadOnlyList<LightningStrike> LandedStrikes => _landedStrikes;

        private void StepLightning(long previousMs, long nowMs, float3 playerPosition)
        {
            _landedStrikes.Clear();
            if (_stormAt == null)
            {
                return;
            }

            float3 playerDirection = _planet.ToDirection(playerPosition);
            int region = _lightningRegionAt(playerDirection);
            long firstSlot = previousMs < 0 ? 0 : previousMs / LightningSchedule.SlotMs + 1;
            for (long slot = firstSlot; slot <= nowMs / LightningSchedule.SlotMs; slot++)
            {
                long slotMs = slot * LightningSchedule.SlotMs;
                float3 center = _lightningRegionCenter(region);
                int2 storm = _stormAt(center, slotMs);
                if (!LightningSchedule.TryStrike(_lightningSeed, region, slot, storm.x, storm.y, out int angle, out int reach))
                {
                    continue;
                }

                float3 point = RollStrikePoint(center, angle, reach);
                // Rejection keeps a region's strikes inside it, so a neighbour region's client never disagrees.
                if (_lightningRegionAt(point) != region)
                {
                    continue;
                }

                bool hasRod = PickRod(point, out DestructibleCandidate rod);
                _pendingStrikes.Add(new LightningStrike(hasRod ? _planet.ToDirection(rod.Center) : point,
                    slotMs + LightningSchedule.WarningMs, hasRod ? rod.Id : -1));
                _pendingRods.Add(rod);
            }

            for (int i = 0; i < _pendingStrikes.Count; i++)
            {
                LightningStrike strike = _pendingStrikes[i];
                if (strike.LandsAtMs > nowMs)
                {
                    continue;
                }

                Land(strike, _pendingRods[i], nowMs);
                _landedStrikes.Add(strike);
                _pendingStrikes.RemoveAt(i);
                _pendingRods.RemoveAt(i);
                i--;
            }
        }

        private float3 RollStrikePoint(float3 center, int angle, int reach)
        {
            PlanetFrame frame = PlanetFrame.FromUp(center);
            float bearing = angle * (2f * math.PI / 1000f);
            float3 tangent = frame.East * math.cos(bearing) + frame.North * math.sin(bearing);
            // sqrt spreads strikes evenly over the region's area instead of crowding the centre.
            float arc = _lightningRegionRadiusM * math.sqrt(reach / 1000f) / _planet.Radius;
            return math.normalize(center * math.cos(arc) + tangent * math.sin(arc));
        }

        // REQ: weather.md#落雷 — tall (volume), conductive (×3) and isolated (×2) objects draw the strike.
        private bool PickRod(float3 point, out DestructibleCandidate rod)
        {
            rod = default;
            if (_objectIndex == null || _objects == null)
            {
                return false;
            }

            float3 world = _planet.ToWorld(point, _surface.GroundAltitude(point));
            _rodBuffer.Clear();
            _objectIndex.Query(world, LightningSchedule.RodSearchM + LightningSchedule.IsolationM, _rodBuffer);
            long best = -1;
            long bestWeight = 0;
            float isolationSq = LightningSchedule.IsolationM * LightningSchedule.IsolationM;
            foreach (DestructibleCandidate c in _rodBuffer)
            {
                if (_objects.IsDestroyed(c.Id) || math.distance(c.Center, world) > LightningSchedule.RodSearchM)
                {
                    continue;
                }

                bool isolated = true;
                foreach (DestructibleCandidate other in _rodBuffer)
                {
                    if (other.Id != c.Id && !_objects.IsDestroyed(other.Id) && math.distancesq(other.Center, c.Center) < isolationSq)
                    {
                        isolated = false;
                        break;
                    }
                }

                long weight = math.max(1, c.VolumeL) * (c.Composition.Dominant.Conductive ? 3L : 1L) * (isolated ? 2L : 1L);
                // Ties go to the lower id: the index's query order is not part of any contract.
                if (weight > bestWeight || (weight == bestWeight && c.Id < best))
                {
                    best = c.Id;
                    bestWeight = weight;
                    rod = c;
                }
            }

            return best >= 0;
        }

        private void Land(in LightningStrike strike, in DestructibleCandidate c, long nowMs)
        {
            float reachM = LightningSchedule.WarningRadiusM;
            for (int i = 0; i < _units.Count; i++)
            {
                EncounterUnit u = _units[i];
                if (!u.IsActive || SurfaceDistanceM(u.Position, strike.Direction) > reachM + u.BodyRadiusM)
                {
                    continue;
                }

                int dealt = u.Combatant.ReceiveEnvironmentDamage(Element.Thunder, LightningSchedule.DamagePermilleOfMaxHp, nowMs);
                _events.Add(new EncounterEvent(EncounterEventKind.Struck, NoSource, u.Id, 0, dealt));
                if (u.Combatant.IsFainted)
                {
                    _events.Add(new EncounterEvent(EncounterEventKind.Faint, NoSource, u.Id, 0, 0));
                }
            }

            if (_player != null && !_player.IsDowned && SurfaceDistanceM(_playerCenter, strike.Direction) <= reachM + PlayerBodyRadiusM)
            {
                int amount = (int)(((long)_player.MaxHp * LightningSchedule.DamagePermilleOfMaxHp + 500) / 1000);
                int dealt = _player.ApplyDamage(math.max(1, amount), nowMs);
                _events.Add(new EncounterEvent(EncounterEventKind.Struck, NoSource, PlayerCharacterId, 0, dealt));
                if (_player.IsDowned)
                {
                    _events.Add(new EncounterEvent(EncounterEventKind.PlayerDowned, NoSource, PlayerCharacterId, 0, 0));
                }
            }

            if (strike.ObjectId < 0 || _objects == null || _objects.IsDestroyed(strike.ObjectId))
            {
                return;
            }

            int index = _objects.IndexOf(c.Id);
            if (index < 0)
            {
                index = _objects.Register(c.Id, c.Composition, c.VolumeL, c.Center);
            }

            ObjectHitResult r = _objects.ApplyHit(index, Element.Thunder, LightningSchedule.ObjectPower, 100, nowMs,
                lightning: true);
            _objectEvents.Add(new ObjectEvent(ObjectEventKind.Hit, c.Id, NoSource, r.Damage, r.Reactions, r.Cause,
                default, LightningSchedule.ObjectPower));
            if (r.Destroyed)
            {
                EmitDestroyed(index, NoSource, LightningSchedule.ObjectPower);
            }
        }

        private float SurfaceDistanceM(float3 position, float3 direction) =>
            math.distance(_planet.ToDirection(position), direction) * _planet.Radius;
    }
}
