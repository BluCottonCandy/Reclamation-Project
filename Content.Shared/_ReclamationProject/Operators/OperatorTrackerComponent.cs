using Content.Shared.DoAfter;
using Content.Shared.Actions;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Operators;

[RegisterComponent]
public sealed partial class OperatorTrackerComponent : Component;

public sealed partial class OperatorCreateContractEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed class OperatorTrailEvent(NetCoordinates[] steps) : EntityEventArgs
{
    public readonly NetCoordinates[] Steps = steps;
}

[Serializable, NetSerializable]
public sealed partial class OperatorSampleDoAfter : SimpleDoAfterEvent;

[Robust.Shared.Serialization.NetSerializable, System.Serializable]
public enum OperatorContractVisuals : byte
{
    Stage
}

[Robust.Shared.Serialization.NetSerializable, System.Serializable]
public enum OperatorContractStage : byte
{
    Written,
    Accepted,
    Paid
}
