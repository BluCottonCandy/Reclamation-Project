using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.Institute.Relay;

/// <summary>
/// Institute relay implant, a head organ in the <see cref="SlotId"/> slot. Fries anyone outside the
/// Institute who gets it, and with <see cref="CanSelfRelay"/> lets its owner fly out as a relay
/// signal and relay in elsewhere.
/// </summary>
[RegisterComponent]
public sealed partial class InstituteRelayImplantComponent : Component
{
    public const string SlotId = "relay";

    [DataField]
    public EntProtoId Action = "ActionInstituteRelay";

    [ViewVariables]
    public EntityUid? ActionEntity;

    /// <summary>
    /// Director and Division Head relay from the implant itself. Coursers relay through a pad instead.
    /// </summary>
    [DataField]
    public bool CanSelfRelay;

    [DataField]
    public string Faction = "Institute";

    /// <summary>
    /// Relaying in again sooner than this after the last arrival makes the user sick.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    [ViewVariables]
    public TimeSpan? LastArrival;

    /// <summary>
    /// True while the cooldown alert is up; cleared (with a "ready" popup) when the cooldown ends.
    /// </summary>
    [ViewVariables]
    public bool CoolingDown;

    [DataField]
    public ProtoId<AlertPrototype> CooldownAlert = "InstituteRelayCooldown";

    /// <summary>
    /// Early relay penalty for anyone with a stomach (they also vomit).
    /// </summary>
    [DataField]
    public DamageSpecifier OrganicPenalty = new() { DamageDict = { ["Heat"] = 15 } };

    /// <summary>
    /// Early relay penalty for anyone without a stomach (robots).
    /// </summary>
    [DataField]
    public DamageSpecifier RobotPenalty = new() { DamageDict = { ["Shock"] = 15 } };

    /// <summary>
    /// Heat added on top of the dead threshold when the implant fries a non-Institute brain.
    /// </summary>
    [DataField]
    public float WrongHeadExtraHeat = 50f;

    [DataField]
    public EntProtoId EyePrototype = "InstituteRelayEye";
}

/// <summary>
/// The unseen relay signal. The relaying body (and everyone in its grab/carry chain) rides
/// inside it until it relays in or cancels.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class InstituteRelayEyeComponent : Component
{
    public const string BodyContainerId = "institute_relay_body";

    [ViewVariables]
    public Container Bodies = default!;

    /// <summary>
    /// The implant user steering the signal.
    /// </summary>
    [ViewVariables]
    public EntityUid? Rider;

    /// <summary>
    /// Everyone the rider was grabbing or carrying, down the whole chain.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Passengers = new();

    /// <summary>
    /// The implant that started this relay. Cooldown is tracked on it; null for pad relays.
    /// </summary>
    [ViewVariables]
    public EntityUid? Implant;

    [ViewVariables]
    public EntityCoordinates Start;

    /// <summary>
    /// Each body's eye FOV/light settings from before the relay, put back on arrival.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, (bool Fov, bool Light)> SavedEyes = new();

    /// <summary>
    /// Passengers always vomit (if they have a stomach) and have this chance to also take <see cref="PassengerPenalty"/>.
    /// </summary>
    [DataField]
    public float PassengerDamageChance = 0.25f;

    [DataField]
    public DamageSpecifier PassengerPenalty = new() { DamageDict = { ["Heat"] = 15 } };

    [DataField]
    public DamageSpecifier PassengerRobotPenalty = new() { DamageDict = { ["Shock"] = 15 } };

    [DataField]
    public SoundSpecifier OutSound = new SoundPathSpecifier("/Audio/_ReclamationProject/Institute/relay_out.ogg",
        AudioParams.Default.WithMaxDistance(20f));

    [DataField]
    public SoundSpecifier InSound = new SoundPathSpecifier("/Audio/_ReclamationProject/Institute/relay_in.ogg",
        AudioParams.Default.WithMaxDistance(20f));

    [DataField]
    public EntProtoId InEffect = "InstituteRelayInEffect";

    [DataField]
    public EntProtoId OutEffect = "InstituteRelayOutEffect";

    [DataField]
    public EntProtoId ArriveAction = "ActionInstituteRelayArrive";

    [DataField]
    public EntProtoId CancelAction = "ActionInstituteRelayCancel";

    [DataField]
    public EntProtoId UpAction = "ActionInstituteRelayUp";

    [DataField]
    public EntProtoId DownAction = "ActionInstituteRelayDown";

    [ViewVariables]
    public EntityUid? ArriveActionEntity;

    [ViewVariables]
    public EntityUid? CancelActionEntity;

    [ViewVariables]
    public EntityUid? UpActionEntity;

    [ViewVariables]
    public EntityUid? DownActionEntity;
}

/// <summary>
/// Worn gear (the Institute MOD suits) that soaks up all relay damage. Vomiting still happens.
/// </summary>
[RegisterComponent]
public sealed partial class InstituteRelayShieldComponent : Component;

public sealed partial class InstituteRelayStartEvent : InstantActionEvent;

public sealed partial class InstituteRelayArriveEvent : InstantActionEvent;

public sealed partial class InstituteRelayCancelEvent : InstantActionEvent;

public sealed partial class InstituteRelayUpEvent : InstantActionEvent;

public sealed partial class InstituteRelayDownEvent : InstantActionEvent;
