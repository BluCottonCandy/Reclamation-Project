// #Reclamation Project - Pip-Boy bio-monitor + bullet embedding
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Medical.BioMonitor;

/// <summary>
/// Put on a bullet projectile. When it hits a mob with a body, it has a chance to leave
/// a fragment item stuck in the limb that was hit (bullets themselves are deleted on hit).
/// </summary>
[RegisterComponent]
public sealed partial class BulletEmbedChanceComponent : Component
{
    /// <summary>0..1 chance for this projectile to leave a fragment behind.</summary>
    [DataField]
    public float Chance = 0.2f;

    /// <summary>The item that gets stuck in the limb.</summary>
    [DataField(required: true)]
    public EntProtoId Fragment;
}

/// <summary>
/// A bullet fragment stuck in someone. Deep fragments need a real tool (hemostat) to remove.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BulletFragmentComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Deep;

    /// <summary>Seconds to remove with a tool (bare hands take longer).</summary>
    [DataField]
    public float RemovalTime = 3f;

    /// <summary>Who it's stuck in (it sits in a hidden container inside their body). Null when loose.</summary>
    [ViewVariables]
    public EntityUid? Target;

    [ViewVariables]
    public TargetBodyPart? Part;
}

/// <summary>
/// Given to every spawned player. Holds the "Bio-monitor" action button.
/// </summary>
[RegisterComponent]
public sealed partial class BioMonitorUserComponent : Component
{
    [DataField]
    public EntityUid? Action;
}

/// <summary>
/// "Field Medic" perk: can read their own body without a device and pulls things out as if using tools,
/// the same as INT 10. Gen 3 synths and Coursers get this for free (checked by species in the system).
/// </summary>
[RegisterComponent]
public sealed partial class SelfMedicComponent : Component;

public sealed partial class OpenBioMonitorActionEvent : InstantActionEvent;

/// <summary>
/// Pulling an embedded object out through the bio-monitor.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class BioMonitorRemoveDoAfterEvent : SimpleDoAfterEvent
{
    /// <summary>0..1 chance the wound starts bleeding when it comes out.</summary>
    [DataField]
    public float BleedChance;

    /// <summary>How much bleeding it starts.</summary>
    [DataField]
    public float BleedAmount;
}
