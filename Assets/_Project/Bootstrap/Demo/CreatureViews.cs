using System;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>What a view factory may use to choose and size the scene representation of one creature unit.</summary>
    /// <remarks>
    /// Pre: Def != null; BodyRadiusM > 0. Post: none. Side effects: none.
    /// Def identifies species and stage, so evolving in the lobby changes the view on the next run without extra state
    /// (REQ: art-asset-pipeline.md 宙獸定稿 path is keyed by line and stage).
    /// </remarks>
    internal readonly struct CreatureViewRequest
    {
        public readonly CreatureDef Def;
        public readonly bool Ally;
        public readonly SpawnRole Role;
        public readonly float BodyRadiusM;

        public CreatureViewRequest(CreatureDef def, bool ally, SpawnRole role, float bodyRadiusM)
        {
            Def = def;
            Ally = ally;
            Role = role;
            BodyRadiusM = bodyRadiusM;
        }
    }

    /// <summary>Scene representation of one encounter unit; the run only positions, shows, greys out and disposes it.</summary>
    /// <remarks>Pre: main thread. Side effects: per member.</remarks>
    internal interface ICreatureView
    {
        /// <summary>Root transform the run places at the unit's position and facing.</summary>
        /// <remarks>Pre: not disposed. Post: non-null. Side effects: none.</remarks>
        Transform Root { get; }

        /// <summary>Marks the unit as fainted. Idempotent.</summary>
        /// <remarks>Pre: not disposed. Post: view reads as fainted. Side effects: mutates owned materials only.</remarks>
        void ShowFainted();

        /// <summary>Destroys the root and everything the view created. Idempotent.</summary>
        /// <remarks>Pre: main thread. Post: no scene objects or materials of this view remain. Side effects: Destroy calls.</remarks>
        void Dispose();
    }

    /// <summary>
    /// Builds creature views. The model-replacement seam required by art-asset-pipeline.md#佔位模型: real models land by
    /// adding a factory, not by editing <see cref="RunSession"/>.
    /// </summary>
    /// <remarks>Pre: main thread. Side effects: per member.</remarks>
    internal interface ICreatureViewFactory
    {
        /// <summary>Creates a view for <paramref name="request"/>; the caller owns it, sets its active state and disposes it.</summary>
        /// <remarks>
        /// Pre: request satisfies <see cref="CreatureViewRequest"/> preconditions.
        /// Post: result != null; result.Root has no Collider (views must not take part in physics; combat geometry is rule-side).
        /// Side effects: instantiates scene objects and materials.
        /// </remarks>
        ICreatureView Create(in CreatureViewRequest request);
    }

    /// <summary>D0 placeholder: toon-shaded primitive in the main element's colour, sized by body class and spawn role.</summary>
    /// <remarks>
    /// Pre: none at construction; toonShader may be null (falls back to the primitive's default material, as before the seam).
    /// Side effects: per member.
    /// </remarks>
    internal sealed class PlaceholderCreatureViewFactory : ICreatureViewFactory
    {
        // Indexed by (int)Element.
        private static readonly Color[] ElementColors =
        {
            new Color(0.75f, 0.75f, 0.8f), new Color(0.3f, 0.7f, 0.25f), new Color(0.2f, 0.5f, 0.95f),
            new Color(0.95f, 0.35f, 0.15f), new Color(0.6f, 0.45f, 0.25f), new Color(0.6f, 0.9f, 1f),
            new Color(0.6f, 0.95f, 0.7f), new Color(0.95f, 0.85f, 0.2f), new Color(1f, 0.97f, 0.75f),
            new Color(0.35f, 0.25f, 0.5f),
        };

        private readonly Shader _toonShader;

        /// <summary>Creates a factory that shades placeholders with <paramref name="toonShader"/> when given.</summary>
        /// <remarks>Pre: none. Post: ready to create views. Side effects: none.</remarks>
        public PlaceholderCreatureViewFactory(Shader toonShader)
        {
            _toonShader = toonShader;
        }

        /// <summary>Diameter in metres of the placeholder for <paramref name="request"/>.</summary>
        /// <remarks>Pre: BodyRadiusM > 0. Post: result > 0. Side effects: none (pure).</remarks>
        public static float Scale(in CreatureViewRequest request)
        {
            Contract.Requires(request.BodyRadiusM > 0f, "body radius must be positive");
            float roleScale = request.Role == SpawnRole.Boss ? 2.5f : request.Role == SpawnRole.Elite ? 1.4f : 1f;
            return request.BodyRadiusM * 2f * roleScale;
        }

        /// <summary>Base colour of the placeholder for <paramref name="def"/> (its main element).</summary>
        /// <remarks>Pre: def != null. Post: one of the ten element colours. Side effects: none (pure).</remarks>
        public static Color ColorOf(CreatureDef def)
        {
            Contract.Requires(def != null, "def required");
            return ElementColors[(int)RunPlanner.MainElement(def)];
        }

        /// <inheritdoc/>
        public ICreatureView Create(in CreatureViewRequest request)
        {
            Contract.Requires(request.Def != null, "def required");
            GameObject go = GameObject.CreatePrimitive(request.Ally ? PrimitiveType.Capsule : PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.name = (request.Ally ? "Ally " : "Wild ") + request.Def.Id;
            float scale = Scale(request);
            go.transform.localScale = new Vector3(scale, scale, scale);
            var renderer = go.GetComponent<Renderer>();
            Color color = ColorOf(request.Def);
            Material material;
            if (_toonShader != null)
            {
                // Outline width is in world metres (the hull extrudes along the normalized world normal).
                material = PlanetLook.CreatureMaterial(_toonShader, color, 0.03f);
                renderer.sharedMaterial = material;
            }
            else
            {
                material = renderer.material;
                material.color = color;
            }

            return new PlaceholderView(go, renderer, material, _toonShader != null);
        }

        private sealed class PlaceholderView : ICreatureView
        {
            private GameObject _root;
            private readonly Material _material;
            private readonly bool _toon;

            public PlaceholderView(GameObject root, Renderer renderer, Material material, bool toon)
            {
                _root = root;
                _material = material;
                _toon = toon;
            }

            public Transform Root
            {
                get
                {
                    if (_root == null)
                    {
                        throw new ObjectDisposedException(nameof(PlaceholderView));
                    }

                    return _root.transform;
                }
            }

            public void ShowFainted()
            {
                if (_toon)
                {
                    _material.SetColor("_BaseColor", Color.gray);
                }
                else
                {
                    _material.color = Color.gray;
                }
            }

            public void Dispose()
            {
                if (_root != null)
                {
                    UnityEngine.Object.Destroy(_root);
                    _root = null;
                }

                // The material is per-view (never shared), so the view owns it; destroying the GameObject does not.
                if (_material != null)
                {
                    UnityEngine.Object.Destroy(_material);
                }
            }
        }
    }
}
