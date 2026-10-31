using Robust.Shared.Serialization;

namespace Content.Shared._Reclamation.Bunkers;

[RegisterComponent]
public sealed partial class PersonalBunkerEntranceComponent : Component;

[RegisterComponent]
public sealed partial class PersonalBunkerExitComponent : Component;

/// <summary>Persisted with the original item so prototype defaults cannot rearm it on load.</summary>
[RegisterComponent]
public sealed partial class BunkerDisplayWeaponComponent : Component;

[Serializable, NetSerializable]
public enum PersonalBunkerUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class PersonalBunkerChoiceMessage(bool room) : BoundUserInterfaceMessage
{
    public bool Room = room;
}
