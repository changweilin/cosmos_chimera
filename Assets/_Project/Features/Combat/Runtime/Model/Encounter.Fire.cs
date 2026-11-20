using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;

namespace Cosmimera.Combat
{
    /// <summary>The six fire types of destructibles.md#延燒; decides spread reach, speed and the look.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum FireKind
    {
        /// <summary>Grass, reeds, shrubs, cloth: low and fast.</summary>
        Grass = 0,
        /// <summary>A single wooden body (trees): burns as a whole crown.</summary>
        Crown = 1,
        /// <summary>A built thing of several materials whose main one burns (wooden houses, tents).</summary>
        Structure = 2,
        /// <summary>Kerosene and the like: blasts on ignition.</summary>
        Chemical = 3,
        /// <summary>A burning structure with a large conductive share (wiring, devices).</summary>
        Electrical = 4,
        /// <summary>Set alight by lava (P3-22); spreads as Grass/Crown would but is drawn with ash.</summary>
        Lava = 5,
    }

    /// <summary>Fire-spread constants and fire-type classification. REQ: docs/design/destructibles.md#延燒 (數值暫定).</summary>
    /// <remarks>Pre: none. Post: pure. Side effects: none.</remarks>
    public static class FireRules
    {
        /// <summary>Spread step.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int StepMs = 1000;

        /// <summary>Heat (permille) at which a neighbour ignites.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int IgniteHeat = 1000;

        /// <summary>Blast radius of a chemical fire (ignites flammables, hurts units).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float BlastRadiusM = 4f;

        /// <summary>Unit damage of a blast, permille of max HP before fire resistance.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int BlastPermilleOfMaxHp = 150;

        /// <summary>Damage per second to a unit standing in flames, permille of max HP before fire resistance.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int BurnPermilleOfMaxHpPerS = 40;

        /// <summary>Downwind reach multiplier at full wind; upwind is <see cref="UpwindStretch"/>.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float DownwindStretch = 1.6f;

        /// <summary>Upwind reach multiplier at full wind.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float UpwindStretch = 0.5f;

        /// <summary>Fire type of an object of <paramref name="composition"/>.</summary>
        /// <remarks>
        /// Pre: composition non-null. Post: Chemical for a Blast main substance; Electrical when the main substance
        ///      burns and conductive parts make up ≥ 25 % of the weight; Structure for any other composite; Grass for a
        ///      fast-burning single substance; else Crown.
        /// Side effects: none.
        /// </remarks>
        public static FireKind KindOf(Composition composition)
        {
            Contract.Requires(composition != null, "composition required");
            Substance main = composition.Dominant;
            if (main.Flammable == Flammability.Blast) return FireKind.Chemical;
            if (composition.Count > 1)
            {
                int conductive = 0;
                for (int i = 0; i < composition.Count; i++)
                {
                    if (composition.Part(i).Conductive) conductive += composition.Weight(i);
                }

                return conductive * 4 >= composition.TotalWeight ? FireKind.Electrical : FireKind.Structure;
            }

            return main.Flammable == Flammability.Fast ? FireKind.Grass : FireKind.Crown;
        }

        /// <summary>Calm-air spread reach of a fire type (m).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public static float ReachM(FireKind kind) => kind switch
        {
            FireKind.Grass => 4f,
            FireKind.Crown => 7f,
            FireKind.Structure => 6f,
            FireKind.Chemical => BlastRadiusM,
            FireKind.Electrical => 3f,
            _ => 5f,
        };

        /// <summary>Heat (permille) a burning object gives a touching neighbour per second.</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public static int HeatPerS(FireKind kind) => kind switch
        {
            FireKind.Grass => 900,
            FireKind.Crown => 400,
            FireKind.Structure => 350,
            FireKind.Electrical => 300,
            _ => 400,
        };

        /// <summary>Distance from a burning object's centre within which a unit is in the flames (m).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public static float FlameReachM(FireKind kind) => kind switch
        {
            FireKind.Grass => 1.5f,
            FireKind.Crown => 2.5f,
            FireKind.Structure => 3f,
            _ => 1.5f,
        };

        /// <summary>Reach multiplier toward a neighbour, from wind.</summary>
        /// <remarks>
        /// Pre: cosToWind in [-1, 1] (cosine between the direction to the neighbour and the downwind tangent);
        ///      windPermille in [0, 1000].
        /// Post: 1 in calm air; DownwindStretch straight downwind and UpwindStretch straight upwind at full wind.
        /// Side effects: none.
        /// </remarks>
        public static float WindStretch(float cosToWind, int windPermille)
        {
            float w = windPermille / 1000f;
            float full = cosToWind >= 0f
                ? 1f + (DownwindStretch - 1f) * cosToWind
                : 1f + (1f - UpwindStretch) * cosToWind;
            return 1f + (full - 1f) * w;
        }

        /// <summary>Spread speed multiplier (permille) under wetness.</summary>
        /// <remarks>Pre: wetPermille in [0, 1000]. Post: 1000 when dry, 300 when fully wet. Side effects: none.</remarks>
        public static int WetSlowPermille(int wetPermille) => 1000 - 700 * wetPermille / 1000;

        /// <summary>Chance (permille) per second that rain puts a fire out.</summary>
        /// <remarks>Pre: wetPermille in [0, 1000]. Post: 0 below 600 wetness, 120 at full. Side effects: none.</remarks>
        public static int DouseChancePermille(int wetPermille) => wetPermille < 600 ? 0 : (wetPermille - 600) * 120 / 400;
    }

    public sealed partial class Encounter
    {
        private ulong _fireSeed;
        private Func<float3, long, int2> _windAt;
        private Func<float3, long, int> _wetAt;
        private readonly Dictionary<long, int> _heat = new Dictionary<long, int>();
        private readonly List<long> _heatKeys = new List<long>(32);
        private readonly List<int> _burningBuffer = new List<int>(32);
        private readonly List<DestructibleCandidate> _fireBuffer = new List<DestructibleCandidate>(32);
        private readonly List<float3> _blasts = new List<float3>(4);

        /// <summary>Gives spreading fire its wind and wetness. REQ: docs/design/destructibles.md#延燒.</summary>
        /// <remarks>
        /// Pre: windAt and wetAt non-null and deterministic; windAt(direction, timeMs) = (heading in tenths of a degree
        ///      from local east toward north, effective wind 0..1000); wetAt = effective rain/snow 0..1000.
        /// Post: spreading uses them from the next Tick; without this call the air is calm and dry.
        /// Side effects: stores the references.
        /// </remarks>
        public void SetFireEnvironment(ulong seed, Func<float3, long, int2> windAt, Func<float3, long, int> wetAt)
        {
            Contract.Requires(windAt != null && wetAt != null, "windAt and wetAt required");
            _fireSeed = seed;
            _windAt = windAt;
            _wetAt = wetAt;
        }

        /// <summary>Heat (permille) gathered so far toward igniting object <paramref name="id"/>.</summary>
        /// <remarks>Pre: none. Post: 0 when none. Side effects: none.</remarks>
        public int HeatOf(long id) => _heat.TryGetValue(id, out int h) ? h : 0;

        private void QueueBlast(float3 center) => _blasts.Add(center);

        private void StepFire(long previousMs, long nowMs)
        {
            if (_objects == null)
            {
                return;
            }

            ResolveBlasts(nowMs);
            long first = previousMs < 0 ? 1 : previousMs / FireRules.StepMs + 1;
            for (long step = first; step <= nowMs / FireRules.StepMs; step++)
            {
                SpreadStep(step, nowMs);
                ResolveBlasts(nowMs);
            }
        }

        private void SpreadStep(long step, long nowMs)
        {
            _burningBuffer.Clear();
            for (int i = 0; i < _objects.Count; i++)
            {
                if (_objects.IsBurning(i)) _burningBuffer.Add(i);
            }

            // Heat that is not renewed fades (a fire that moved on stops warming), so ignition needs a sustained source.
            _heatKeys.Clear();
            _heatKeys.AddRange(_heat.Keys);
            foreach (long key in _heatKeys)
            {
                int h = _heat[key] * 85 / 100;
                if (h <= 0) _heat.Remove(key);
                else _heat[key] = h;
            }

            foreach (int index in _burningBuffer)
            {
                float3 center = _objects.CenterOf(index);
                float3 up = _planet.ToDirection(center);
                FireKind kind = FireRules.KindOf(_objects.CompositionOf(index));
                int2 wind = _windAt != null ? _windAt(up, step * FireRules.StepMs) : int2.zero;
                int wet = _wetAt != null ? math.clamp(_wetAt(up, step * FireRules.StepMs), 0, 1000) : 0;

                // REQ: destructibles.md#延燒 — heavy rain or snow can put a fire out (rolled per object and second, off the
                // combat stream so attaching fire never changes combat replays).
                int douse = FireRules.DouseChancePermille(wet);
                if (douse > 0 && new Pcg32(_fireSeed ^ (ulong)_objects.IdOf(index), (ulong)step).NextInt(0, 1000) < douse)
                {
                    if (_objects.Extinguish(index))
                    {
                        _objectEvents.Add(new ObjectEvent(ObjectEventKind.Hit, _objects.IdOf(index), NoSource, 0,
                            ObjectReaction.Extinguished, DestroyCause.Damage, default, 0));
                    }

                    continue;
                }

                BurnUnits(center, kind, nowMs);
                float3 downwind = WindField.Tangent(up, wind.x);
                float reach = FireRules.ReachM(kind);
                int heat = FireRules.HeatPerS(kind) * FireRules.WetSlowPermille(wet) / 1000;
                _fireBuffer.Clear();
                _objectIndex.Query(center, reach * FireRules.DownwindStretch, _fireBuffer);
                foreach (DestructibleCandidate c in _fireBuffer)
                {
                    if (c.Id == _objects.IdOf(index) || _objects.IsDestroyed(c.Id) ||
                        c.Composition.Dominant.Flammable == Flammability.None)
                    {
                        continue;
                    }

                    int other = _objects.IndexOf(c.Id);
                    if (other >= 0 && _objects.IsBurning(other))
                    {
                        continue;
                    }

                    float3 toward = c.Center - center;
                    toward -= up * math.dot(toward, up);
                    float distance = math.length(toward);
                    float cos = distance > 1e-4f ? math.dot(toward / distance, downwind) : 0f;
                    float effective = reach * FireRules.WindStretch(cos, wind.y);
                    if (distance >= effective)
                    {
                        continue;
                    }

                    int add = (int)(heat * (1f - distance / effective));
                    int total = HeatOf(c.Id) + math.max(1, add);
                    if (total < FireRules.IgniteHeat)
                    {
                        _heat[c.Id] = total;
                        continue;
                    }

                    _heat.Remove(c.Id);
                    if (other < 0) other = _objects.Register(c.Id, c.Composition, c.VolumeL, c.Center);
                    IgniteSlot(other, nowMs);
                }
            }
        }

        private void IgniteSlot(int index, long nowMs)
        {
            ObjectHitResult r = _objects.Ignite(index, nowMs);
            if (r.Reactions == ObjectReaction.None)
            {
                return;
            }

            _objectEvents.Add(new ObjectEvent(ObjectEventKind.Hit, _objects.IdOf(index), NoSource, r.Damage, r.Reactions,
                r.Cause, default, 0));
            if (r.Destroyed)
            {
                EmitDestroyed(index, NoSource, 0);
            }

            if ((r.Reactions & ObjectReaction.Blasted) != 0)
            {
                QueueBlast(_objects.CenterOf(index));
            }
        }

        // REQ: destructibles.md#延燒 — kerosene flashes: flammables within 4 m catch, units within 4 m are hurt.
        // Chains (a blast igniting more kerosene) resolve in the same call, in queue order.
        private void ResolveBlasts(long nowMs)
        {
            for (int b = 0; b < _blasts.Count; b++)
            {
                float3 center = _blasts[b];
                for (int i = 0; i < _units.Count; i++)
                {
                    EncounterUnit u = _units[i];
                    if (!u.IsActive || math.distance(u.Position, center) > FireRules.BlastRadiusM + u.BodyRadiusM) continue;
                    int dealt = u.Combatant.ReceiveEnvironmentDamage(Element.Fire, FireRules.BlastPermilleOfMaxHp, nowMs);
                    ScorchEvents(u, dealt);
                }

                if (_player != null && !_player.IsDowned &&
                    math.distance(_playerCenter, center) <= FireRules.BlastRadiusM + PlayerBodyRadiusM)
                {
                    ScorchPlayer(FireRules.BlastPermilleOfMaxHp, nowMs);
                }

                _fireBuffer.Clear();
                _objectIndex.Query(center, FireRules.BlastRadiusM, _fireBuffer);
                foreach (DestructibleCandidate c in _fireBuffer)
                {
                    if (_objects.IsDestroyed(c.Id) || c.Composition.Dominant.Flammable == Flammability.None) continue;
                    int index = _objects.IndexOf(c.Id);
                    if (index < 0) index = _objects.Register(c.Id, c.Composition, c.VolumeL, c.Center);
                    IgniteSlot(index, nowMs);
                }
            }

            _blasts.Clear();
        }

        private void BurnUnits(float3 center, FireKind kind, long nowMs)
        {
            float reach = FireRules.FlameReachM(kind);
            for (int i = 0; i < _units.Count; i++)
            {
                EncounterUnit u = _units[i];
                if (!u.IsActive || SurfaceDistanceM(u.Position, _planet.ToDirection(center)) > reach + u.BodyRadiusM) continue;
                int dealt = u.Combatant.ReceiveEnvironmentDamage(Element.Fire, FireRules.BurnPermilleOfMaxHpPerS, nowMs);
                ScorchEvents(u, dealt);
            }

            if (_player != null && !_player.IsDowned &&
                SurfaceDistanceM(_playerCenter, _planet.ToDirection(center)) <= reach + PlayerBodyRadiusM)
            {
                ScorchPlayer(FireRules.BurnPermilleOfMaxHpPerS, nowMs);
            }
        }

        private void ScorchEvents(EncounterUnit u, int dealt)
        {
            _events.Add(new EncounterEvent(EncounterEventKind.Scorched, NoSource, u.Id, 0, dealt));
            if (u.Combatant.IsFainted)
            {
                _events.Add(new EncounterEvent(EncounterEventKind.Faint, NoSource, u.Id, 0, 0));
            }
        }

        private void ScorchPlayer(int permille, long nowMs)
        {
            int amount = (int)(((long)_player.MaxHp * permille + 500) / 1000);
            int dealt = _player.ApplyDamage(math.max(1, amount), nowMs);
            _events.Add(new EncounterEvent(EncounterEventKind.Scorched, NoSource, PlayerCharacterId, 0, dealt));
            if (_player.IsDowned)
            {
                _events.Add(new EncounterEvent(EncounterEventKind.PlayerDowned, NoSource, PlayerCharacterId, 0, 0));
            }
        }
    }
}
