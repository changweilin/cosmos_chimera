#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // PlayMode, not EditMode: the placeholder removes its collider with Object.Destroy, which edit mode refuses.
    public sealed class CreatureViewsTests
    {
        private static CreatureDef AnyCreature()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            Assert.That(db, Is.Not.Null);
            CreatureDef def = db.All.OfType<CreatureDef>().FirstOrDefault();
            Assert.That(def, Is.Not.Null, "at least one creature in the database");
            return def;
        }

        private static Shader Toon() =>
            AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader");

        // Material colours round-trip through the project's linear colour space, so channels drift by ~1e-7.
        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-4f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-4f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-4f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(1e-4f));
        }

        [Test]
        public void Scale_IsBodyDiameterTimesRoleFactor()
        {
            CreatureDef def = AnyCreature();
            Assert.That(PlaceholderCreatureViewFactory.Scale(new CreatureViewRequest(def, false, SpawnRole.Normal, 0.5f)),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(PlaceholderCreatureViewFactory.Scale(new CreatureViewRequest(def, false, SpawnRole.Elite, 0.5f)),
                Is.EqualTo(1.4f).Within(1e-6f));
            Assert.That(PlaceholderCreatureViewFactory.Scale(new CreatureViewRequest(def, false, SpawnRole.Boss, 0.5f)),
                Is.EqualTo(2.5f).Within(1e-6f));
        }

        [Test]
        public void PreViolations_Throw()
        {
            CreatureDef def = AnyCreature();
            Assert.Throws<ContractException>(() =>
                PlaceholderCreatureViewFactory.Scale(new CreatureViewRequest(def, false, SpawnRole.Normal, 0f)));
            Assert.Throws<ContractException>(() => PlaceholderCreatureViewFactory.ColorOf(null));
            var factory = new PlaceholderCreatureViewFactory(Toon());
            Assert.Throws<ContractException>(() => factory.Create(new CreatureViewRequest(null, true, SpawnRole.Normal, 0.5f)));
            int roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().rootCount;
            Assert.Throws<ContractException>(() => factory.Create(new CreatureViewRequest(def, true, SpawnRole.Normal, 0f)));
            Assert.That(UnityEngine.SceneManagement.SceneManager.GetActiveScene().rootCount, Is.EqualTo(roots),
                "a rejected request creates no scene object");
        }

        [UnityTest]
        public IEnumerator Create_ToonAndFallback_ColliderFreeSizedGreysOutAndDisposes([Values(true, false)] bool toon)
        {
            CreatureDef def = AnyCreature();
            var factory = new PlaceholderCreatureViewFactory(toon ? Toon() : null);
            var request = new CreatureViewRequest(def, true, SpawnRole.Elite, 0.4f);
            ICreatureView view = factory.Create(request);
            GameObject root = view.Root.gameObject;
            Material material = root.GetComponent<Renderer>().sharedMaterial;
            yield return null;

            Assert.That(root.GetComponent<Collider>() == null, Is.True, "views never take part in physics");
            Assert.That(root.transform.localScale.x, Is.EqualTo(PlaceholderCreatureViewFactory.Scale(request)).Within(1e-6f));
            Color expected = PlaceholderCreatureViewFactory.ColorOf(def);
            AssertColor(toon ? material.GetColor("_BaseColor") : material.color, expected);

            view.ShowFainted();
            view.ShowFainted();
            AssertColor(toon ? material.GetColor("_BaseColor") : material.color, Color.gray);

            view.Dispose();
            view.Dispose();
            yield return null;
            Assert.That(root == null, Is.True, "root destroyed");
            Assert.That(material == null, Is.True, "per-view material destroyed with the view");
        }
    }
}
#endif
