using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Forged;

[Serializable, NetSerializable]
public sealed partial class ForgedBrandingDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ForgedBrandRemovalDoAfterEvent : SimpleDoAfterEvent;
