using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Magic;

// #Reclamation Project - Arcane magic components.
// Every admin-facing name contains "Magic" so typing "magic" in View Variables finds all of it.

/// <summary>
/// Added to anyone who knows the Arcane language. Holds their mana and the spells they know without a book.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MagicCasterComponent : Component
{
    /// <summary>
    /// Only the mage's own client gets this. Sent to everyone, a cheat client could read who is secretly a mage
    /// (Wild Wasteland is hidden from players) and every spell they know.
    /// </summary>
    public override bool SendOnlyToOwner => true;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float MagicMana = 100f;

    /// <summary>Starts at 100 and grows the more you cast (+1 per <see cref="ManaPerGrowth"/> spent), up to <see cref="MagicMaxManaCap"/>.</summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float MagicMaxMana = 100f;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float MagicMaxManaCap = 200f;

    /// <summary>
    /// Instant casting (chantless): spells fire the moment you aim, no charging. Comes from reaching the max mana
    /// cap, or from studying the Treatise on Silent Casting and then casting <see cref="ChantlessPracticeNeeded"/>
    /// spells. Admins can toggle it here.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public bool MagicChantless;

    /// <summary>
    /// Psychic and Dark are open to every mage. The other schools (Elemental, Atomic, Blood, Healing, Conjuration)
    /// stay sealed until an admin unseals them: the Codex of the Unsealed Schools, the admin button or command.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool MagicAllSchoolsUnlocked;

    /// <summary>Has read the Treatise on Silent Casting (now practising toward instant casting).</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool MagicChantlessStudied;

    /// <summary>Spells cast since studying the treatise.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MagicChantlessPractice;

    public const int ChantlessPracticeNeeded = 100;

    /// <summary>A charged targeted spell held in the hand, waiting for a target click.</summary>
    [AutoNetworkedField, ViewVariables]
    public EntityUid? HeldSpell;

    [ViewVariables]
    public TimeSpan HeldUntil;

    /// <summary>Server only: the glow shown on the caster while a spell is held.</summary>
    [ViewVariables]
    public EntityUid? HeldGlow;

    /// <summary>The spell being charged right now (the client shows its reach while it charges).</summary>
    [AutoNetworkedField, ViewVariables]
    public EntityUid? ChargingAction;

    [DataField]
    public float ManaPerGrowth = 25f;

    /// <summary>Mana spent that hasn't turned into max mana yet.</summary>
    [DataField]
    public float SpentTowardGrowth;

    /// <summary>Mana refilled per second while standing (~1 every 2 seconds).</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MagicRegenPerSecond = 0.5f;

    [DataField]
    public float RestingMultiplier = 3f;

    [DataField]
    public float SleepingMultiplier = 5f;

    /// <summary>Brain damage per point of mana you didn't have when overcasting.</summary>
    [DataField]
    public float OvercastDamagePerMana = 0.5f;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public bool MagicInfiniteMana;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public bool MagicIgnoreActiveLimit;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool MagicNoMisfire;

    /// <summary>Spells held in the mind (no book needed). Each one uses one of the 8 active slots.</summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public HashSet<ProtoId<MagicSpellPrototype>> MagicKnownSpells = new();

    /// <summary>Action entities granted for <see cref="MagicKnownSpells"/> (server only).</summary>
    [ViewVariables]
    public Dictionary<ProtoId<MagicSpellPrototype>, EntityUid> KnownSpellActions = new();

    /// <summary>VV: set to true to instantly know every spell. Flips back to false by itself.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool MagicGrantAllSpells
    {
        get => false;
        set
        {
            if (value)
                IoCManager.Resolve<IEntityManager>().EventBus.RaiseLocalEvent(Owner, new MagicAdminGrantAllEvent());
        }
    }

    /// <summary>VV: set to true to forget every spell. Flips back to false by itself.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool MagicRemoveAllSpells
    {
        get => false;
        set
        {
            if (value)
                IoCManager.Resolve<IEntityManager>().EventBus.RaiseLocalEvent(Owner, new MagicAdminRemoveAllEvent());
        }
    }

    /// <summary>Base words this person has written correctly. The Codex names and spells these out for them.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public HashSet<ProtoId<MagicWordPrototype>> MagicSolvedWords = new();

    /// <summary>How many times each spell was cast this round (from a page, scroll, torn page or etching).</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<ProtoId<MagicSpellPrototype>, int> MagicCastCounts = new();

    /// <summary>Spells known by heart: they live in the mind, use a slot, and cost 10% less mana.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public HashSet<ProtoId<MagicSpellPrototype>> MagicMemorisedSpells = new();

    /// <summary>Casts of one spell needed before it can be memorised.</summary>
    public const int MemoriseCasts = 10;

    /// <summary>Spells cast at least once this round. Only these can be cast by typing their words.</summary>
    [ViewVariables]
    public HashSet<ProtoId<MagicSpellPrototype>> CastThisRound = new();

    /// <summary>
    /// Arcane Strain, 0-100. Every spell builds it (bigger spells more, Dark spells extra).
    /// 25%: eyes glow. 50%: whispers, and healing on you is weaker. 75%: nosebleeds.
    /// 100%: any spell can backfire. Slowly fades over time.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float MagicStrain;

    [DataField]
    public float StrainDecayPerSecond = 0.1f;

    [ViewVariables]
    public TimeSpan NextWhisper;

    [ViewVariables]
    public TimeSpan NextNosebleed;

    /// <summary>Server only: the action whose reading just finished, allowed to fire once without reading again.</summary>
    [ViewVariables]
    public EntityUid? ReadyToFire;

    /// <summary>Server only: the reading in progress (so Dispel can break it).</summary>
    [ViewVariables]
    public Content.Shared.DoAfter.DoAfterId? Reading;

    /// <summary>Server only: the spell action being read right now.</summary>
    [ViewVariables]
    public EntityUid? ReadingAction;

    /// <summary>The owner's own name for each spell in their mind (from the page's margin note).</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<ProtoId<MagicSpellPrototype>, string> MagicKnownSpellNotes = new();

    /// <summary>Typed casting costs this much more mana.</summary>
    [DataField]
    public float TypedManaMultiplier = 1.25f;

    /// <summary>The max number of active spells (readied pages + known spells).</summary>
    public const int ActiveLimit = 8;

    [ViewVariables]
    public float NextRegenUpdate;

    /// <summary>Server only: 1.5 if the last spell cast was Empowered (spell projectiles read this when they hit).</summary>
    [ViewVariables]
    public float LastPower = 1f;

    /// <summary>Server only: how much of the last spell's mana was missing (0 = paid in full, 1 = none at all).</summary>
    [ViewVariables]
    public float LastOvercast;
}

/// <summary>Marker from the Wild Wasteland trait. Only these characters can learn Arcane from the Primer.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MagicWildWastelandComponent : Component;

/// <summary>A spellbook. Binds to the first person who writes a working spell in it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MagicGrimoireComponent : Component
{
    /// <summary>Pages hold the round's secret words: only the bound owner's client is sent them.</summary>
    public override bool SessionSpecific => true;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? MagicOwner;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public List<MagicPage> MagicPages = new();

    /// <summary>Blank pages a new book starts with.</summary>
    [DataField]
    public int PageCount = 12;

    /// <summary>Most pages the spine can hold. Blank vellum stitches in more pages up to this.</summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public int MagicMaxPages = 40;

    /// <summary>The Key of Runes leaf is bound into the book's Codex tab.</summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public bool MagicHasKey;

    /// <summary>The Spell Index leaf is bound in: the book gets a Spells tab listing every spell you know.</summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public bool MagicHasSpellIndex;

    /// <summary>Class word leaves bound into the book's Codex tab.</summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public HashSet<MagicClass> MagicCodexClasses = new();

    /// <summary>Server only: page index → action entity for readied pages.</summary>
    [ViewVariables]
    public Dictionary<int, EntityUid> PageActions = new();

    /// <summary>VV: set to true to fill the book with every spell (binds it to whoever holds it, if unbound).</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool MagicFillAllPages
    {
        get => false;
        set
        {
            if (value)
                IoCManager.Resolve<IEntityManager>().EventBus.RaiseLocalEvent(Owner, new MagicAdminFillGrimoireEvent());
        }
    }
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class MagicPage
{
    /// <summary>What the owner wrote, as plain letters. Shown to them as Arcane glyphs.</summary>
    [DataField]
    public string Text = string.Empty;

    /// <summary>The owner's own margin note. The game never names a spell.</summary>
    [DataField]
    public string Note = string.Empty;

    [DataField]
    public bool Readied;

    /// <summary>Server only (never sent): the spell these words make, if any.</summary>
    [NonSerialized]
    public ProtoId<MagicSpellPrototype>? Spell;

    [NonSerialized]
    public bool Empowered;

    /// <summary>Sent to the owner: the glow colour of a working page. Null = the ink lies still.</summary>
    [DataField]
    public MagicClass? Glow;

    /// <summary>
    /// Real spell words of a school still sealed to the writer: the page glows faintly in this class colour and
    /// says so, but does nothing until an admin unseals the other schools for them.
    /// </summary>
    [DataField]
    public MagicClass? Sealed;
}

/// <summary>On a spell action entity: which spell it is and whether it was written in the true order.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MagicSpellActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<MagicSpellPrototype>? Spell;

    [DataField, AutoNetworkedField]
    public bool Empowered;

    /// <summary>Cast from a one-use paper scroll: costs 50% more and the scroll crumbles afterwards.</summary>
    [DataField, AutoNetworkedField]
    public bool Scroll;
}

/// <summary>Added to a sheet of paper when someone who knows Arcane holds it and its words make a spell.</summary>
[RegisterComponent]
public sealed partial class MagicScrollComponent : Component
{
    [ViewVariables]
    public EntityUid? Action;

    [ViewVariables]
    public ProtoId<MagicSpellPrototype>? Spell;
}

/// <summary>Admin book: reading it unseals every school of magic for the reader.</summary>
[RegisterComponent]
public sealed partial class MagicUnsealingBookComponent : Component
{
    [DataField]
    public float ReadSeconds = 20f;
}

/// <summary>Treatise on Silent Casting: study it (90 s) once, then practise to cast without charging.</summary>
[RegisterComponent]
public sealed partial class MagicChantlessBookComponent : Component
{
    [DataField]
    public float ReadSeconds = 90f;
}

/// <summary>The Arcane Primer: reading it (60 s) teaches the Arcane language to Wild Wasteland characters.</summary>
[RegisterComponent]
public sealed partial class MagicPrimerComponent : Component
{
    [DataField]
    public float ReadSeconds = 60f;
}

/// <summary>
/// A Codex leaf. Use it on your grimoire to bind it into the Codex tab.
/// No class = the Key of Runes (every rune with its letter). A class = that class's word hints.
/// </summary>
[RegisterComponent]
public sealed partial class MagicCodexLeafComponent : Component
{
    [DataField]
    public MagicClass? Class;
}

/// <summary>The Spell Index: use it on your grimoire to add a Spells tab (every spell you know and what it does).</summary>
[RegisterComponent]
public sealed partial class MagicSpellIndexLeafComponent : Component;

/// <summary>Blank vellum. Use it on your grimoire to stitch in more blank pages.</summary>
[RegisterComponent]
public sealed partial class MagicVellumComponent : Component
{
    [DataField]
    public int Pages = 1;
}

// Admin VV events (handled on the server).
public sealed class MagicAdminGrantAllEvent : EntityEventArgs;
public sealed class MagicAdminRemoveAllEvent : EntityEventArgs;
public sealed class MagicAdminFillGrimoireEvent : EntityEventArgs;

/// <summary>
/// A page torn out of a grimoire. Still bound to the book's owner: only they can cast it (while holding it)
/// or slide it back into their book. To anyone else it's an old star chart.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MagicTornPageComponent : Component
{
    /// <summary>The page's words are secret: only the owner's client is sent them.</summary>
    public override bool SessionSpecific => true;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? MagicOwner;

    [DataField, AutoNetworkedField]
    public MagicPage Page = new();

    /// <summary>Server only: the spell action this page grants while held.</summary>
    [ViewVariables]
    public EntityUid? Action;
}

/// <summary>Server-side FEV dose tracking (how much has passed through, how many doses so far).</summary>
[RegisterComponent]
public sealed partial class MagicFEVExposureComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public float Metabolized;

    [ViewVariables(VVAccess.ReadWrite)]
    public int Doses;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool Awakened;

    [ViewVariables]
    public bool Overdosing;
}

/// <summary>Marks someone awakened by FEV. Admins see it when examining them.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MagicFEVAwakenedComponent : Component;
