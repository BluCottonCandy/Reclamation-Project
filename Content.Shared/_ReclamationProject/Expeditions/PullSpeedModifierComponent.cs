using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Expeditions;

/// <summary>Replaces the normal pulling penalty while this entity is dragged.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PullSpeedModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    public float WalkModifier = 1f;

    [DataField, AutoNetworkedField]
    public float SprintModifier = 1f;
}
