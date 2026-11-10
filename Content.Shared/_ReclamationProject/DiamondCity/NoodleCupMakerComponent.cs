// #Reclamation Project - Diamond City: Takahashi's "Make Noodle Cup" power.
using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.DiamondCity;

/// <summary>
/// Gives the entity an action that turns bottlecaps into a fresh noodle cup.
/// Pays from the persistent cap wallet first, then from caps held in hand.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NoodleCupMakerComponent : Component
{
    /// <summary>Action prototype added on map init.</summary>
    [DataField]
    public EntProtoId ActionId = "ActionTakahashiMakeNoodleCup";

    [DataField, AutoNetworkedField]
    public EntityUid? Action;

    /// <summary>What the action makes.</summary>
    [DataField]
    public EntProtoId Result = "RPFoodNoodleCup";

    /// <summary>Put in the entity's hand when it spawns (job entities get no starting gear).</summary>
    [DataField]
    public EntProtoId? StartingItem;

    /// <summary>Bottlecaps each use costs.</summary>
    [DataField]
    public int Cost = 2;
}

public sealed partial class MakeNoodleCupActionEvent : InstantActionEvent;
