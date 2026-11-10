using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Institute.Uplink;

// #Reclamation Project - Institute uplink implant components.

/// <summary>On the uplink implant. Holds the PIN and who unlocked it.</summary>
[RegisterComponent]
public sealed partial class InstituteUplinkComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public string Pin = string.Empty;

    /// <summary>The mind this implant was issued to. Its PIN shows in that mind's character menu.</summary>
    [ViewVariables]
    public EntityUid? OwnerMind;

    /// <summary>The body that typed the right PIN. Any other body has to type it again.</summary>
    [ViewVariables]
    public EntityUid? UnlockedFor;

    [ViewVariables]
    public TimeSpan NextAttempt;
}

/// <summary>On the mind of every Institute member.</summary>
[RegisterComponent]
public sealed partial class InstituteMemberComponent : Component
{
    [ViewVariables]
    public EntityUid? Implant;

    [ViewVariables]
    public ProtoId<JobPrototype> Job;
}

/// <summary>The Institute antagonist role, kept on the mind.</summary>
[RegisterComponent]
public sealed partial class InstituteAgentRoleComponent : AntagonistRoleComponent;

/// <summary>Game rule that lists Institute members in the round-end summary.</summary>
[RegisterComponent]
public sealed partial class InstituteRuleComponent : Component
{
    /// <summary>Items the Director can pick for a "steal an item" objective.</summary>
    [DataField]
    public List<EntProtoId> StealTargets = new();
}

/// <summary>On every objective given out through the Institute network.</summary>
[RegisterComponent]
public sealed partial class InstituteObjectiveComponent : Component
{
    [DataField]
    public bool AutoPay = true;

    [ViewVariables(VVAccess.ReadWrite)]
    public int Reward;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool Paid;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool ForceCompleted;

    [ViewVariables]
    public string Assigner = string.Empty;
}

/// <summary>Steal objective: have this many of an item prototype on you.</summary>
[RegisterComponent]
public sealed partial class InstituteStealObjectiveComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId? Item;

    [ViewVariables(VVAccess.ReadWrite)]
    public int Count = 1;
}

/// <summary>Custom written order. Only done when someone force-completes it.</summary>
[RegisterComponent]
public sealed partial class InstituteCustomObjectiveComponent : Component;

/// <summary>Bought reinforcement waiting for a ghost to take it.</summary>
[RegisterComponent]
public sealed partial class InstituteReinforcementBeaconComponent : Component
{
    [DataField(required: true)]
    public ProtoId<JobPrototype> Job;

    [DataField(required: true)]
    public string Species = string.Empty;

    /// <summary>Research Points refunded if nobody takes the role in time.</summary>
    [DataField]
    public int Refund;

    [DataField]
    public TimeSpan Timeout = TimeSpan.FromMinutes(2);

    [ViewVariables]
    public TimeSpan RefundAt;

    [ViewVariables]
    public EntityUid? Store;
}

/// <summary>Grenade that relays in Gen 1 synths when it goes off.</summary>
[RegisterComponent]
public sealed partial class InstituteSynthDropComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Synth;

    [DataField]
    public int Count = 3;

    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromMinutes(10);
}

/// <summary>Grenade synth. Relays out when the time is up or when it dies.</summary>
[RegisterComponent]
public sealed partial class InstituteTemporarySynthComponent : Component
{
    [ViewVariables]
    public TimeSpan DespawnAt;
}
