using Robust.Shared.GameObjects;

namespace Content.Shared._ReclamationProject.Nutrition;

/// <summary>
/// Explicit nutrition policy for NPC prototypes. Player species are not opted in.
/// Pets override the hostile base policy; PackAnimal also enables animal needs.
/// </summary>
[RegisterComponent]
public sealed partial class NpcNutritionComponent : Component
{
    [DataField]
    public bool NeedsFood;
}
