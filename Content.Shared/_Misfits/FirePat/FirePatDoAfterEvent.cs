// #Misfits Add - Fire Pat DoAfter event.
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.FirePat;

/// <summary>
/// Raised on the patter when the patting do-after completes or is cancelled.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class FirePatDoAfterEvent : SimpleDoAfterEvent
{
}
