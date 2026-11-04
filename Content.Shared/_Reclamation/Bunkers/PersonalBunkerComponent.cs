using Robust.Shared.Serialization;

namespace Content.Shared._Reclamation.Bunkers;

[RegisterComponent]
public sealed partial class PersonalBunkerEntranceComponent : Component;

[RegisterComponent]
public sealed partial class PersonalBunkerExitComponent : Component;

/// <summary>Persisted with the original item so prototype defaults cannot rearm it on load.</summary>
[RegisterComponent]
public sealed partial class BunkerDisplayWeaponComponent : Component;

/// <summary>Persisted so loading the original clothing prototype cannot restore armor protection.</summary>
[RegisterComponent]
public sealed partial class BunkerDisplayArmorComponent : Component;

[Serializable, NetSerializable]
public enum PersonalBunkerUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class PersonalBunkerChoiceMessage(bool room) : BoundUserInterfaceMessage
{
    public bool Room = room;
}

/// <summary>Retains decorative identity and prevents prototype defaults restoring utility.</summary>
[RegisterComponent]
public sealed partial class BunkerDisplayItemComponent : Component
{
    [DataField] public string OriginalName = string.Empty;
    [DataField] public bool Ammo;
    [DataField] public bool Id;
}
