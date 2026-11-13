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
                Is.EqualTo(1f).Within(1e-6f));
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
        public IEnumerator Placeholder_baseline_covers_all_elements_and_both_primitives()
        {
            Color[] colors = {
                new Color(0.75f, 0.75f, 0.8f), new Color(0.3f, 0.7f, 0.25f), new Color(0.2f, 0.5f, 0.95f),
                new Color(0.95f, 0.35f, 0.15f), new Color(0.6f, 0.45f, 0.25f), new Color(0.6f, 0.9f, 1f),
                new Color(0.6f, 0.95f, 0.7f), new Color(0.95f, 0.85f, 0.2f), new Color(1f, 0.97f, 0.75f),
                new Color(0.35f, 0.25f, 0.5f),
            };
            Shader shader = Toon();
            var factory = new PlaceholderCreatureViewFactory(shader);
            CreatureDef def = ScriptableObject.CreateInstance<CreatureDef>();
            var serialized = new SerializedObject(def);
            SerializedProperty mastery = serialized.FindProperty("mastery");
            mastery.arraySize = 1;
            for (int i = 0; i < colors.Length; i++)
            {
                mastery.GetArrayElementAtIndex(0).FindPropertyRelative("element").enumValueIndex = i;
                mastery.GetArrayElementAtIndex(0).FindPropertyRelative("value").intValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                foreach (bool ally in new[] { true, false })
                {
                    ICreatureView view = factory.Create(new CreatureViewRequest(def, ally, SpawnRole.Normal, 0.5f));
                    GameObject root = view.Root.gameObject;
                    Assert.That(root.GetComponent<CapsuleCollider>() != null, Is.EqualTo(ally));
                    Assert.That(root.GetComponent<SphereCollider>() != null, Is.EqualTo(!ally));
                    Material material = root.GetComponent<Renderer>().sharedMaterial;
                    Assert.That(material.shader, Is.SameAs(shader));
                    Assert.That(material.GetFloat("_OutlineWidth"), Is.EqualTo(0.03f).Within(1e-6f));
                    AssertColor(material.GetColor("_BaseColor"), colors[i]);
                    view.Dispose();
                }
            }

            Object.Destroy(def);
            yield return null;
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

        [UnityTest]
        public IEnumerator Procedural_Create_BuildsMultiPartColliderFreeGreysOutAndDisposes([Values(true, false)] bool toon)
        {
            CreatureDef def = AnyCreature();
            var factory = new ProceduralCreatureViewFactory(toon ? Toon() : null);
            var request = new CreatureViewRequest(def, true, SpawnRole.Boss, 0.5f);
            ICreatureView view = factory.Create(request);
            GameObject root = view.Root.gameObject;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            yield return null;

            Assert.That(renderers.Length, Is.GreaterThan(1), "procedural 3D creature has multiple parts");
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty, "views never take part in physics");

            view.ShowFainted();
            view.ShowFainted();
            Material firstMat = renderers[0].sharedMaterial;
            AssertColor(toon ? firstMat.GetColor("_BaseColor") : firstMat.color, Color.gray);

            view.Dispose();
            view.Dispose();
            yield return null;
            Assert.That(root == null, Is.True, "root destroyed");
            Assert.That(firstMat == null, Is.True, "owned material destroyed with the view");
        }
    }
}
#endif
