// #Misfits Add - DoAfter event for landmine disarm interaction
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.LandMines;

[Serializable, NetSerializable]
public sealed partial class LandMineDisarmDoAfterEvent : SimpleDoAfterEvent
{
}
