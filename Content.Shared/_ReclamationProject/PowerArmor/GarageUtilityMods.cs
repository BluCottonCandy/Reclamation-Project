using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._ReclamationProject.PowerArmor;

public sealed partial class TargetGarageModEvent : EntityTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class JumpStartDoAfterEvent : DoAfterEvent
{
    [DataField] public NetEntity SourceCore;
    [DataField] public NetEntity ReceivingCore;
    public override DoAfterEvent Clone() => new JumpStartDoAfterEvent { SourceCore = SourceCore, ReceivingCore = ReceivingCore };
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ArmorMagLockComponent : Component;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArmorTowComponent : Component
{
    [DataField, AutoNetworkedField] public EntityUid Target;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class JunkhoundPulseComponent : Component
{
    public const float Range = 6f;
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(10);
    public override bool SendOnlyToOwner => true;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField] public TimeSpan Until;
    [DataField, AutoNetworkedField] public List<EntityUid> Hits = new();
}
