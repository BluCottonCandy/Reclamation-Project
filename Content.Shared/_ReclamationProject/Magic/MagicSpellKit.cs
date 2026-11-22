using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.Magic;

// #Reclamation Project - the spell kit: reusable building blocks so most spells are pure YAML.
// A spell = how it picks targets (touch, area around you, zone where you click, projectile, conjure)
// + what happens to each target (a MagicHit) + optional extras (spawn something, teleport, a special).
// Empowered spells (true word order) hit 50% harder and last 50% longer.

/// <summary>Everything a spell can do to one target. Every field is optional; 0 = off.</summary>
[DataDefinition]
public sealed partial class MagicHit
{
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>Heals this much damage, spread over the damage types the target has (negative of Damage).</summary>
    [DataField]
    public DamageSpecifier? Heal;

    /// <summary>The damage goes straight through armour and resistances.</summary>
    [DataField] public bool IgnoreArmor;

    [DataField] public float Stun;
    [DataField] public float Knockdown;
    /// <summary>Seconds of slow. Speed is multiplied by <see cref="SlowAmount"/>.</summary>
    [DataField] public float Slow;
    [DataField] public float SlowAmount = 0.5f;
    [DataField] public float Sleep;
    [DataField] public float Drowsy;
    [DataField] public float Blind;
    /// <summary>Can't speak and can't cast.</summary>
    [DataField] public float Mute;
    /// <summary>Can't attack.</summary>
    [DataField] public float Pacify;
    /// <summary>Seeing things and stumbling (rainbows + drunk).</summary>
    [DataField] public float Confuse;
    [DataField] public float Jitter;
    [DataField] public float Stutter;
    [DataField] public float FireStacks;
    [DataField] public bool Extinguish;
    /// <summary>Thrown away from the spell's centre this many tiles.</summary>
    [DataField] public float Push;
    /// <summary>Thrown toward the caster (or the zone centre) this many tiles.</summary>
    [DataField] public float Pull;
    /// <summary>Bleeding added (negative stops bleeding).</summary>
    [DataField] public float Bleed;
    /// <summary>Blood added in units (negative drains blood).</summary>
    [DataField] public float Blood;
    /// <summary>Mana added to a caster (negative drains it).</summary>
    [DataField] public float Mana;
    /// <summary>Arcane Strain added (negative lowers it).</summary>
    [DataField] public float Strain;
    /// <summary>Drops everything in their hands and flings it.</summary>
    [DataField] public bool Disarm;
    /// <summary>Only drops guns and metal tools (Lodestone).</summary>
    [DataField] public bool DisarmGuns;
    /// <summary>Soaked: takes double Shock damage for this many seconds.</summary>
    [DataField] public float Wet;
    /// <summary>Electrocuted for this much Shock damage (also stuns briefly, like a real shock).</summary>
    [DataField] public int Shock;
    /// <summary>A message only the target sees (locale id).</summary>
    [DataField] public LocId? Message;
    /// <summary>Any other reagent-style effects (HealthChange, Emote, Polymorph, ...).</summary>
    [DataField] public List<EntityEffect> Effects = new();
    /// <summary>A timed magic condition (see <see cref="MagicCondition"/>).</summary>
    [DataField] public MagicCondition Condition = MagicCondition.None;
    [DataField] public float ConditionTime = 10f;
    /// <summary>Strength of the condition (charges, damage absorbed, Luck points...).</summary>
    [DataField] public float ConditionPower = 1f;
}

/// <summary>Timed magic conditions handled in code. They wear off by themselves.</summary>
public enum MagicCondition : byte
{
    None,
    /// <summary>Blocks the next N spells aimed at you.</summary>
    Ward,
    /// <summary>Absorbs the next N damage.</summary>
    Absorb,
    /// <summary>The next hit misses you completely.</summary>
    Dodge,
    /// <summary>Your next N spells or attacks fail.</summary>
    Hex,
    /// <summary>Invisible (and see-through).</summary>
    Invisible,
    /// <summary>Can't be knocked down.</summary>
    Steady,
    /// <summary>Takes double Shock damage.</summary>
    Wet,
    /// <summary>Half of all damage you take goes to your bond partner instead.</summary>
    BloodBond,
    /// <summary>Pulled after the caster every half second (puppets and leashes).</summary>
    Leash,
    /// <summary>Elemental spells cost half mana.</summary>
    Avatar,
    /// <summary>The next spell costs no mana and skips reading.</summary>
    Echo,
    /// <summary>Telepathy chat for a while.</summary>
    Telepathy,
    /// <summary>Speaks and understands every language.</summary>
    Tongues,
    /// <summary>+Endurance (more max health) and +Luck for a while (Titan's Blessing).</summary>
    Titan,
    /// <summary>Melee hits and swings are weaker; slowed (Curse of Weakness).</summary>
    Weakness,
    /// <summary>A popup tells the caster which way the target is, every few seconds.</summary>
    Tracked,
    /// <summary>Takes brain damage over time while asleep (Nightmare).</summary>
    Nightmare,
    /// <summary>Rotting: slow Cellular damage over time.</summary>
    Rot,
    /// <summary>Withering: slow Poison damage over time (Wither curse; easy to treat, unlike Rot).</summary>
    Wither,
}

/// <summary>Spell behaviours that need their own code.</summary>
public enum MagicSpecial : byte
{
    None,
    /// <summary>Caster and target trade places.</summary>
    SwapWithTarget,
    /// <summary>The target and the nearest other person trade places.</summary>
    SwapTargetWithNearest,
    /// <summary>Disarm + magic cuffs that vanish after 2 minutes.</summary>
    Shackles,
    /// <summary>Tells the caster the target's name, job and how hurt they are.</summary>
    ReadMind,
    /// <summary>Tells the caster every kind of damage on the target.</summary>
    Diagnose,
    /// <summary>Lists the minds nearby and which way they are.</summary>
    SenseMinds,
    /// <summary>Shows an arrow over everyone nearby for where they are heading.</summary>
    Foresight,
    /// <summary>Lights in the area go out for a while.</summary>
    LightsOut,
    /// <summary>Everyone caught is thrown to random spots across the map (DOOM).</summary>
    Scatter,
    /// <summary>Frees the caster's grimoires so someone else can claim them.</summary>
    Unbind,
    /// <summary>Brings a dead body back to life.</summary>
    Resurrect,
    /// <summary>Breaks every spell being read in the area.</summary>
    DispelArea,
    /// <summary>The target blurts out a random line.</summary>
    ImplantThought,
    /// <summary>The target forgets the last few minutes (told to them).</summary>
    MemoryWipe,
    /// <summary>Shows the last fingerprints on an item.</summary>
    Psychometry,
    /// <summary>Takes the target's name for 60 seconds.</summary>
    FalseFace,
    /// <summary>Uses up the grimoire in hand and writes its spells into your mind.</summary>
    MindInscription,
    /// <summary>Teleports the caster to the target spot.</summary>
    TeleportCaster,
    /// <summary>Teleports the caster only into a dark spot.</summary>
    TeleportCasterDark,
    /// <summary>Crafting: scrap into steel, steel into plasteel.</summary>
    TransmuteMetal,
    /// <summary>Crafting: steel into silver, gold or uranium.</summary>
    TransmuteRare,
    /// <summary>Crafting: raw food into medicine.</summary>
    TransmuteLife,
    /// <summary>Crafting: copies a simple held item.</summary>
    Duplicate,
    /// <summary>Crafting: upgrades a held melee weapon.</summary>
    ArcaneForge,
    /// <summary>EMP burst (drains batteries, energy weapons, synths).</summary>
    Emp,
    /// <summary>Ion Storm: lights go out (20 s) and an EMP burst goes off in the same area.</summary>
    LightsOutEmp,
    /// <summary>Possession: the caster's mind takes over the target's body for 10 s; the target's mind is stuck in the caster's body, which can't move or speak.</summary>
    Possession,
    /// <summary>Telekinesis: pull a loose item into your hands, or rip what a person is holding out of their hands into yours.</summary>
    TelekinesisGrab,
    /// <summary>Pulls an item into the caster's hands (Telekinesis / Mind Pull on items).</summary>
    FetchItem,
    /// <summary>Spawns copies of the caster's look that wander (Mirror Image).</summary>
    MirrorImage,
    /// <summary>Spawned creatures fight for the caster (clones, golems, raised dead).</summary>
    Servant,
}

/// <summary>Where an area spell lands.</summary>
public enum MagicShape : byte
{
    Circle,
    /// <summary>A straight line from the caster toward the click.</summary>
    Line,
    /// <summary>A 90 degree cone from the caster toward the click.</summary>
    Cone,
}

/// <summary>Click a person (or item). Effects hit them; optional effects on yourself.</summary>
public sealed partial class MagicTouchSpellEvent : EntityTargetActionEvent
{
    [DataField] public MagicHit Hit = new();
    [DataField] public MagicHit? Self;
    [DataField] public MagicSpecial Special = MagicSpecial.None;
    /// <summary>Spawned on the target (zones, effects, servants).</summary>
    [DataField] public EntProtoId? Spawn;
    [DataField] public int SpawnCount = 1;
    /// <summary>Seconds before the hit lands (Fission Mind, Sky Strike).</summary>
    [DataField] public float Delay;
    /// <summary>When the delay ends, this also hits everyone around the target.</summary>
    [DataField] public float BurstRadius;
    [DataField] public MagicHit? Burst;
    /// <summary>Only someone who is dead (Resurrect, Raise Dead).</summary>
    [DataField] public bool DeadOnly;
}

/// <summary>Everyone around you. Instant, no aiming.</summary>
public sealed partial class MagicAreaSpellEvent : InstantActionEvent
{
    [DataField] public float Radius = 3f;
    [DataField] public MagicHit Hit = new();
    [DataField] public MagicHit? Self;
    [DataField] public bool IncludeSelf;
    /// <summary>Also hits items lying around (pull and push only).</summary>
    [DataField] public bool Items;
    [DataField] public MagicSpecial Special = MagicSpecial.None;
    [DataField] public EntProtoId? Spawn;
    [DataField] public int SpawnCount = 1;
    [DataField] public float SpawnSpread;
}

/// <summary>Click a spot. Hits everyone in a shape there, can spawn things, can move you there.</summary>
public sealed partial class MagicZoneSpellEvent : WorldTargetActionEvent
{
    [DataField] public float Radius = 2f;
    [DataField] public MagicShape Shape = MagicShape.Circle;
    [DataField] public float Length = 7f;
    [DataField] public MagicHit Hit = new();
    [DataField] public MagicHit? Self;
    [DataField] public bool IncludeSelf;
    [DataField] public bool Items;
    [DataField] public MagicSpecial Special = MagicSpecial.None;
    [DataField] public EntProtoId? Spawn;
    [DataField] public int SpawnCount = 1;
    [DataField] public float SpawnSpread;
    /// <summary>Hit happens this many seconds after the cast (a warning shimmer shows first).</summary>
    [DataField] public float Delay;
}

/// <summary>A weapon (or other item) appears in your hands. It vanishes after a while or when dropped.</summary>
public sealed partial class MagicConjureSpellEvent : InstantActionEvent
{
    [DataField(required: true)] public EntProtoId Item;
    [DataField] public int Count = 1;
    [DataField] public float Lifetime = 120f;
    /// <summary>Put it on (outer clothing) instead of in your hands.</summary>
    [DataField] public string? EquipSlot;
    [DataField] public bool VanishOnDrop = true;
    [DataField] public MagicHit? Self;
}

/// <summary>On a spell projectile: what happens to whatever it hits (plus an optional blast around it).</summary>
[RegisterComponent]
public sealed partial class MagicOnHitComponent : Component
{
    [DataField] public MagicHit Hit = new();
    [DataField] public float BurstRadius;
    [DataField] public MagicHit? Burst;
    [DataField] public EntProtoId? Spawn;
    [DataField] public MagicSpecial Special = MagicSpecial.None;
    /// <summary>Set when fired: 1.5 for Empowered.</summary>
    [ViewVariables] public float Power = 1f;
    [ViewVariables] public EntityUid? Caster;
}

/// <summary>
/// A lingering spell zone (fire pool, healing ward, gravity well, storm cloud...).
/// Every <see cref="Interval"/> seconds it hits everyone inside. It vanishes on its own (TimedDespawn).
/// </summary>
[RegisterComponent]
public sealed partial class MagicAuraComponent : Component
{
    [DataField] public float Radius = 2f;
    [DataField] public float Interval = 1f;
    [DataField] public MagicHit Hit = new();
    /// <summary>What the caster gets instead while inside (Blood Mist heals its caster).</summary>
    [DataField] public MagicHit? CasterHit;
    [DataField] public bool HitCaster;
    [DataField] public bool Items;
    /// <summary>Drifts slowly in a random direction (fire tornado, fallout cloud).</summary>
    [DataField] public float Drift;
    [DataField] public MagicSpecial Special = MagicSpecial.None;
    [ViewVariables] public EntityUid? Caster;
    [ViewVariables] public float Power = 1f;
    [ViewVariables] public float NextPulse;
}

/// <summary>A conjured item: vanishes when dropped (if set). Lifetime is a TimedDespawn.</summary>
[RegisterComponent]
public sealed partial class MagicConjuredComponent : Component
{
    [DataField] public bool VanishOnDrop = true;
}

/// <summary>Active timed magic conditions on someone (server only).</summary>
[RegisterComponent]
public sealed partial class MagicConditionsComponent : Component
{
    [ViewVariables] public Dictionary<MagicCondition, MagicActiveCondition> Active = new();
}

public sealed class MagicActiveCondition
{
    public TimeSpan Ends;
    public float Power;
    public EntityUid? Source;
    public TimeSpan NextTick;
}

/// <summary>A creature summoned by a spell: fights for its caster, vanishes after a while.</summary>
[RegisterComponent]
public sealed partial class MagicServantComponent : Component
{
    [ViewVariables] public EntityUid? Master;
}

/// <summary>A held melee weapon already upgraded by Arcane Forge (only once each).</summary>
[RegisterComponent]
public sealed partial class MagicForgedComponent : Component;

/// <summary>Magic cuffs from Arcane Shackles. Dispel removes them.</summary>
[RegisterComponent]
public sealed partial class MagicShacklesComponent : Component;
