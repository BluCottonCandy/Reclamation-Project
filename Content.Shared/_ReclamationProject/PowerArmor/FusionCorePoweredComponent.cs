using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.PowerArmor;

/// <summary>
/// Power armour power systems that run on a fusion core. While the core has charge the piece's
/// speed boost works; with an empty or missing core it's cancelled and the wearer slows to a crawl.
/// The wearer can switch the suit on and off from inside (off saves the core but crawls); only
/// someone outside the suit can put a core in or take it out.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class FusionCorePoweredComponent : Component
{
    [DataField]
    public string SlotId = "fusion_core";

    /// <summary>
    /// Charge drained per second while worn. 5000 / 1800 = a full core lasts 30 minutes.
    /// </summary>
    [DataField]
    public float DrainPerSecond = 5000f / 1800f;

    /// <summary>
    /// Extra speed multiplier on top of losing the power systems' boost when unpowered.
    /// Standard power armour is x0.2 on its own, so 0.2 here = 4% of normal speed.
    /// </summary>
    [DataField]
    public float UnpoweredSpeedMultiplier = 0.2f;

    /// <summary>
    /// Used instead of <see cref="UnpoweredSpeedMultiplier"/> for wearers with at least
    /// <see cref="StrongMinimumStrength"/> Strength. 0.25 = 5% of normal speed.
    /// </summary>
    [DataField]
    public float StrongUnpoweredSpeedMultiplier = 0.25f;

    [DataField]
    public int StrongMinimumStrength = 8;

    /// <summary>
    /// How long someone else takes to put a core into, or pull one out of, a suit you're wearing.
    /// </summary>
    [DataField]
    public TimeSpan WearerCoreDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The wearer's power switch. Off = no drain, but the suit crawls as if the core were dead.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool SwitchedOn = true;

    /// <summary>
    /// Switched on AND the core has charge.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Powered = true;

    [DataField]
    public EntProtoId PowerAction = "ActionFusionCorePower";

    [DataField, AutoNetworkedField]
    public EntityUid? PowerActionEntity;

    [ViewVariables]
    public TimeSpan NextDrain;
}

public sealed partial class FusionCorePowerToggleEvent : InstantActionEvent;

/// <summary>
/// Someone else finished pulling the fusion core out of a suit another player is wearing.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class FusionCoreRemoveDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Someone else finished putting a fusion core into a suit another player is wearing.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class FusionCoreInstallDoAfterEvent : SimpleDoAfterEvent;
