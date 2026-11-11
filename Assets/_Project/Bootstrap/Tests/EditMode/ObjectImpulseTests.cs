using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: destructibles.md#物理 — impulse ∝ power, along caster → object with a lift, heavy objects never move.
    public sealed class ObjectImpulseTests
    {
        private static readonly PlanetConfig Planet = new PlanetConfig(320f, float3.zero);
        private static readonly ObjectPhysicsTuning Tuning = ObjectPhysicsTuning.Default;

        private static float3 At(float alongM, float upM) => Planet.ToWorld(
            new float3(math.sin(alongM / 320f), math.cos(alongM / 320f), 0f), upM);

        private static Substance S(string id, int density) =>
            new Substance(id, Element.Earth, Flammability.None, false, Brittleness.Never, 500, density, 10, new int[10]);

        [Test]
        public void Points_away_from_the_caster_with_a_lift_and_scales_with_power()
        {
            float3 source = At(0f, 0.5f);
            float3 target = At(3f, 1f);
            float3 i60 = ObjectImpulse.Compute(Planet, source, target, 60, 100, 270f, Tuning);
            float3 i120 = ObjectImpulse.Compute(Planet, source, target, 120, 100, 270f, Tuning);
            Assert.That(math.length(i60), Is.EqualTo(60 * Tuning.ImpulsePerPowerNs).Within(1e-2f));
            Assert.That(math.length(i120), Is.EqualTo(2f * math.length(i60)).Within(1e-2f));

            float3 up = Planet.ToDirection(target);
            float3 away = target - source;
            away = math.normalize(away - math.dot(away, up) * up);
            float3 dir = math.normalize(i60);
            Assert.That(math.dot(dir, away), Is.GreaterThan(0.9f), "mostly along caster → object");
            Assert.That(math.dot(dir, up), Is.EqualTo(Tuning.Lift / math.sqrt(1f + Tuning.Lift * Tuning.Lift)).Within(1e-3f));
        }

        [Test]
        public void Gust_multiplies_and_mass_does_not()
        {
            float3 source = At(0f, 0.5f);
            float3 target = At(3f, 1f);
            float plain = math.length(ObjectImpulse.Compute(Planet, source, target, 50, 100, 10f, Tuning));
            float gust = math.length(ObjectImpulse.Compute(Planet, source, target, 50, 200, 10f, Tuning));
            float heavy = math.length(ObjectImpulse.Compute(Planet, source, target, 50, 100, 10_000f, Tuning));
            Assert.That(gust, Is.EqualTo(2f * plain).Within(1e-2f));
            Assert.That(heavy, Is.EqualTo(plain).Within(1e-3f), "velocity change, not impulse, falls with mass");
        }

        [Test]
        public void Immovable_or_powerless_hits_give_no_impulse()
        {
            float3 source = At(0f, 0.5f);
            float3 target = At(3f, 1f);
            Assert.That(ObjectImpulse.Compute(Planet, source, target, 80, 100, Tuning.MaxMovableKg + 1f, Tuning),
                Is.EqualTo(float3.zero));
            Assert.That(ObjectImpulse.Compute(Planet, source, target, 0, 100, 10f, Tuning), Is.EqualTo(float3.zero));
            Assert.That(math.length(ObjectImpulse.Compute(Planet, source, target, 80, 100, Tuning.MaxMovableKg, Tuning)),
                Is.GreaterThan(0f), "the threshold itself still moves");
        }

        [Test]
        public void A_caster_straight_below_still_gets_a_finite_direction()
        {
            float3 target = At(0f, 3f);
            float3 i = ObjectImpulse.Compute(Planet, At(0f, 0.5f), target, 40, 100, 10f, Tuning);
            Assert.That(math.all(math.isfinite(i)), Is.True);
            Assert.That(math.length(i), Is.EqualTo(40 * Tuning.ImpulsePerPowerNs).Within(1e-2f));
        }

        [Test]
        public void Mass_is_the_weighted_density_times_volume()
        {
            Assert.That(ObjectImpulse.MassKg(Composition.Of(S("wood", 900)), 300), Is.EqualTo(270f).Within(1e-3f));
            var mix = new Composition(new[] { S("a", 1000), S("b", 3000) }, new[] { 3, 1 });
            Assert.That(ObjectImpulse.MassKg(mix, 2000), Is.EqualTo(3000f).Within(1e-2f));
        }

        [Test]
        public void Contract_violations_throw()
        {
            float3 ok = At(0f, 0.5f);
            ObjectPhysicsTuning bad = Tuning;
            bad.MaxAwake = 0;
            Assert.Throws<ContractException>(() => ObjectImpulse.Compute(Planet, ok, ok, 1, 100, 1f, bad));
            Assert.Throws<ContractException>(() => ObjectImpulse.Compute(Planet, new float3(float.NaN), ok, 1, 100, 1f, Tuning));
            Assert.Throws<ContractException>(() => ObjectImpulse.Compute(Planet, ok, ok, -1, 100, 1f, Tuning));
            Assert.Throws<ContractException>(() => ObjectImpulse.Compute(Planet, ok, ok, 1, -1, 1f, Tuning));
            Assert.Throws<ContractException>(() => ObjectImpulse.Compute(Planet, ok, ok, 1, 100, -1f, Tuning));
            Assert.Throws<ContractException>(() => ObjectImpulse.MassKg(null, 1));
            Assert.Throws<ContractException>(() => ObjectImpulse.MassKg(Composition.Of(S("a", 1)), -1));
        }
    }
}
