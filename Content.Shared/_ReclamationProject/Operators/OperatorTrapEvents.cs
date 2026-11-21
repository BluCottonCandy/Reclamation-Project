using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Operators;

public sealed partial class OperatorThrowCaltropsEvent : WorldTargetActionEvent;
public sealed partial class OperatorBuildShotgunEvent : WorldTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class OperatorFinishShotgunEvent : SimpleDoAfterEvent;

public sealed partial class OperatorPlaceMineEvent : WorldTargetActionEvent;
