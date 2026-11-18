using System.Diagnostics.CodeAnalysis;
using Content.Server.Nutrition.Components;
using Content.Shared._ReclamationProject.Pack;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Pack;

public sealed class PackDigestionSystem : EntitySystem
{
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly IComponentFactory _components = default!;

    private static readonly ProtoId<TagPrototype> Raw = "Raw";
    private static readonly ProtoId<TagPrototype> Meat = "Meat";
    private static readonly ProtoId<TagPrototype> Cooked = "Cooked";

    public bool TryGetRawMeatRecipe(EntityUid eater, Entity<FoodComponent> food, [NotNullWhen(true)] out Solution? recipe)
    {
        recipe = null;
        if (!HasComp<PackMemberComponent>(eater) || _tags.HasTag(food, Cooked) ||
            MetaData(food).EntityPrototype is not { } prototype ||
            !prototype.TryGetComponent<SolutionContainerManagerComponent>(out var containers, _components) ||
            containers.Solutions is not { } solutions || !solutions.TryGetValue(food.Comp.Solution, out var ingredients))
            return false;

        // Wasteland cutlets and edible organs often carry raw protein without both food tags.
        // Consult the original recipe so spiking another kind of food cannot grant it this diet.
        if (!ingredients.ContainsReagent("UncookedAnimalProteins", null) &&
            !ingredients.ContainsReagent("N14UncookedAnimalProteinsRad", null) &&
            !(_tags.HasTag(food, Raw) && _tags.HasTag(food, Meat)))
            return false;

        recipe = ingredients;
        return true;
    }

    public void DigestRawMeat(Solution bite, Solution recipe)
    {
        var nutriment = FixedPoint2.Zero;
        foreach (var (id, _) in recipe)
        {
            // Keep normal nutrients and harmless dragon ichor. Convert other native ingredients
            // only in this bite; unrelated substances mixed into the meat stay in it.
            if (id.Prototype.Id is "Nutriment" or "Protein" or "Vitamin" or "Fat" or "Water" or "Ichor")
                continue;

            nutriment += bite.RemoveReagent(id, bite.Volume);
        }

        bite.AddReagent("Nutriment", nutriment);
    }
}
