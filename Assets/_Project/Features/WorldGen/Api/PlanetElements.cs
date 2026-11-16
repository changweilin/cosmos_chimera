using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>One element's share in a planet's composition.</summary>
    /// <remarks>Pre: Element defined; 1 &lt;= Percent &lt;= 100. Post: immutable value. Side effects: none.</remarks>
    public readonly struct PlanetElementShare
    {
        /// <summary>Element.</summary>
        /// <remarks>Pre: none. Post: defined <see cref="GameData.Element"/>. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>Share percentage in 1..100.</summary>
        /// <remarks>Pre: none. Post: 1 &lt;= Percent &lt;= 100. Side effects: none.</remarks>
        public int Percent { get; }

        /// <summary>True for a main element, false for a sub-element.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public bool IsMain { get; }

        /// <summary>Constructs one element share.</summary>
        /// <remarks>Pre: element defined; 1 &lt;= percent &lt;= 100. Post: fields set. Side effects: none.</remarks>
        public PlanetElementShare(Element element, int percent, bool isMain)
        {
            Contract.Requires(Enum.IsDefined(typeof(Element), element), "element must be defined");
            Contract.Requires(percent >= 1 && percent <= 100, "percent must be in [1, 100]");
            Element = element;
            Percent = percent;
            IsMain = isMain;
        }
    }

    /// <summary>
    /// Main and sub-elements of one planet with integer percentages summing to 100.
    /// REQ: docs/design/planet-world.md#星球屬性.
    /// </summary>
    /// <remarks>
    /// Pre: constructed via <see cref="PlanetElementDraw.Draw"/>. Post: immutable; 1..2 main elements, 0..2 sub-elements,
    /// all unique; sub-element total in 0..35; sum of all Percents == 100. Side effects: none.
    /// </remarks>
    public sealed class PlanetElements
    {
        private readonly PlanetElementShare[] _shares;
        private readonly Element[] _elements;
        private readonly int[] _percents;
        private readonly int[] _byElement;

        internal PlanetElements(PlanetElementShare[] shares, int mainCount, int subCount, int mainTotal, int subTotal)
        {
            _shares = shares;
            MainCount = mainCount;
            SubCount = subCount;
            MainPercentTotal = mainTotal;
            SubPercentTotal = subTotal;

            _elements = new Element[shares.Length];
            _percents = new int[shares.Length];
            _byElement = new int[10];
            for (int i = 0; i < shares.Length; i++)
            {
                _elements[i] = shares[i].Element;
                _percents[i] = shares[i].Percent;
                _byElement[(int)shares[i].Element] = shares[i].Percent;
            }
        }

        /// <summary>Number of main elements (1 on Asteroid/Moon/Planet, 1..2 on Supergiant).</summary>
        /// <remarks>Pre: none. Post: 1 &lt;= result &lt;= 2. Side effects: none.</remarks>
        public int MainCount { get; }

        /// <summary>Number of sub-elements (0 on Asteroid, 0..1 on Moon, 0..2 on Planet/Supergiant).</summary>
        /// <remarks>Pre: none. Post: 0 &lt;= result &lt;= 2. Side effects: none.</remarks>
        public int SubCount { get; }

        /// <summary>Total percentage of main elements (65..100).</summary>
        /// <remarks>Pre: none. Post: MainPercentTotal + SubPercentTotal == 100. Side effects: none.</remarks>
        public int MainPercentTotal { get; }

        /// <summary>Total percentage of sub-elements (0..35).</summary>
        /// <remarks>Pre: none. Post: 0 &lt;= result &lt;= 35; 0 iff SubCount == 0. Side effects: none.</remarks>
        public int SubPercentTotal { get; }

        /// <summary>All active shares: main elements first (descending by Percent), then sub-elements (descending).</summary>
        /// <remarks>Pre: none. Post: Count == MainCount + SubCount; unique Elements; sum == 100. Side effects: none.</remarks>
        public IReadOnlyList<PlanetElementShare> Shares => _shares;

        /// <summary>Parallel array of active elements in <see cref="Shares"/> order.</summary>
        /// <remarks>Pre: none. Post: Count == Shares.Count. Side effects: none.</remarks>
        public IReadOnlyList<Element> Elements => _elements;

        /// <summary>Parallel array of percentages in <see cref="Shares"/> order.</summary>
        /// <remarks>Pre: none. Post: Count == Shares.Count; sum == 100. Side effects: none.</remarks>
        public IReadOnlyList<int> Percents => _percents;

        /// <summary>Percentage of <paramref name="element"/> on this planet (0 if absent).</summary>
        /// <remarks>Pre: element defined. Post: 0 &lt;= result &lt;= 100. Side effects: none.</remarks>
        public int PercentOf(Element element)
        {
            Contract.Requires(Enum.IsDefined(typeof(Element), element), "element must be defined");
            return _byElement[(int)element];
        }
    }
}
