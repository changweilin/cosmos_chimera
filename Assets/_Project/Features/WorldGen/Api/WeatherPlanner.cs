using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Deterministic weather scheduler driven by planet type, local terrain, seed, and simulation time.
    /// REQ: docs/design/planet-world.md#天氣影響.
    /// </summary>
    /// <remarks>Pre: none. Post: pure and reproducible across host and clients. Side effects: none.</remarks>
    public static class WeatherPlanner
    {
        /// <summary>Default duration of one dynamic weather slot in milliseconds (180 s).</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const int DefaultSlotDurationMs = 180_000;

        private const ulong WeatherStreamBase = 0x57454154UL; // "WEAT"
        private const int WeatherCount = 8;

        /// <summary>Deterministically selects the active <see cref="WeatherProfile"/> for a planet, terrain, and time.</summary>
        /// <remarks>
        /// Pre: <paramref name="timeMs"/> &gt;= 0; <paramref name="slotDurationMs"/> &gt; 0.
        /// Post: valid <see cref="WeatherProfile"/>; identical (seed, planetTypeId, terrainId, timeMs, slotDurationMs)
        ///       always produces the same weather.
        /// Side effects: none (pure).
        /// </remarks>
        public static WeatherProfile WeatherAt(
            ulong seed,
            string planetTypeId,
            string terrainId,
            long timeMs,
            int slotDurationMs = DefaultSlotDurationMs)
        {
            Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
            Contract.Requires(slotDurationMs > 0, "slotDurationMs must be > 0");

            long slot = timeMs / slotDurationMs;
            ulong terrainHash = HashId(terrainId);
            var rng = new Pcg32(seed ^ terrainHash, WeatherStreamBase + (ulong)slot);

            // Stack-allocated weights (indices match WeatherKind 0..7) so hot queries allocate zero heap memory.
            SpanWeights weights = default;
            FillPlanetBaseWeights(planetTypeId, ref weights);
            ApplyTerrainBias(terrainId, ref weights);

            int total = 0;
            for (int i = 0; i < WeatherCount; i++)
            {
                total += weights.Get(i);
            }

            if (total <= 0)
            {
                return WeatherProfiles.Clear;
            }

            int roll = rng.NextInt(0, total);
            int acc = 0;
            for (int i = 0; i < WeatherCount; i++)
            {
                acc += weights.Get(i);
                if (roll < acc)
                {
                    return WeatherProfiles.For((WeatherKind)i);
                }
            }

            return WeatherProfiles.Clear;
        }

        private static void FillPlanetBaseWeights(string planetTypeId, ref SpanWeights w)
        {
            switch (planetTypeId)
            {
                case "verdant_planet":
                    w.Set(WeatherKind.Clear, 40);
                    w.Set(WeatherKind.Rain, 25);
                    w.Set(WeatherKind.Fog, 15);
                    w.Set(WeatherKind.Gale, 10);
                    w.Set(WeatherKind.Thunderstorm, 10);
                    break;
                case "lava_planet":
                    w.Set(WeatherKind.Heatwave, 45);
                    w.Set(WeatherKind.Clear, 25);
                    w.Set(WeatherKind.Sandstorm, 15);
                    w.Set(WeatherKind.Thunderstorm, 15);
                    break;
                case "ice_crystal_planet":
                    w.Set(WeatherKind.Blizzard, 40);
                    w.Set(WeatherKind.Fog, 20);
                    w.Set(WeatherKind.Clear, 25);
                    w.Set(WeatherKind.Gale, 15);
                    break;
                case "storm_planet":
                    w.Set(WeatherKind.Thunderstorm, 35);
                    w.Set(WeatherKind.Gale, 30);
                    w.Set(WeatherKind.Rain, 20);
                    w.Set(WeatherKind.Clear, 15);
                    break;
                case "mineral_planet":
                    w.Set(WeatherKind.Sandstorm, 35);
                    w.Set(WeatherKind.Clear, 35);
                    w.Set(WeatherKind.Gale, 15);
                    w.Set(WeatherKind.Fog, 15);
                    break;
                case "twilight_planet":
                    w.Set(WeatherKind.Fog, 40);
                    w.Set(WeatherKind.Clear, 25);
                    w.Set(WeatherKind.Thunderstorm, 20);
                    w.Set(WeatherKind.Blizzard, 15);
                    break;
                default:
                    w.Set(WeatherKind.Clear, 50);
                    w.Set(WeatherKind.Rain, 20);
                    w.Set(WeatherKind.Fog, 15);
                    w.Set(WeatherKind.Gale, 15);
                    break;
            }
        }

        private static void ApplyTerrainBias(string terrainId, ref SpanWeights w)
        {
            switch (terrainId)
            {
                case "swamp":
                case "rainforest":
                    w.Add(WeatherKind.Fog, 25);
                    w.Add(WeatherKind.Rain, 20);
                    break;
                case "desert":
                    w.Add(WeatherKind.Sandstorm, 30);
                    w.Add(WeatherKind.Heatwave, 20);
                    break;
                case "ice_field":
                case "ice_cap":
                    w.Add(WeatherKind.Blizzard, 35);
                    break;
                case "volcano_foothills":
                case "volcano":
                    w.Add(WeatherKind.Heatwave, 35);
                    break;
                case "plateau":
                case "mountain":
                    w.Add(WeatherKind.Gale, 25);
                    w.Add(WeatherKind.Thunderstorm, 15);
                    break;
                case "archipelago":
                case "fjord":
                case "deep_sea":
                    w.Add(WeatherKind.Rain, 20);
                    w.Add(WeatherKind.Fog, 15);
                    break;
                case "eternal_night":
                case "cave":
                case "labyrinth":
                    w.Add(WeatherKind.Fog, 25);
                    break;
            }
        }

        private static ulong HashId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return 0x9E3779B97F4A7C15UL;
            }

            // FNV-1a 64-bit so terrain ids produce stable hashes across runtimes (string.GetHashCode is randomized per process).
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < id.Length; i++)
            {
                h ^= id[i];
                h *= 1099511628211UL;
            }

            return h;
        }

        private struct SpanWeights
        {
            private int _w0, _w1, _w2, _w3, _w4, _w5, _w6, _w7;

            public int Get(int index) => index switch
            {
                0 => _w0, 1 => _w1, 2 => _w2, 3 => _w3,
                4 => _w4, 5 => _w5, 6 => _w6, 7 => _w7,
                _ => 0,
            };

            public void Set(WeatherKind kind, int value) => AddOrSet((int)kind, value, add: false);
            public void Add(WeatherKind kind, int delta) => AddOrSet((int)kind, delta, add: true);

            private void AddOrSet(int index, int v, bool add)
            {
                switch (index)
                {
                    case 0: _w0 = add ? _w0 + v : v; break;
                    case 1: _w1 = add ? _w1 + v : v; break;
                    case 2: _w2 = add ? _w2 + v : v; break;
                    case 3: _w3 = add ? _w3 + v : v; break;
                    case 4: _w4 = add ? _w4 + v : v; break;
                    case 5: _w5 = add ? _w5 + v : v; break;
                    case 6: _w6 = add ? _w6 + v : v; break;
                    case 7: _w7 = add ? _w7 + v : v; break;
                }
            }
        }
    }
}
