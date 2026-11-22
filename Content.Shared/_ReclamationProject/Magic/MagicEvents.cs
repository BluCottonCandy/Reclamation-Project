using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Magic;

// #Reclamation Project - Arcane magic events and grimoire UI messages.

[Serializable, NetSerializable]
public enum MagicGrimoireUiKey : byte
{
    Key,
}

/// <summary>Everything the owner's grimoire window needs. Never contains spell names or stats.</summary>
[Serializable, NetSerializable]
public sealed class MagicGrimoireBuiState : BoundUserInterfaceState
{
    public List<MagicPage> Pages = new();
    public int Readied;
    public int ActiveLimit;
    public float Mana;
    public float MaxMana;
    public bool HasPen;
    public int MaxPages;
    /// <summary>The Key of Runes leaf is bound in (shown while writing).</summary>
    public bool HasKey;
    /// <summary>Class word leaves bound into the Codex tab.</summary>
    public List<MagicClass> CodexClasses = new();
    /// <summary>The word entries for the bound class leaves, for this owner (hints or solved words).</summary>
    public List<MagicCodexEntry> Codex = new();
    /// <summary>Every word this owner has written correctly (named and spelled out), for the Words tab.</summary>
    public List<MagicCodexEntry> KnownWords = new();
    /// <summary>The Spell Index leaf is bound in: the Spells tab lists every spell the owner knows.</summary>
    public bool HasSpellIndex;
    /// <summary>Every spell the owner knows (mind, memorised, or a glowing page), for the Spells tab.</summary>
    public List<MagicKnownSpellEntry> KnownSpells = new();
}

/// <summary>One spell in the Spells tab: what it is, how to say it, what it does, and where you hold it.</summary>
[Serializable, NetSerializable]
public sealed class MagicKnownSpellEntry
{
    public string Name = string.Empty;
    public MagicClass Class;
    /// <summary>The words that make it, e.g. "Earth + Fire".</summary>
    public string Words = string.Empty;
    /// <summary>The spoken form in plain letters (drawn as runes with the letter under each).</summary>
    public string Incantation = string.Empty;
    public int ManaCost;
    public string Summary = string.Empty;
    /// <summary>Where it is held: in the mind, memorised, or on a page.</summary>
    public string Where = string.Empty;
}

[Serializable, NetSerializable]
public sealed class MagicWritePageMessage(int page, string text) : BoundUserInterfaceMessage
{
    public readonly int Page = page;
    public readonly string Text = text;
}

[Serializable, NetSerializable]
public sealed class MagicSetNoteMessage(int page, string note) : BoundUserInterfaceMessage
{
    public readonly int Page = page;
    public readonly string Note = note;
}

[Serializable, NetSerializable]
public sealed class MagicToggleReadyMessage(int page) : BoundUserInterfaceMessage
{
    public readonly int Page = page;
}

[Serializable, NetSerializable]
public sealed class MagicCastPageMessage(int page) : BoundUserInterfaceMessage
{
    public readonly int Page = page;
}

/// <summary>Healing spell: heals brute damage on the touched target.</summary>
public sealed partial class MagicHealSpellEvent : EntityTargetActionEvent
{
    [DataField]
    public float Brute = 15f;

    [DataField]
    public float Burn;
}

[Serializable, NetSerializable]
public sealed partial class MagicPrimerDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Reading a spell before it fires.</summary>
[Serializable, NetSerializable]
public sealed partial class MagicReadDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Writing a spell into your own mind (Mind Etching).</summary>
[Serializable, NetSerializable]
public sealed partial class MagicEtchDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed class MagicEtchPageMessage(int page) : BoundUserInterfaceMessage
{
    public readonly int Page = page;
}

/// <summary>Dispel: breaks a spell the target is reading (and puts it on cooldown).</summary>
public sealed partial class MagicDispelSpellEvent : EntityTargetActionEvent
{
    [DataField]
    public float Cooldown = 5f;
}

/// <summary>Dark Pact: trade some of your own health for mana.</summary>
public sealed partial class MagicDarkPactEvent : InstantActionEvent
{
    [DataField]
    public float HealthCost = 20f;

    [DataField]
    public float ManaGain = 30f;
}

/// <summary>Clot: stops your own bleeding and gives back some blood.</summary>
public sealed partial class MagicClotEvent : InstantActionEvent
{
    [DataField]
    public float BloodRestored = 25f;
}

/// <summary>Blood Boil: burns the target from the inside (ignores armour).</summary>
public sealed partial class MagicBloodBoilEvent : EntityTargetActionEvent
{
    [DataField]
    public float Heat = 20f;
}

/// <summary>Ignite: sets the target (a person or anything that burns) on fire.</summary>
public sealed partial class MagicIgniteSpellEvent : EntityTargetActionEvent
{
    [DataField]
    public float FireStacks = 2f;

    /// <summary>Heat damage on the spot, before the burning starts.</summary>
    [DataField]
    public float Heat;
}

/// <summary>Blink: short teleport to the clicked spot (server side only, so it never mispredicts).</summary>
public sealed partial class MagicBlinkSpellEvent : WorldTargetActionEvent;

[Serializable, NetSerializable]
public sealed class MagicTearOutMessage(int page) : BoundUserInterfaceMessage
{
    public readonly int Page = page;
}

/// <summary>One-time action from a first successful FEV dose: a bound grimoire and quill appear.</summary>
public sealed partial class MagicManifestGrimoireEvent : InstantActionEvent;

/// <summary>
/// One word in the Codex. Until the reader has written it correctly they only get a one-word hint and the
/// jumbled runes (first rune glowing). Once solved, the Codex names it and spells the word out in order.
/// </summary>
[Serializable, NetSerializable]
public sealed class MagicCodexEntry
{
    public bool Solved;
    /// <summary>Only filled in once solved.</summary>
    public string Name = string.Empty;
    /// <summary>Only filled in once solved: the word in the right order.</summary>
    public string Plain = string.Empty;
    public string Hint = string.Empty;
    public string Scrambled = string.Empty;
    public char First;
    public MagicClass Class;
}

/// <summary>
/// Rock Bullet: each cast summons one rock that circles the caster (up to <see cref="Max"/>, each lasting
/// <see cref="Lifetime"/> seconds). The caster's next attack fires every stored rock where they face.
/// </summary>
public sealed partial class MagicSummonRockEvent : InstantActionEvent
{
    [DataField(required: true)]
    public Robust.Shared.Prototypes.EntProtoId Projectile;

    [DataField]
    public int Max = 4;

    [DataField]
    public float Lifetime = 60f;
}

/// <summary>Rocks summoned by Rock Bullet, circling their caster until they attack or the rocks run out of time.</summary>
[RegisterComponent, Robust.Shared.GameStates.NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MagicStoredRocksComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>How many rocks are circling (sent to the owner's client so a left click can fire them).</summary>
    [AutoNetworkedField] public int Count;

    [ViewVariables] public List<EntityUid> Rocks = new();
    [ViewVariables] public List<TimeSpan> Expires = new();
    [ViewVariables] public Robust.Shared.Prototypes.EntProtoId? Projectile;
    [ViewVariables] public float Angle;
}

/// <summary>Client to server: left click in combat mode with rocks circling - fire them at this spot.</summary>
[Serializable, NetSerializable]
public sealed class MagicFireRocksEvent : EntityEventArgs
{
    public Robust.Shared.Map.NetCoordinates Target;

    public MagicFireRocksEvent(Robust.Shared.Map.NetCoordinates target)
    {
        Target = target;
    }
}

/// <summary>Undying Will (King): cast while dead. You come back, hurt but alive.</summary>
public sealed partial class MagicSelfReviveEvent : InstantActionEvent
{
    /// <summary>How hurt you come back, as a share of your crit point (0.75 = three quarters of the way to crit).</summary>
    [DataField]
    public float WoundsLeft = 0.75f;
}

/// <summary>Astral Projection: your mind leaves your body as an unseen spirit for a while, then snaps back.</summary>
public sealed partial class MagicAstralProjectionEvent : InstantActionEvent
{
    [DataField]
    public float Seconds = 60f;

    [DataField]
    public Robust.Shared.Prototypes.EntProtoId Form = "MagicAstralForm";
}

/// <summary>The Undying Will button that appears on your hotbar when you die (if you have the spell ready).</summary>
public sealed partial class MagicUndyingChoiceEvent : InstantActionEvent;

/// <summary>The astral form's button: go back to your body now.</summary>
public sealed partial class MagicAstralReturnEvent : InstantActionEvent;

/// <summary>Phantom: toggles a magic cloak. While it is up it drains mana every second.</summary>
public sealed partial class MagicPhantomEvent : InstantActionEvent
{
    [DataField]
    public float DrainPerSecond = 1f;
}

/// <summary>On someone whose Phantom cloak is up.</summary>
[RegisterComponent]
public sealed partial class MagicPhantomComponent : Component
{
    [ViewVariables] public float DrainPerSecond = 1f;
    [ViewVariables] public float Accumulator;
}

/// <summary>Telepathy that comes with psychic powers: timed Telepathy spells never take it away.</summary>
[RegisterComponent]
public sealed partial class MagicPermanentTelepathyComponent : Component;

/// <summary>On both bodies while a Possession is running, so a second one cannot tangle the minds.</summary>
[RegisterComponent]
public sealed partial class MagicPossessionComponent : Component;

/// <summary>
/// Mirror Image copy: looks and dresses like the caster, wanders about, and pops the moment it is hurt
/// or its time runs out. It cannot be stripped, and everything it wears vanishes with it.
/// </summary>
[RegisterComponent]
public sealed partial class MagicMirrorImageComponent : Component
{
    [DataField]
    public float Lifetime = 20f;

    [ViewVariables]
    public TimeSpan Expires;
}

/// <summary>On fire spell projectiles: the floor where they land catches fire (radius 0 = one tile).</summary>
[RegisterComponent]
public sealed partial class MagicFloorFireOnHitComponent : Component
{
    [DataField]
    public float Radius;
}

/// <summary>Psionic insulation a timed telepathy spell lifted; it comes back when the spell ends.</summary>
[RegisterComponent]
public sealed partial class MagicLiftedInsulationComponent : Component;

/// <summary>Client to server: start charging this targeted spell (pressed on the hotbar, not charged yet).</summary>
[Serializable, NetSerializable]
public sealed class MagicStartChargeEvent : EntityEventArgs
{
    public NetEntity Action;

    public MagicStartChargeEvent(NetEntity action)
    {
        Action = action;
    }
}

/// <summary>Server to client: the spell is charged and held - start aiming it.</summary>
[Serializable, NetSerializable]
public sealed class MagicSpellChargedEvent : EntityEventArgs
{
    public NetEntity Action;

    public MagicSpellChargedEvent(NetEntity action)
    {
        Action = action;
    }
}

[Serializable, NetSerializable]
public sealed partial class MagicChantlessDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Clothes (and anything inside them) a Mirror Image copy wears. Taken out of the copy, they vanish.</summary>
[RegisterComponent]
public sealed partial class MagicIllusionItemComponent : Component;

[Serializable, NetSerializable]
public sealed partial class MagicUnsealDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Psy-Forge (+25%) and Greater Psy-Forge (+50%, King): raise the held armour's protection for good.</summary>
public sealed partial class MagicPsyForgeEvent : InstantActionEvent
{
    /// <summary>How much of the armour's normal protection is added (0.25 = +25%).</summary>
    [DataField]
    public float Bonus = 0.25f;

    /// <summary>The King version (its own once-per-piece slot, so both stack to +75%).</summary>
    [DataField]
    public bool Major;
}

/// <summary>
/// Armour improved by Psy-Forge. Keeps the armour's normal protection so every upgrade is worked out from it
/// (+25% and +50% of the normal value stack to +75%, never compounding).
/// </summary>
[RegisterComponent]
public sealed partial class MagicPsyForgedComponent : Component
{
    [ViewVariables] public bool Minor;
    [ViewVariables] public bool Major;
    [ViewVariables] public float Bonus;
    [ViewVariables] public Dictionary<string, float> BaseCoefficients = new();
    [ViewVariables] public Dictionary<string, float> BaseFlat = new();
    [ViewVariables] public string BaseName = string.Empty;

    /// <summary>A melee weapon's normal hit.</summary>
    [ViewVariables] public Content.Shared.Damage.DamageSpecifier? BaseMelee;

    /// <summary>A gun's own flat beam bonus before forging (Misfits GunDamageBonus), kept so forging adds to it.</summary>
    [ViewVariables] public Content.Shared.Damage.DamageSpecifier? BaseBeamBonus;

    /// <summary>A power cell's normal capacity.</summary>
    [ViewVariables] public float BaseMaxCharge;
}

/// <summary>
/// On a fired bullet boosted by Psy-Forge: its damage before the boost and the bonus used. A forged gun firing forged
/// bullets uses the bigger of the two bonuses (they don't multiply), and a burst never boosts a bullet twice.
/// </summary>
[RegisterComponent]
public sealed partial class MagicPsyForgedShotComponent : Component
{
    [ViewVariables] public Content.Shared.Damage.DamageSpecifier? BaseDamage;
    [ViewVariables] public float Bonus;
}

/// <summary>Raised on an item after Psy-Forge changed it (the psy katana re-applies its drawn / sheathed damage).</summary>
[ByRefEvent]
public record struct MagicPsyForgedChangedEvent;
