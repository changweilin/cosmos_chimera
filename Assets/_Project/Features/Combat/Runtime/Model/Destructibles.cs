using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Unity.Mathematics;

namespace Cosmimera.Combat
{
    /// <summary>Reactions a single hit on an object triggered. REQ: docs/design/destructibles.md#物質反應.</summary>
    [Flags]
    public enum ObjectReaction
    {
        None = 0,
        Ignited = 1 << 0,
        /// <summary>Kerosene-style flash: the object destroyed itself on ignition.</summary>
        Blasted = 1 << 1,
        Melted = 1 << 2,
        Froze = 1 << 3,
        Shattered = 1 << 4,
        Conducted = 1 << 5,
        /// <summary>Wind hit: presentation doubles the impulse (<see cref="EnvMaterials.GustImpulsePercent"/>).</summary>
        Gust = 1 << 6,
        Extinguished = 1 << 7,
    }

    /// <summary>Why an object was destroyed; decides the material element conversion.</summary>
    public enum DestroyCause
    {
        /// <summary>Plain damage: element comes from the substance (or a weighted roll for a building).</summary>
        Damage = 0,
        /// <summary>Burnt out or blasted: drops charcoal (fire).</summary>
        Burnt = 1,
        /// <summary>Ice melted by fire: drops water.</summary>
        Melted = 2,
    }

    /// <summary>Outcome of one hit on one object.</summary>
    /// <remarks>Pre: none. Post: immutable value. Side effects: none.</remarks>
    public readonly struct ObjectHitResult
    {
        /// <summary>Creates a result.</summary>
        /// <remarks>Pre: damage &gt;= 0. Post: fields echo the arguments. Side effects: none.</remarks>
        public ObjectHitResult(int damage, ObjectReaction reactions, bool destroyed, DestroyCause cause)
        {
            Damage = damage;
            Reactions = reactions;
            Destroyed = destroyed;
            Cause = cause;
        }

        /// <summary>HP removed by this hit (a blast removes all remaining HP).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Damage { get; }

        /// <summary>Reactions triggered.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public ObjectReaction Reactions { get; }

        /// <summary>Whether this hit destroyed the object.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public bool Destroyed { get; }

        /// <summary>Cause, meaningful only when <see cref="Destroyed"/>.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public DestroyCause Cause { get; }
    }

    /// <summary>What a destroyed object drops. All rolls come from the host's drop stream.</summary>
    /// <remarks>Pre: none. Post: immutable value. Side effects: none.</remarks>
    public readonly struct ObjectDrop
    {
        /// <summary>Creates a drop.</summary>
        /// <remarks>Pre: none. Post: fields echo the arguments. Side effects: none.</remarks>
        public ObjectDrop(Element element, bool material, bool gem, bool shadow)
        {
            Element = element;
            Material = material;
            Gem = gem;
            Shadow = shadow;
        }

        /// <summary>Element of the tier-1 material and the tier-3 gem (after reaction conversion).</summary>
        /// <remarks>Pre: none. Post: never Shadow. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>One tier-1 material of <see cref="Element"/> dropped.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public bool Material { get; }

        /// <summary>One tier-3 gem of <see cref="Element"/> dropped.</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public bool Gem { get; }

        /// <summary>One tier-1 shadow material dropped (night rule).</summary>
        /// <remarks>Pre: none. Post: stable. Side effects: none.</remarks>
        public bool Shadow { get; }
    }

    /// <summary>
    /// Rule-layer table of destructible scene objects: HP, reactions, burning, destruction, the run-wide destroyed
    /// set and drop rolls. Integer-only so the host's results are exact; clients receive events, never re-simulate.
    /// REQ: docs/design/destructibles.md; ADR-0009 (rigidbody motion stays in presentation and never feeds back here).
    /// </summary>
    /// <remarks>
    /// Objects do not roll to hit (destructibles.md#傷害): geometry contact is enough, and they never touch the combat
    /// stream. Ids are generation-derived and stable across LOD rebuilds, so the destroyed set is keyed by id, not slot.
    /// Pre: single-threaded host use. Post: see members. Side effects: mutates only itself.
    /// </remarks>
    public sealed class DestructibleTable
    {
        private struct Slot
        {
            public long Id;
            public float3 Center;
            public Composition Composition;
            public int VolumeL;
            public int MaxHp;
            public int Hp;
            public bool Burning;
            public long NextBurnTickMs;
            public long FrozenUntilMs;
            public bool Destroyed;
            public DestroyCause Cause;
            public bool Registered;
        }

        private readonly EnvMaterials _env;
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<long, int> _byId = new Dictionary<long, int>();
        private readonly HashSet<long> _destroyed = new HashSet<long>();

        /// <summary>Creates an empty table.</summary>
        /// <remarks>Pre: env non-null. Post: Count == 0; no destroyed ids. Side effects: allocates.</remarks>
        public DestructibleTable(EnvMaterials env)
        {
            Contract.Requires(env != null, "env must be non-null");
            _env = env;
        }

        /// <summary>Number of registered slots (live and destroyed).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Count => _slots.Count;

        /// <summary>Registers an object produced by generation, unless its id was destroyed earlier this run.</summary>
        /// <remarks>
        /// Pre: composition non-null; 1 &lt;= volumeL (litres = dm³); center finite (world hit centre); id not currently
        ///      registered; a re-registered id comes from the same generation result (same composition, volume, centre).
        /// Post: returns -1 iff id is in the destroyed set (a LOD rebuild must not regrow it); a previously
        ///       unregistered id gets its old slot back with HP, burning and frozen state intact; a new id gets a slot
        ///       with Hp == MaxHp == max(1, Dominant.HpPerM3 × volumeL / 1000).
        /// Side effects: may append a slot.
        /// </remarks>
        public int Register(long id, Composition composition, int volumeL, float3 center)
        {
            Contract.Requires(composition != null, "composition must be non-null");
            Contract.Requires(math.all(math.isfinite(center)), "center must be finite");
            Contract.Requires(volumeL >= 1, "volumeL must be >= 1");
            if (_destroyed.Contains(id))
            {
                return -1;
            }

            if (_byId.TryGetValue(id, out int old))
            {
                Slot o = _slots[old];
                Contract.Requires(!o.Registered, "id is already registered");
                o.Registered = true;
                _slots[old] = o;
                return old;
            }

            long hp = (long)composition.Dominant.HpPerM3 * volumeL / 1000;
            int maxHp = hp < 1 ? 1 : hp > int.MaxValue ? int.MaxValue : (int)hp;
            _slots.Add(new Slot { Id = id, Center = center, Composition = composition, VolumeL = volumeL, MaxHp = maxHp, Hp = maxHp, Registered = true });
            _byId.Add(id, _slots.Count - 1);
            return _slots.Count - 1;
        }

        /// <summary>Marks an object unloaded (its chunk left LOD) without forgetting its state.</summary>
        /// <remarks>
        /// Pre: id is registered. Post: <see cref="IndexOf"/> returns -1 until it is registered again; slot indices
        ///      never move; a burning slot keeps burning (host state does not depend on what is loaded).
        /// Side effects: mutates the table.
        /// </remarks>
        public void Unregister(long id)
        {
            int i = _byId.TryGetValue(id, out int found) ? found : -1;
            Contract.Requires(i >= 0 && _slots[i].Registered, "id is not registered");
            Slot s = _slots[i];
            s.Registered = false;
            _slots[i] = s;
        }

        /// <summary>Slot index of a registered <paramref name="id"/>, or -1.</summary>
        /// <remarks>Pre: none. Post: -1 iff not currently registered. Side effects: none.</remarks>
        public int IndexOf(long id) => _byId.TryGetValue(id, out int i) && _slots[i].Registered ? i : -1;

        /// <summary>Whether <paramref name="id"/> was destroyed this run.</summary>
        /// <remarks>Pre: none. Post: stable once true. Side effects: none.</remarks>
        public bool IsDestroyed(long id) => _destroyed.Contains(id);

        /// <summary>Id of slot <paramref name="index"/>.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Count. Post: the registered id. Side effects: none.</remarks>
        public long IdOf(int index) => Get(index).Id;

        /// <summary>World hit centre of slot <paramref name="index"/>.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Count. Post: as registered. Side effects: none.</remarks>
        public float3 CenterOf(int index) => Get(index).Center;

        /// <summary>Why slot <paramref name="index"/> was destroyed.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Count. Post: meaningful only once destroyed. Side effects: none.</remarks>
        public DestroyCause CauseOf(int index) => Get(index).Cause;

        /// <summary>Current HP of slot <paramref name="index"/>.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Count. Post: 0..MaxHp; 0 iff destroyed. Side effects: none.</remarks>
        public int Hp(int index) => Get(index).Hp;

        /// <summary>Max HP of slot <paramref name="index"/>.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Count. Post: &gt;= 1. Side effects: none.</remarks>
        public int MaxHp(int index) => Get(index).MaxHp;

        /// <summary>Whether slot <paramref name="index"/> is burning.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Count. Post: false once destroyed. Side effects: none.</remarks>
        public bool IsBurning(int index) => Get(index).Burning;

        /// <summary>Whether slot <paramref name="index"/> is frozen at <paramref name="nowMs"/>.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Count. Post: true iff an ice hit's window covers nowMs. Side effects: none.</remarks>
        public bool IsFrozen(int index, long nowMs) => Get(index).FrozenUntilMs > nowMs;

        /// <summary>Mass for the presentation rigidbody: dominant density × volume.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Count. Post: &gt;= 1. Side effects: none.</remarks>
        public long MassKg(int index)
        {
            Slot s = Get(index);
            long m = (long)s.Composition.Dominant.DensityKgM3 * s.VolumeL / 1000;
            return m < 1 ? 1 : m;
        }

        /// <summary>Applies one move hit (geometry already touched; objects never roll).</summary>
        /// <remarks>
        /// Pre: 0 &lt;= index &lt; Count; the slot is not destroyed; 0 &lt;= power &lt;= DamageModel.MaxPower;
        ///      0 &lt;= masteryPercent &lt;= DamageModel.MaxMasteryPercent; nowMs &gt;= 0.
        /// Post: damage = DamageModel(power, mastery, resist) × reaction percent (one rounding, min 1 unless immune);
        ///       brittleness is judged before this hit's own freeze; Destroyed iff Hp reached 0.
        /// Side effects: mutates the slot (HP, burning, frozen window); on destruction adds the id to the destroyed set.
        /// </remarks>
        public ObjectHitResult ApplyHit(int index, Element element, int power, int masteryPercent, long nowMs)
        {
            Contract.Requires(index >= 0 && index < _slots.Count, "index out of range");
            Contract.Requires(!_slots[index].Destroyed, "object already destroyed");
            Contract.Requires(nowMs >= 0, "nowMs must be >= 0");
            Slot s = _slots[index];
            Substance sub = s.Composition.Dominant;
            bool isIce = ReferenceEquals(sub, _env.Ice);
            bool brittle = sub.Brittle == Brittleness.Always || s.FrozenUntilMs > nowMs;
            var reactions = ObjectReaction.None;
            int percent = 100;

            // REQ: destructibles.md#物質反應 — one rule per (element, condition) row; first version touches only this object.
            switch (element)
            {
                case Element.Fire:
                    if (isIce)
                    {
                        percent = _env.MeltPercent;
                        reactions |= ObjectReaction.Melted;
                    }
                    else if (sub.Flammable != Flammability.None && !s.Burning)
                    {
                        reactions |= ObjectReaction.Ignited;
                    }

                    break;
                case Element.Ice:
                case Element.Water:
                    if (s.Burning)
                    {
                        s.Burning = false;
                        reactions |= ObjectReaction.Extinguished;
                    }

                    if (element == Element.Ice && sub.Element != Element.Ice)
                    {
                        s.FrozenUntilMs = nowMs + _env.FreezeMs;
                        reactions |= ObjectReaction.Froze;
                    }

                    break;
                case Element.Earth:
                case Element.Metal:
                    if (brittle)
                    {
                        percent = _env.ShatterPercent;
                        reactions |= ObjectReaction.Shattered;
                    }

                    break;
                case Element.Thunder:
                    if (sub.Conductive)
                    {
                        percent = _env.ConductPercent;
                        reactions |= ObjectReaction.Conducted;
                    }

                    break;
                case Element.Wind:
                    reactions |= ObjectReaction.Gust;
                    break;
            }

            int baseDamage = DamageModel.Compute(power, masteryPercent, sub.Resist(element));
            long scaled = ((long)baseDamage * percent + 50) / 100;
            int damage = scaled > int.MaxValue ? int.MaxValue : (int)scaled;
            // REQ: destructibles.md#掉落機率 — a flammable finished by fire counts as burnt (charcoal), not only a burn-out.
            DestroyCause cause = (reactions & ObjectReaction.Melted) != 0 ? DestroyCause.Melted
                : element == Element.Fire && sub.Flammable != Flammability.None ? DestroyCause.Burnt
                : DestroyCause.Damage;

            if ((reactions & ObjectReaction.Ignited) != 0)
            {
                if (sub.Flammable == Flammability.Blast)
                {
                    // Kerosene flashes: the ignition itself destroys it, regardless of remaining HP.
                    reactions |= ObjectReaction.Blasted;
                    damage = s.Hp;
                    cause = DestroyCause.Burnt;
                }
                else
                {
                    s.Burning = true;
                    s.NextBurnTickMs = nowMs + 1000;
                }
            }

            if (damage > s.Hp)
            {
                damage = s.Hp;
            }

            s.Hp -= damage;
            bool destroyed = s.Hp == 0;
            if (destroyed)
            {
                Destroy(ref s, cause);
            }

            _slots[index] = s;
            return new ObjectHitResult(damage, reactions, destroyed, cause);
        }

        /// <summary>Advances burning to <paramref name="nowMs"/> in whole-second ticks.</summary>
        /// <remarks>
        /// Pre: destroyedOut non-null; nowMs is non-decreasing across calls.
        /// Post: each burning slot lost max(1, MaxHp × rate / 1000) per elapsed tick; slots that reached 0 are
        ///       destroyed with <see cref="DestroyCause.Burnt"/> and their indices appended to destroyedOut.
        /// Side effects: mutates slots and the destroyed set; appends to destroyedOut. No allocation beyond list growth.
        /// </remarks>
        public void Tick(long nowMs, List<int> destroyedOut)
        {
            Contract.Requires(destroyedOut != null, "destroyedOut must be non-null");
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                if (!s.Burning)
                {
                    continue;
                }

                int rate = s.Composition.Dominant.Flammable == Flammability.Fast
                    ? _env.BurnFastPermillePerS
                    : _env.BurnSlowPermillePerS;
                long perTick = (long)s.MaxHp * rate / 1000;
                int loss = perTick < 1 ? 1 : (int)perTick;
                while (s.NextBurnTickMs <= nowMs && s.Hp > 0)
                {
                    s.Hp = s.Hp > loss ? s.Hp - loss : 0;
                    s.NextBurnTickMs += 1000;
                }

                if (s.Hp == 0)
                {
                    Destroy(ref s, DestroyCause.Burnt);
                    destroyedOut.Add(i);
                }

                _slots[i] = s;
            }
        }

        /// <summary>Rolls what destroyed slot <paramref name="index"/> drops.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= index &lt; Count; the slot is destroyed; rng is the host's drop stream (separate from
        ///      generation and combat); call at most once per destruction.
        /// Post: Element = Fire if Burnt, Water if Melted, else the single substance's element or a weight-proportional
        ///       pick among a composite's parts; Material/Gem follow <see cref="EnvMaterials.Material"/>/<see cref="EnvMaterials.Gem"/>
        ///       at the object's volume; Shadow is false unless night, else
        ///       chance = min(cap, ShadowPerM2 × area_m² × opacity / 1000) with area = volume^(2/3).
        /// Side effects: advances rng by a fixed draw order: [composite pick], material, gem, [shadow if night].
        /// </remarks>
        public ObjectDrop RollDrop(int index, bool night, ref Pcg32 rng)
        {
            Contract.Requires(index >= 0 && index < _slots.Count, "index out of range");
            Slot s = _slots[index];
            Contract.Requires(s.Destroyed, "object is not destroyed");
            Composition c = s.Composition;

            Element element;
            if (s.Cause == DestroyCause.Burnt)
            {
                element = Element.Fire;
            }
            else if (s.Cause == DestroyCause.Melted)
            {
                element = Element.Water;
            }
            else if (c.Count == 1)
            {
                element = c.Part(0).Element;
            }
            else
            {
                int pick = (int)rng.NextUInt((uint)c.TotalWeight);
                int part = 0;
                while (pick >= c.Weight(part))
                {
                    pick -= c.Weight(part);
                    part++;
                }

                element = c.Part(part).Element;
            }

            bool material = Roll(ref rng, _env.Material.ChancePermille(s.VolumeL));
            bool gem = Roll(ref rng, _env.Gem.ChancePermille(s.VolumeL));
            bool shadow = night && Roll(ref rng, ShadowChancePermille(s.VolumeL, c.OpacityPermille));
            return new ObjectDrop(element, material, gem, shadow);
        }

        /// <summary>Night shadow chance for an object of the given volume and opacity.</summary>
        /// <remarks>
        /// Pre: volumeL &gt;= 0; 0 &lt;= opacityPermille &lt;= 1000.
        /// Post: 0..ShadowCapPermille; non-decreasing in volume and opacity. Side effects: none.
        /// </remarks>
        public int ShadowChancePermille(int volumeL, int opacityPermille)
        {
            Contract.Requires(volumeL >= 0, "volumeL must be >= 0");
            Contract.Requires(opacityPermille >= 0 && opacityPermille <= 1000, "opacityPermille must be in 0..1000");
            // REQ: destructibles.md#影素材 — shadow size = volume^(2/3). Litres are dm³, so the integer cube root is in dm
            // and its square in dm² (÷100 → m²); integer math keeps host results exact.
            long side = IntCbrt(volumeL);
            long chance = _env.ShadowPerM2Permille * side * side * opacityPermille / 100_000;
            return chance > _env.ShadowCapPermille ? _env.ShadowCapPermille : (int)chance;
        }

        private static bool Roll(ref Pcg32 rng, int chancePermille) => rng.NextUInt(1000) < (uint)chancePermille;

        private static long IntCbrt(int v)
        {
            long r = (long)Math.Round(Math.Pow(v, 1.0 / 3.0));
            // Pow may land one off for perfect cubes; correct so the result is the exact floor cube root.
            while (r * r * r > v)
            {
                r--;
            }

            while ((r + 1) * (r + 1) * (r + 1) <= v)
            {
                r++;
            }

            return r;
        }

        private void Destroy(ref Slot s, DestroyCause cause)
        {
            s.Hp = 0;
            s.Burning = false;
            s.Destroyed = true;
            s.Cause = cause;
            _destroyed.Add(s.Id);
        }

        private Slot Get(int index)
        {
            Contract.Requires(index >= 0 && index < _slots.Count, "index out of range");
            return _slots[index];
        }
    }
}
