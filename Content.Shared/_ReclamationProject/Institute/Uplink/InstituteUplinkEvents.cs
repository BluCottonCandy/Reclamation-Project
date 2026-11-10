using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Institute.Uplink;

// #Reclamation Project - Institute uplink implant: messages between the server and the client windows.

/// <summary>Action event of the uplink implant's button.</summary>
public sealed partial class OpenInstituteUplinkEvent : InstantActionEvent;

/// <summary>
/// Raised on the buyer by the free "Institute network" store listing.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class OpenInstituteNetworkEvent : EntityEventArgs;

/// <summary>Server -> client: ask the player for their uplink PIN. Implant may also be an Institute tablet.</summary>
[Serializable, NetSerializable]
public sealed class InstitutePinRequestEvent : EntityEventArgs
{
    public NetEntity Implant;
    public bool Tablet;
    public string? Error;
}

/// <summary>Client -> server: the PIN the player typed.</summary>
[Serializable, NetSerializable]
public sealed class InstitutePinSubmitEvent : EntityEventArgs
{
    public NetEntity Implant;
    public string Pin = string.Empty;
}

/// <summary>Client -> server: send the network window state again.</summary>
[Serializable, NetSerializable]
public sealed class InstituteNetworkRequestEvent : EntityEventArgs;

[Serializable, NetSerializable]
public enum InstituteObjectiveKind : byte
{
    Steal,
    Kill,
    Protect,
    Survive,
    Custom,
}

[Serializable, NetSerializable]
public sealed class InstituteObjectiveEntry
{
    public NetEntity Objective;
    public string Title = string.Empty;
    public int Reward;
    public bool Done;
    public bool Paid;
}

[Serializable, NetSerializable]
public sealed class InstituteMemberEntry
{
    public NetEntity Mind;
    public string Name = string.Empty;
    public string Job = string.Empty;
    /// <summary>True when the viewer may reset this member's PIN and edit their objectives.</summary>
    public bool CanManage;
    /// <summary>The member's uplink and tablet PIN. Only sent to command who can manage them.</summary>
    public string? Pin;
    public List<InstituteObjectiveEntry> Objectives = new();
}

[Serializable, NetSerializable]
public sealed class InstitutePersonEntry
{
    public NetEntity Mind;
    public string Name = string.Empty;
}

[Serializable, NetSerializable]
public sealed class InstituteItemEntry
{
    public string Prototype = string.Empty;
    public string Name = string.Empty;
}

/// <summary>Server -> client: everything the Institute network window shows.</summary>
[Serializable, NetSerializable]
public sealed class InstituteNetworkStateEvent : EntityEventArgs
{
    public NetEntity Self;
    public int Balance;
    public bool IsCommand;
    public bool IsDirector;
    public List<InstituteMemberEntry> Members = new();
    public List<InstitutePersonEntry> People = new();
    public List<InstituteItemEntry> StealItems = new();
    public string? Status;
}

[Serializable, NetSerializable]
public sealed class InstituteSendPointsEvent : EntityEventArgs
{
    public NetEntity Target;
    public int Amount;
}

/// <summary>Command only: give a member a PIN of the commander's choosing.</summary>
[Serializable, NetSerializable]
public sealed class InstituteSetPinEvent : EntityEventArgs
{
    public NetEntity Target;
    public string Pin = string.Empty;
}

/// <summary>Command only. A null target resets every member's PIN.</summary>
[Serializable, NetSerializable]
public sealed class InstituteResetPinEvent : EntityEventArgs
{
    public NetEntity? Target;
}

[Serializable, NetSerializable]
public sealed class InstituteAssignObjectiveEvent : EntityEventArgs
{
    public NetEntity Target;
    public InstituteObjectiveKind Kind;
    public NetEntity? Person;
    public string? Item;
    public string Title = string.Empty;
    public string Text = string.Empty;
    public int Reward;
}

[Serializable, NetSerializable]
public sealed class InstituteObjectiveActionEvent : EntityEventArgs
{
    public NetEntity Objective;
    /// <summary>True removes the objective, false force-completes it.</summary>
    public bool Remove;
}
