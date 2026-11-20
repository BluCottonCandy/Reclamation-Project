using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.PowerArmor;

[RegisterComponent]
public sealed partial class RustbucketEngineComponent : Component
{
    [DataField] public EntityUid? Action;
    [DataField] public DoAfterId? Rev;
    [DataField] public EntityUid? Audio;
}

[Serializable, NetSerializable]
public sealed partial class RevEngineDoAfterEvent : SimpleDoAfterEvent;

public sealed partial class RevRustbucketEvent : InstantActionEvent;
