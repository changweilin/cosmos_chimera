using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Lobby;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Lobby.Tests
{
    public sealed class MainWorldNpcRulesTests
    {
        [Test]
        public void BuildSettlementNpcs_places_six_distinct_roles_per_settlement_on_sphere()
        {
            var centers = new[]
            {
                math.normalize(new float3(0f, 1f, 0f)),
                math.normalize(new float3(1f, 0f, 0f)),
                math.normalize(new float3(0f, -0.6f, 0.8f)),
            };

            List<SettlementNpcPlacement> a = MainWorldNpcRules.BuildSettlementNpcs(centers, 2560f, 20261121UL);
            List<SettlementNpcPlacement> b = MainWorldNpcRules.BuildSettlementNpcs(centers, 2560f, 20261121UL);

            Assert.That(a.Count, Is.EqualTo(centers.Length * 6));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].SettlementIndex, Is.EqualTo(b[i].SettlementIndex));
                Assert.That(a[i].Role, Is.EqualTo(b[i].Role));
                Assert.That(a[i].Direction, Is.EqualTo(b[i].Direction));
                Assert.That(math.length(a[i].Direction), Is.EqualTo(1f).Within(1e-3f));
                Assert.That(math.abs(math.dot(a[i].Direction, a[i].Facing)), Is.LessThan(0.02f));
            }

            // Ensure no two NPCs in the same settlement overlap.
            for (int s = 0; s < centers.Length; s++)
            {
                int offset = s * 6;
                var roles = new HashSet<SettlementNpcRole>();
                for (int r = 0; r < 6; r++)
                {
                    Assert.That(roles.Add(a[offset + r].Role), Is.True);
                    for (int r2 = r + 1; r2 < 6; r2++)
                    {
                        float distM = math.distance(a[offset + r].Direction, a[offset + r2].Direction) * 2560f;
                        Assert.That(distM, Is.GreaterThan(5f));
                    }
                }
            }
        }

        [Test]
        public void Vessel_conversion_rules_and_preconditions()
        {
            Assert.That(MainWorldNpcRules.CanConvertVessel(false), Is.False);
            Assert.That(MainWorldNpcRules.CanConvertVessel(true), Is.True);

            MaterialStack cost = MainWorldNpcRules.VesselConvertCost(Element.Fire);
            Assert.That(cost.Key, Is.EqualTo(new MaterialKey(Element.Fire, 1)));
            Assert.That(cost.Qty, Is.EqualTo(2));

            Assert.Throws<ContractException>(() => MainWorldNpcRules.BuildSettlementNpcs(null, 2560f, 1UL));
            Assert.Throws<ContractException>(() => MainWorldNpcRules.BuildSettlementNpcs(new[] { new float3(0f, 1f, 0f) }, 0f, 1UL));
            Assert.Throws<ContractException>(() => MainWorldNpcRules.VesselConvertCost((Element)99));
        }
    }
}
