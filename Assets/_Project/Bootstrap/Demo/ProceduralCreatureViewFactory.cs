using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Procedural 3D creature model builder used when a species has no dedicated 3D asset (or 2D concept art).
    /// Constructs a multi-part toon-shaded 3D body, head, eyes, limbs, tail, and stage/role silhouette.
    /// </summary>
    /// <remarks>
    /// Pre: none at construction; toonShader may be null (falls back to primitive default material).
    /// Side effects: per member.
    /// </remarks>
    internal sealed class ProceduralCreatureViewFactory : ICreatureViewFactory
    {
        private readonly Shader _toonShader;

        /// <summary>Creates a procedural 3D creature view factory shaded with <paramref name="toonShader"/> when given.</summary>
        /// <remarks>Pre: none. Post: ready to create views. Side effects: none.</remarks>
        public ProceduralCreatureViewFactory(Shader toonShader)
        {
            _toonShader = toonShader;
        }

        /// <inheritdoc/>
        public ICreatureView Create(in CreatureViewRequest request)
        {
            Contract.Requires(request.Def != null, "def required");
            float diameter = PlaceholderCreatureViewFactory.Scale(request);
            float r = diameter * 0.5f;
            Element element = RunPlanner.MainElement(request.Def);
            Color primary = PlaceholderCreatureViewFactory.ColorOf(request.Def);
            Color accent = Color.Lerp(primary, Color.white, 0.35f);
            Color dark = Color.Lerp(primary, Color.black, 0.55f);

            var root = new GameObject((request.Ally ? "Ally " : "Wild ") + request.Def.Id);
            var materials = new List<Material>(4);
            Material bodyMat = CreateMaterial(primary, 0.03f, materials);
            Material accentMat = CreateMaterial(accent, 0.02f, materials);
            Material eyeMat = CreateMaterial(new Color(0.08f, 0.08f, 0.1f), 0.01f, materials);

            // Center torso above ground (feet at y=0) so the creature stands on the planet surface.
            float legH = r * 0.45f;
            float torsoY = legH + r * 0.55f;
            bool winged = element == Element.Wind || element == Element.Light || element == Element.Fire;
            bool horned = request.Def.Stage >= CreatureStage.Adolescent || element == Element.Metal || element == Element.Earth || element == Element.Thunder;

            // Torso
            AddPart(root.transform, "Torso", PrimitiveType.Capsule, bodyMat,
                new Vector3(0f, torsoY, 0f),
                Quaternion.Euler(90f, 0f, 0f),
                new Vector3(r * 1.15f, r * 0.85f, r * 1.05f));

            // Head (proportional: larger relative head for Juvenile)
            float headScale = request.Def.Stage == CreatureStage.Juvenile ? r * 0.9f : r * 0.72f;
            float headY = torsoY + r * 0.55f;
            float headZ = r * 0.75f;
            AddPart(root.transform, "Head", PrimitiveType.Sphere, bodyMat,
                new Vector3(0f, headY, headZ),
                Quaternion.identity,
                new Vector3(headScale, headScale * 0.92f, headScale * 0.95f));

            // Eyes (+Z forward)
            float eyeSize = headScale * 0.22f;
            float eyeZ = headZ + headScale * 0.4f;
            float eyeY = headY + headScale * 0.1f;
            float eyeX = headScale * 0.24f;
            AddPart(root.transform, "EyeL", PrimitiveType.Sphere, eyeMat,
                new Vector3(-eyeX, eyeY, eyeZ), Quaternion.identity, Vector3.one * eyeSize);
            AddPart(root.transform, "EyeR", PrimitiveType.Sphere, eyeMat,
                new Vector3(eyeX, eyeY, eyeZ), Quaternion.identity, Vector3.one * eyeSize);

            // Ears / Horns
            if (horned)
            {
                float hornH = headScale * (request.Def.Stage == CreatureStage.Adult ? 0.65f : 0.45f);
                AddPart(root.transform, "HornL", PrimitiveType.Cylinder, accentMat,
                    new Vector3(-headScale * 0.26f, headY + headScale * 0.48f, headZ),
                    Quaternion.Euler(-15f, 0f, 18f),
                    new Vector3(headScale * 0.16f, hornH * 0.5f, headScale * 0.16f));
                AddPart(root.transform, "HornR", PrimitiveType.Cylinder, accentMat,
                    new Vector3(headScale * 0.26f, headY + headScale * 0.48f, headZ),
                    Quaternion.Euler(-15f, 0f, -18f),
                    new Vector3(headScale * 0.16f, hornH * 0.5f, headScale * 0.16f));
            }
            else
            {
                AddPart(root.transform, "EarL", PrimitiveType.Capsule, accentMat,
                    new Vector3(-headScale * 0.34f, headY + headScale * 0.42f, headZ - headScale * 0.05f),
                    Quaternion.Euler(0f, 0f, 25f),
                    new Vector3(headScale * 0.22f, headScale * 0.28f, headScale * 0.18f));
                AddPart(root.transform, "EarR", PrimitiveType.Capsule, accentMat,
                    new Vector3(headScale * 0.34f, headY + headScale * 0.42f, headZ - headScale * 0.05f),
                    Quaternion.Euler(0f, 0f, -25f),
                    new Vector3(headScale * 0.22f, headScale * 0.28f, headScale * 0.18f));
            }

            // Four limbs
            float legW = r * 0.32f;
            float legX = r * 0.42f;
            float legZ = r * 0.48f;
            Material limbMat = CreateMaterial(dark, 0.02f, materials);
            AddPart(root.transform, "LegFL", PrimitiveType.Cylinder, limbMat,
                new Vector3(-legX, legH * 0.5f, legZ), Quaternion.identity, new Vector3(legW, legH * 0.5f, legW));
            AddPart(root.transform, "LegFR", PrimitiveType.Cylinder, limbMat,
                new Vector3(legX, legH * 0.5f, legZ), Quaternion.identity, new Vector3(legW, legH * 0.5f, legW));
            AddPart(root.transform, "LegBL", PrimitiveType.Cylinder, limbMat,
                new Vector3(-legX, legH * 0.5f, -legZ), Quaternion.identity, new Vector3(legW, legH * 0.5f, legW));
            AddPart(root.transform, "LegBR", PrimitiveType.Cylinder, limbMat,
                new Vector3(legX, legH * 0.5f, -legZ), Quaternion.identity, new Vector3(legW, legH * 0.5f, legW));

            // Tail (-Z)
            float tailLen = request.Def.Stage == CreatureStage.Adult ? r * 0.9f : r * 0.6f;
            AddPart(root.transform, "Tail", PrimitiveType.Capsule, accentMat,
                new Vector3(0f, torsoY + r * 0.15f, -r * 0.95f),
                Quaternion.Euler(-65f, 0f, 0f),
                new Vector3(r * 0.3f, tailLen * 0.5f, r * 0.3f));

            // Wings / Dorsal crest for winged elements or Adult stage
            if (winged || request.Def.Stage == CreatureStage.Adult)
            {
                AddPart(root.transform, "WingL", PrimitiveType.Cube, accentMat,
                    new Vector3(-r * 0.78f, torsoY + r * 0.35f, -r * 0.05f),
                    Quaternion.Euler(10f, 0f, 35f),
                    new Vector3(r * 0.85f, r * 0.08f, r * 0.55f));
                AddPart(root.transform, "WingR", PrimitiveType.Cube, accentMat,
                    new Vector3(r * 0.78f, torsoY + r * 0.35f, -r * 0.05f),
                    Quaternion.Euler(10f, 0f, -35f),
                    new Vector3(r * 0.85f, r * 0.08f, r * 0.55f));
            }

            // Role / Ally markers
            if (request.Role == SpawnRole.Boss || request.Role == SpawnRole.Elite)
            {
                Color crownColor = request.Role == SpawnRole.Boss
                    ? new Color(1f, 0.25f, 0.2f)
                    : new Color(1f, 0.82f, 0.2f);
                Material crownMat = CreateMaterial(crownColor, 0.02f, materials);
                AddPart(root.transform, "RoleCrown", PrimitiveType.Cube, crownMat,
                    new Vector3(0f, headY + headScale * 0.75f, headZ),
                    Quaternion.Euler(0f, 45f, 0f),
                    Vector3.one * (headScale * 0.35f));
            }

            if (request.Ally)
            {
                Material collarMat = CreateMaterial(new Color(0.25f, 0.95f, 0.85f), 0.01f, materials);
                AddPart(root.transform, "AllyCollar", PrimitiveType.Cylinder, collarMat,
                    new Vector3(0f, torsoY + r * 0.3f, r * 0.45f),
                    Quaternion.Euler(45f, 0f, 0f),
                    new Vector3(r * 0.95f, r * 0.08f, r * 0.95f));
            }

            return new ProceduralView(root, materials, _toonShader != null);
        }

        private Material CreateMaterial(Color color, float outlineWidth, List<Material> owned)
        {
            Material mat;
            if (_toonShader != null)
            {
                mat = PlanetLook.CreatureMaterial(_toonShader, color, outlineWidth);
            }
            else
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(lit) { color = color };
            }

            owned.Add(mat);
            return mat;
        }

        private static void AddPart(Transform parent, string name, PrimitiveType primitive, Material material,
            Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            Collider col = part.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col);
            }

            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localRotation = localRot;
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        private sealed class ProceduralView : ICreatureView
        {
            private GameObject _root;
            private readonly List<Material> _materials;
            private readonly bool _toon;
            private bool _fainted;

            public ProceduralView(GameObject root, List<Material> materials, bool toon)
            {
                _root = root;
                _materials = materials;
                _toon = toon;
            }

            public Transform Root
            {
                get
                {
                    Contract.Requires(_root != null, "creature view has been disposed");
                    return _root.transform;
                }
            }

            public void ShowFainted()
            {
                Contract.Requires(_root != null, "creature view has been disposed");
                if (_fainted)
                {
                    return;
                }

                _fainted = true;
                for (int i = 0; i < _materials.Count; i++)
                {
                    Material m = _materials[i];
                    if (m == null)
                    {
                        continue;
                    }

                    if (_toon)
                    {
                        m.SetColor("_BaseColor", Color.grey);
                    }
                    else
                    {
                        m.color = Color.grey;
                    }
                }
            }

            public void Dispose()
            {
                if (_root == null)
                {
                    return;
                }

                Object.Destroy(_root);
                _root = null;
                for (int i = 0; i < _materials.Count; i++)
                {
                    if (_materials[i] != null)
                    {
                        Object.Destroy(_materials[i]);
                    }
                }

                _materials.Clear();
            }
        }
    }
}
