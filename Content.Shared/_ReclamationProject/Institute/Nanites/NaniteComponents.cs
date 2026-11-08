using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Institute.Nanites;

/// <summary>
/// Institute nanites. After a while without taking damage they repair everything: every damage type on
/// the body and each limb, lost blood and bleeding, broken bones, organ damage and internal bleeding.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NaniteRepairComponent : Component
{
    /// <summary>How long without taking damage before the nanites start.</summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(10);

    /// <summary>Healed per second for each damage type, on the body and on every limb.</summary>
    [DataField]
    public float HealPerSecond = 1.25f;

    [DataField]
    public float BloodPerSecond = 5f;

    /// <summary>Organ damage points repaired per second.</summary>
    [DataField]
    public float OrganPerSecond = 2f;

    /// <summary>Lost maximum health (out of 100) rebuilt per second on failed organs and limbs.</summary>
    [DataField]
    public float CapacityPerSecond = 2f;

    /// <summary>Bone healing progress per second; a fracture is fixed at 100.</summary>
    [DataField]
    public float BonePerSecond = 10f;

    /// <summary>Damage from these doesn't count as getting hurt, so bleeding out or starving can't stop the repair.</summary>
    [DataField]
    public HashSet<string> IgnoredDamageTypes = new() { "Bloodloss", "Asphyxiation", "Starvation", "Dehydration" };

    [DataField]
    public LocId StartMessage = "nanite-repair-start";

    [DataField]
    public LocId FinishMessage = "nanite-repair-finish";

    public TimeSpan LastDamaged;
    public TimeSpan NextUpdate;
    public bool Repairing;
}

/// <summary>Can eat and drink, but never gets hungry or thirsty, and losing blood never hurts or dizzies them.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SyntheticSustenanceComponent : Component
{
    public TimeSpan NextCheck;
}

/// <summary>
/// Crawls in crit at the normal lying-down speed instead of the usual crit crawl, which also stacks the
/// crit penalty (x0.5) and the damage slowdown (x0.55) on top of lying down.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SteadyCrawlComponent : Component
{
    [DataField]
    public float CritCrawlMultiplier = 3.6f;
}

/// <summary>Built-in fire resistance. Only cuts burning and fire tiles, not lasers or other Heat damage.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class InnateFireProtectionComponent : Component
{
    [DataField]
    public float Reduction = 0.95f;
}
