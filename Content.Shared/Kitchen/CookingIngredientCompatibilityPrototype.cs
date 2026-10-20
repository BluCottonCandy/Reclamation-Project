using Robust.Shared.Prototypes;

namespace Content.Shared.Kitchen;

/// <summary>Equivalent cooking ingredients without changing the items or chemicals themselves.</summary>
[Prototype("cookingIngredientCompatibility")]
public sealed partial class CookingIngredientCompatibilityPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public List<HashSet<string>> SolidGroups = new();
    [DataField] public List<HashSet<string>> ReagentGroups = new();
    [DataField] public HashSet<string> ExactOnly = new();
}
