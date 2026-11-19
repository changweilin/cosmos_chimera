using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>How an advanced terrain's dungeon is built (planet-world.md#進階地形地城).</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum AdvancedDungeonForm
    {
        /// <summary>Enclosed maze on the planet surface (DungeonPlanner + PlanetWalls).</summary>
        SurfaceMaze = 0,
        /// <summary>Separate indoor / underground dungeon stitched from room modules (RoomDungeonPlanner).</summary>
        Rooms = 1,
    }

    /// <summary>One of the ten element advanced terrains.</summary>
    /// <remarks>Pre: obtained from <see cref="AdvancedTerrains"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct AdvancedTerrainSpec
    {
        internal AdvancedTerrainSpec(string id, Element element, PlanetSize minSize, AdvancedDungeonForm form,
            FieldUtility immunity, int trialTeamSize, string[] aliases)
        {
            Id = id;
            Element = element;
            MinSize = minSize;
            Form = form;
            Immunity = immunity;
            TrialTeamSize = trialTeamSize;
            Aliases = aliases;
        }

        /// <summary>Canonical terrain id (snake_case), e.g. "laboratory"; trial badges are keyed by it.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Element whose immunity opens this terrain and whose specialists hold the trial.</summary>
        /// <remarks>Pre: none. Post: defined value; unique across the ten terrains. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>Smallest planet size this terrain appears on.</summary>
        /// <remarks>Pre: none. Post: Moon, Planet or Supergiant. Side effects: none.</remarks>
        public PlanetSize MinSize { get; }

        /// <summary>Whether its dungeon is a surface maze or a room dungeon.</summary>
        /// <remarks>Pre: none. Post: defined value. Side effects: none.</remarks>
        public AdvancedDungeonForm Form { get; }

        /// <summary>Field trait that lets a party enter outside a trial.</summary>
        /// <remarks>Pre: none. Post: one of the ten Immune* values. Side effects: none.</remarks>
        public FieldUtility Immunity { get; }

        /// <summary>Number of creatures in the trial master's team (provisional, trainer-battles.md#地形試煉).</summary>
        /// <remarks>Pre: none. Post: 2 (Moon terrains), 3 (Planet) or 4 (Supergiant). Side effects: none.</remarks>
        public int TrialTeamSize { get; }

        /// <summary>Other theme ids that name the same terrain.</summary>
        /// <remarks>Pre: none. Post: non-null; never contains Id. Side effects: none.</remarks>
        public IReadOnlyList<string> Aliases { get; }

        /// <summary>Codex character id of this terrain's trial master.</summary>
        /// <remarks>Pre: none. Post: "trial_master_" + Id. Side effects: none.</remarks>
        public string TrialMasterId => "trial_master_" + Id;
    }

    /// <summary>
    /// The ten element advanced terrains and the per-planet-size rules shared by their dungeons and trials.
    /// REQ: docs/design/planet-world.md#進階地形與通行限制, #進階地形地城; docs/design/trainer-battles.md#地形試煉.
    /// </summary>
    /// <remarks>Pre: none. Post: static, immutable tables. Side effects: none.</remarks>
    public static class AdvancedTerrains
    {
        private static readonly AdvancedTerrainSpec[] Specs =
        {
            new AdvancedTerrainSpec("laboratory", Element.Metal, PlanetSize.Moon, AdvancedDungeonForm.Rooms,
                FieldUtility.ImmuneLaboratory, 2, new[] { "factory", "server_room" }),
            new AdvancedTerrainSpec("rainforest", Element.Wood, PlanetSize.Moon, AdvancedDungeonForm.SurfaceMaze,
                FieldUtility.ImmuneRainforest, 2, new[] { "jungle" }),
            new AdvancedTerrainSpec("deep_sea", Element.Water, PlanetSize.Moon, AdvancedDungeonForm.Rooms,
                FieldUtility.ImmuneDeepSea, 2, new[] { "trench" }),
            new AdvancedTerrainSpec("volcano", Element.Fire, PlanetSize.Moon, AdvancedDungeonForm.SurfaceMaze,
                FieldUtility.ImmuneVolcano, 2, new[] { "caldera" }),
            new AdvancedTerrainSpec("cave", Element.Earth, PlanetSize.Moon, AdvancedDungeonForm.Rooms,
                FieldUtility.ImmuneCave, 2, new[] { "mine", "underground" }),
            new AdvancedTerrainSpec("ice_cap", Element.Ice, PlanetSize.Planet, AdvancedDungeonForm.SurfaceMaze,
                FieldUtility.ImmuneIceCap, 3, new[] { "permafrost" }),
            new AdvancedTerrainSpec("tower", Element.Thunder, PlanetSize.Planet, AdvancedDungeonForm.Rooms,
                FieldUtility.ImmuneTower, 3, new[] { "spire", "skyscraper" }),
            new AdvancedTerrainSpec("space", Element.Wind, PlanetSize.Planet, AdvancedDungeonForm.Rooms,
                FieldUtility.ImmuneSpace, 3, new[] { "floating_island", "space_facility" }),
            new AdvancedTerrainSpec("labyrinth", Element.Shadow, PlanetSize.Supergiant, AdvancedDungeonForm.SurfaceMaze,
                FieldUtility.ImmuneLabyrinth, 4, new[] { "maze" }),
            new AdvancedTerrainSpec("temple", Element.Light, PlanetSize.Supergiant, AdvancedDungeonForm.Rooms,
                FieldUtility.ImmuneTemple, 4, new[] { "shrine" }),
        };

        /// <summary>All ten terrains in table order (metal, wood, water, fire, earth, ice, thunder, wind, shadow, light).</summary>
        /// <remarks>Pre: none. Post: Count == 10; ids and elements unique. Side effects: none.</remarks>
        public static IReadOnlyList<AdvancedTerrainSpec> All => Specs;

        // Table position: a stable per-terrain index for RNG streams. Reordering Specs changes generated output.
        internal static int IndexOf(string canonicalId)
        {
            for (int i = 0; i < Specs.Length; i++)
            {
                if (string.Equals(Specs[i].Id, canonicalId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            throw new ContractException("unknown advanced terrain " + canonicalId);
        }

        /// <summary>Looks up a terrain by canonical id or alias.</summary>
        /// <remarks>Pre: none. Post: true iff idOrAlias names one of the ten; spec is default otherwise. Side effects: none.</remarks>
        public static bool TryGet(string idOrAlias, out AdvancedTerrainSpec spec)
        {
            spec = default;
            if (string.IsNullOrEmpty(idOrAlias))
            {
                return false;
            }

            for (int i = 0; i < Specs.Length; i++)
            {
                if (string.Equals(Specs[i].Id, idOrAlias, StringComparison.Ordinal))
                {
                    spec = Specs[i];
                    return true;
                }

                foreach (string alias in Specs[i].Aliases)
                {
                    if (string.Equals(alias, idOrAlias, StringComparison.Ordinal))
                    {
                        spec = Specs[i];
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The terrain whose element is <paramref name="element"/>.</summary>
        /// <remarks>Pre: element defined. Post: result.Element == element. Side effects: none.</remarks>
        public static AdvancedTerrainSpec ForElement(Element element)
        {
            Contract.Requires(Enum.IsDefined(typeof(Element), element), "element must be defined");
            for (int i = 0; i < Specs.Length; i++)
            {
                if (Specs[i].Element == element)
                {
                    return Specs[i];
                }
            }

            throw new ContractException("no advanced terrain for element " + element);
        }

        /// <summary>Whether <paramref name="spec"/> can appear on a planet of <paramref name="size"/>.</summary>
        /// <remarks>Pre: size defined. Post: size &gt;= spec.MinSize. Side effects: none.</remarks>
        public static bool AppearsOn(in AdvancedTerrainSpec spec, PlanetSize size)
        {
            Contract.Requires(Enum.IsDefined(typeof(PlanetSize), size), "size must be defined");
            return size >= spec.MinSize;
        }

        /// <summary>
        /// Form of a trial boss (and the trial master's creatures) on a planet of <paramref name="size"/>: the planet's
        /// boss tier. REQ: trainer-battles.md#地形試煉 — moon = adolescent, planet = adult, supergiant = adult + overload.
        /// </summary>
        /// <remarks>Pre: size is Moon, Planet or Supergiant (asteroids hold no advanced terrain). Post: Adolescent or Adult. Side effects: none.</remarks>
        public static CreatureStage TrialStageFor(PlanetSize size)
        {
            Contract.Requires(size >= PlanetSize.Moon && size <= PlanetSize.Supergiant,
                "advanced terrains appear on moon, planet and supergiant only");
            return size == PlanetSize.Moon ? CreatureStage.Adolescent : CreatureStage.Adult;
        }

        /// <summary>Whether the trial boss fights overloaded on a planet of <paramref name="size"/>.</summary>
        /// <remarks>Pre: as TrialStageFor. Post: true iff size == Supergiant. Side effects: none.</remarks>
        public static bool TrialBossOverloadsOn(PlanetSize size)
        {
            Contract.Requires(size >= PlanetSize.Moon && size <= PlanetSize.Supergiant,
                "advanced terrains appear on moon, planet and supergiant only");
            return size == PlanetSize.Supergiant;
        }
    }
}
