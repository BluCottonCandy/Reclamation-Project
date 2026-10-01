// #Misfits Add - EntrenchingEvents: DoAfter events used by the entrenching tool system.
// Ported from RMC-14 — no marine-specific fields.
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Entrenching;

/// <summary>
/// Fired after a successful dig operation to produce empty sandbags.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class EntrenchingToolDigDoAfterEvent : SimpleDoAfterEvent { }

/// <summary>
/// Fired after filling an empty sandbag with dirt.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SandbagFillDoAfterEvent : SimpleDoAfterEvent { }

/// <summary>
/// Fired after assembling full sandbags into a barricade.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SandbagBuildDoAfterEvent : SimpleDoAfterEvent { }

/// <summary>
/// Fired after dismantling a sandbag barricade with the entrenching tool.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SandbagDismantleDoAfterEvent : SimpleDoAfterEvent { }
