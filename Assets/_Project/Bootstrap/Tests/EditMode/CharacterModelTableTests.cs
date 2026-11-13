using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests.EditMode
{
    public sealed class CharacterModelTableTests
    {
        [Test]
        public void TryFind_returns_registered_entry_and_rejects_missing_or_empty()
        {
            var table = ScriptableObject.CreateInstance<CharacterModelTable>();
            var dummyPrefab = new GameObject("DummyChar");
            try
            {
                table.SetEntries(new List<CharacterModelEntry>
                {
                    new CharacterModelEntry("chr_amemori_sumiha", "雨森澄羽", dummyPrefab, null, true, "task-1"),
                });

                Assert.That(table.TryFind("chr_amemori_sumiha", out CharacterModelEntry found), Is.True);
                Assert.That(found.displayName, Is.EqualTo("雨森澄羽"));
                Assert.That(found.isProvisional, Is.True);
                Assert.That(table.FindPrefab("chr_amemori_sumiha"), Is.SameAs(dummyPrefab));

                Assert.That(table.TryFind("chr_missing", out _), Is.False);
                Assert.That(table.FindPrefab("chr_missing"), Is.Null);
                Assert.That(table.TryFind(string.Empty, out _), Is.False);
                Assert.That(table.TryFind(null, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(dummyPrefab);
                Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void CharacterAnimatorDriver_Configure_enforces_non_null_contracts()
        {
            var go = new GameObject("DriverTest");
            var body = go.AddComponent<Rigidbody>();
            var col = go.AddComponent<CapsuleCollider>();
            var character = go.AddComponent<SphericalCharacter>();
            var animator = go.AddComponent<Animator>();
            var driver = go.AddComponent<CharacterAnimatorDriver>();

            try
            {
                Assert.Throws<ContractException>(() => driver.Configure(null, animator));
                Assert.Throws<ContractException>(() => driver.Configure(character, null));
                Assert.DoesNotThrow(() => driver.Configure(character, animator));
                // Keep unused locals referenced cleanly.
                Assert.That(body != null && col != null, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Registered_provisional_prefabs_have_renderer_and_animator()
        {
            var table = AssetDatabase.LoadAssetAtPath<CharacterModelTable>(
                CharacterModelTableBuilder.TableAssetPath);
            Assert.That(table, Is.Not.Null);
            Assert.That(table.Entries, Is.Not.Empty);

            foreach (CharacterModelEntry entry in table.Entries)
            {
                Assert.That(entry.prefab, Is.Not.Null, $"Prefab missing for {entry.id}");
                Assert.That(entry.prefab.GetComponentsInChildren<Renderer>(true), Is.Not.Empty,
                    $"No Renderer found on {entry.id} prefab");
                Animator animator = entry.prefab.GetComponentInChildren<Animator>(true);
                Assert.That(animator, Is.Not.Null, $"No Animator found on {entry.id} prefab");
                Assert.That(animator.runtimeAnimatorController, Is.Not.Null,
                    $"No RuntimeAnimatorController assigned on {entry.id} prefab");
            }
        }
    }
}
