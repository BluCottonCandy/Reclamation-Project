using Content.Shared.Body.Components;
using Content.Shared._ReclamationProject.Pack;

namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>Restricts the foraging branch to animals on shared wasteland AI roots.</summary>
public sealed partial class WastelandAnimalPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entities))
            return false;

        return _entities.HasComponent<PackAnimalComponent>(owner)
            || (_entities.TryGetComponent<BodyComponent>(owner, out var body)
                && body.Prototype?.Id is "N14Animal" or "N14PetAnimal");
    }
}
