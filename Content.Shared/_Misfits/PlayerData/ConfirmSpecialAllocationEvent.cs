// #Misfits Add - Client → server message to lock in a player's SPECIAL allocation.
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.PlayerData;

/// <summary>
/// Sent by the client when a player confirms their S.P.E.C.I.A.L. allocation.
/// The server validates totals (each stat 1-10, seven stats at 5 plus five extra points) then locks them in.
/// </summary>
[Serializable, NetSerializable]
public sealed class ConfirmSpecialAllocationEvent : EntityEventArgs
{
    public int Strength;
    public int Perception;
    public int Endurance;
    public int Charisma;
    public int Intelligence;
    public int Agility;
    public int Luck;
}
