using Content.Shared.Damage;
using Robust.Shared.Prototypes;
using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Pack;

[RegisterComponent, NetworkedComponent]
public sealed partial class PackMemberComponent : Component
{
    [DataField] public List<EntProtoId> StartingAnimals = new();
    [DataField] public bool CanTame;
    [DataField] public bool CanCommandDeathclaw;
    [DataField] public bool CanCommandKing;
    [DataField] public int StartingArrows;
    public EntityUid? TameAction;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class PackAnimalComponent : Component
{
    [DataField] public DamageModifierSet? ArmorModifiers;
    [DataField] public bool PowerBite;
    [DataField] public bool Knockback;
    public TimeSpan NextPowerBite;
}

public sealed partial class PackTameActionEvent : EntityTargetActionEvent;

[RegisterComponent, NetworkedComponent]
public sealed partial class PackCompoundBowComponent : Component;

/// <summary>Arrow quivers usable for automatic bow nocking.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BowQuiverComponent : Component;

[RegisterComponent]
public sealed partial class PackAnimalArmorComponent : Component;
