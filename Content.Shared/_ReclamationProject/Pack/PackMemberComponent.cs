using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Pack;

[RegisterComponent, NetworkedComponent]
public sealed partial class PackMemberComponent : Component
{
    [DataField] public int FollowerLimit = 4;
    [DataField] public bool SpawnHound;
    [DataField] public bool CanTame;
    [DataField] public int TameLimit = 1;
    [DataField] public bool CanTameDeathclaw;
    [DataField] public int StartingArrows;
    public EntityUid? TameAction;
    public readonly HashSet<EntityUid> TamedAnimals = new();
}

[RegisterComponent, NetworkedComponent]
public sealed partial class PackAnimalComponent : Component;

public sealed partial class PackTameActionEvent : EntityTargetActionEvent;

[RegisterComponent, NetworkedComponent]
public sealed partial class PackCompoundBowComponent : Component;

/// <summary>Arrow quivers usable for automatic bow nocking.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BowQuiverComponent : Component;
