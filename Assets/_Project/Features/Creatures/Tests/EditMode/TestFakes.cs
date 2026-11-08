using System;
using System.Collections.Generic;
using Cosmimera.GameData;

namespace Cosmimera.Creatures.Tests
{
    internal sealed class FakeRelationGraph : IRelationGraph
    {
        private readonly HashSet<string> _edges = new HashSet<string>();

        public int Calls { get; private set; }

        public string LastA { get; private set; }

        public string LastB { get; private set; }

        public FakeRelationGraph Relate(string a, string b)
        {
            _edges.Add(Key(a, b));
            return this;
        }

        public FakeRelationGraph Unrelate(string a, string b)
        {
            _edges.Remove(Key(a, b));
            return this;
        }

        public bool AreRelated(string speciesIdA, string speciesIdB)
        {
            Calls++;
            LastA = speciesIdA;
            LastB = speciesIdB;
            return _edges.Contains(Key(speciesIdA, speciesIdB));
        }

        private static string Key(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
    }

    internal sealed class FakeSpeciesLookup : ISpeciesLookup
    {
        private readonly Dictionary<string, SpeciesInfo> _species = new Dictionary<string, SpeciesInfo>();

        public FakeSpeciesLookup Add(string speciesId, string moveId, string traitId)
        {
            _species[speciesId] = new SpeciesInfo(speciesId, moveId, traitId, CreatureStage.Juvenile);
            return this;
        }

        public bool TryGet(string speciesId, out SpeciesInfo info) => _species.TryGetValue(speciesId, out info);
    }

    /// Partner-rule tests that are not about stages: every species is known, at DefaultStage unless overridden.
    internal sealed class FakeStages : ISpeciesLookup
    {
        private readonly Dictionary<string, CreatureStage> _overrides = new Dictionary<string, CreatureStage>();

        public CreatureStage DefaultStage { get; set; } = CreatureStage.Juvenile;

        public FakeStages Set(string speciesId, CreatureStage stage)
        {
            _overrides[speciesId] = stage;
            return this;
        }

        public bool TryGet(string speciesId, out SpeciesInfo info)
        {
            CreatureStage stage = _overrides.TryGetValue(speciesId, out CreatureStage s) ? s : DefaultStage;
            info = new SpeciesInfo(speciesId, "move_" + speciesId, "trait_" + speciesId, stage);
            return true;
        }
    }

    internal static class Make
    {
        public static OwnedCreature Creature(int id, string species, VesselKind vessel = VesselKind.GlassBead, int exp = 0)
            => new OwnedCreature(id, species, vessel, exp);

        public static Func<int, string> SpeciesOf(Dictionary<int, string> table) => id => table[id];
    }
}
