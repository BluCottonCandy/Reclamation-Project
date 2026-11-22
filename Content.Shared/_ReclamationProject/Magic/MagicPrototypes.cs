using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Magic;

// #Reclamation Project - Arcane magic (grimoire spell system).
// A spell is made of 1-4 secret base words. The real spoken/written form of every base word is
// random every round (see MagicWordsSystem on the server), so these prototypes only hold the
// meaning, never the text a player has to write.

/// <summary>The spell classes. Each class has its own glow colour.</summary>
[Serializable, NetSerializable]
public enum MagicClass : byte
{
    Elemental,
    Atomic,
    Psychic,
    Dark,
    Blood,
    Healing,
    Conjuration,
}

/// <summary>Beginner = 1 word, Intermediate = 2, Saint = 3, King = 4.</summary>
[Serializable, NetSerializable]
public enum MagicRank : byte
{
    Beginner = 1,
    Intermediate = 2,
    Saint = 3,
    King = 4,
}

/// <summary>One base word (Fire, Blink, Healing...). Its written form changes every round.</summary>
[Prototype("magicWord")]
public sealed partial class MagicWordPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Name shown only to admins (magic_words command / admin verb).</summary>
    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public MagicClass Class = MagicClass.Psychic;

    /// <summary>One-word hint shown in the Codex until the reader has written this word correctly.</summary>
    [DataField]
    public LocId? Hint;
}

/// <summary>A castable spell: which base words make it and what it costs.</summary>
[Prototype("magicSpell")]
public sealed partial class MagicSpellPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Admin-facing name. Players are never told the name of a spell.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The base words, 1 to 4. Any order casts it; one random order per round is "true" (Empowered).</summary>
    [DataField(required: true)]
    public List<ProtoId<MagicWordPrototype>> Words = new();

    [DataField]
    public MagicClass Class = MagicClass.Elemental;

    [DataField]
    public float ManaCost = 10f;

    /// <summary>Action entity spawned for a readied page. It holds the actual spell event.</summary>
    [DataField(required: true)]
    public EntProtoId Action;

    /// <summary>Dark spells: how much Corruption one cast adds (0-100 bar).</summary>
    [DataField]
    public float CorruptionCost;

    /// <summary>Blood spells: units of blood paid instead of mana (from a held blood pack first, then your veins).</summary>
    [DataField]
    public float BloodCost;

    /// <summary>Seconds of reading before the spell fires. Null = by rank (1 / 2 / 4 / 8 s).</summary>
    [DataField]
    public float? ReadTime;

    /// <summary>Can only be cast with at least this much max mana (Transmutation: 180).</summary>
    [DataField]
    public float RequiredMaxMana;

    /// <summary>Empties the caster's mana when cast (DOOM, Transmutation).</summary>
    [DataField]
    public bool UsesAllMana;

    /// <summary>Leaves the floor it hits burning (fire spells without a Fire or Ember word, like Pyrokinesis).</summary>
    [DataField]
    public bool BurnsFloor;

    /// <summary>One line for admins (magic_words, the spell list). Players never see it.</summary>
    [DataField]
    public string? Summary;

    public MagicRank Rank => (MagicRank) Math.Clamp(Words.Count, 1, 4);

    public float ReadSeconds => ReadTime ?? Rank switch
    {
        MagicRank.Beginner => 1f,
        MagicRank.Intermediate => 2f,
        MagicRank.Saint => 4f,
        _ => 8f,
    };
}
