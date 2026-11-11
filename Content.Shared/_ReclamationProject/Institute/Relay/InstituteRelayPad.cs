using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Institute.Relay;

// #Reclamation Project - Institute relay pads: Gen 3 synths and Coursers relay onto any deployed pad.

/// <summary>A relay pad Gen 3 synths and Coursers can relay onto.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class InstituteRelayPadComponent : Component;

/// <summary>
/// On Gen 3 synth and Courser bodies: gives the "Relay to pad" action, which relays the body
/// (and everyone it's grabbing or carrying) onto a pad picked from a list.
/// </summary>
[RegisterComponent]
public sealed partial class InstitutePadTravelerComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionInstituteRelayToPad";

    [ViewVariables]
    public EntityUid? ActionEntity;

    /// <summary>Relaying again sooner than this makes the user sick, like the relay implant.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [ViewVariables]
    public TimeSpan? LastUse;

    [ViewVariables]
    public bool CoolingDown;

    [DataField]
    public ProtoId<AlertPrototype> CooldownAlert = "InstituteRelayCooldown";

    [DataField]
    public DamageSpecifier OrganicPenalty = new() { DamageDict = { ["Heat"] = 15 } };

    [DataField]
    public DamageSpecifier RobotPenalty = new() { DamageDict = { ["Shock"] = 15 } };
}

public sealed partial class InstituteOpenPadListEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed class InstitutePadEntry
{
    public NetEntity Pad;
    public string Name = string.Empty;
    /// <summary>Distance and direction from the user, or "another level".</summary>
    public string Where = string.Empty;
}

/// <summary>Server -> client: open the pad picker.</summary>
[Serializable, NetSerializable]
public sealed class InstitutePadListEvent : EntityEventArgs
{
    public List<InstitutePadEntry> Pads = new();
    public string? Status;
}

/// <summary>Client -> server: relay me onto this pad.</summary>
[Serializable, NetSerializable]
public sealed class InstitutePadRelayRequestEvent : EntityEventArgs
{
    public NetEntity Pad;
}
