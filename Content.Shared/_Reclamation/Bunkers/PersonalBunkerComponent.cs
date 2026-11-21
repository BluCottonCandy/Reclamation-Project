using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Robust.Shared.Serialization;

namespace Content.Shared._Reclamation.Bunkers;

[RegisterComponent]
public sealed partial class PersonalBunkerEntranceComponent : Component;

[RegisterComponent]
public sealed partial class PersonalBunkerExitComponent : Component;

/// <summary>Persisted with the original item so prototype defaults cannot rearm it on load.</summary>
[RegisterComponent]
public sealed partial class BunkerDisplayWeaponComponent : Component;

/// <summary>A working tool that keeps its normal functions in a saved bunker.</summary>
[RegisterComponent]
public sealed partial class BunkerPreservedToolComponent : Component;

/// <summary>Wildlife and domestic animals eligible for bunker storage while exclusively NPCs.</summary>
[RegisterComponent]
public sealed partial class BunkerAnimalComponent : Component
{
    [DataField] public bool KeepAfterDeath;
    [DataField] public bool WasPlayerControlled;
    // Damageable does not write its live damage dictionary to map saves.
    [DataField] public Dictionary<string, FixedPoint2>? StoredDamage;
    [DataField] public MobState StoredState;
}

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

[RegisterComponent]
public sealed partial class BunkerMaterialLockerComponent : Component;

[Serializable, NetSerializable]
public enum BunkerMaterialLockerUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class OpenBunkerSupplyDrawerMessage : BoundUserInterfaceMessage;
