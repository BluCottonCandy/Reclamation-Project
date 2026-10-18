using System.Linq;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Traits;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._ReclamationProject.Medical;

/// <summary>Starts with a healed amputation, without leaving severed limbs as free starting loot.</summary>
[UsedImplicitly]
public sealed partial class TraitAmputateLimb : TraitFunction
{
    [DataField(required: true)] public BodyPartType Part;
    [DataField(required: true)] public BodyPartSymmetry Symmetry;

    public override void OnPlayerSpawn(EntityUid uid, IComponentFactory factory,
        IEntityManager entityManager, ISerializationManager serializationManager)
    {
        var body = entityManager.System<SharedBodySystem>();
        var containers = entityManager.System<SharedContainerSystem>();
        foreach (var limb in body.GetBodyChildrenOfType(uid, Part, symmetry: Symmetry).ToArray())
        {
            var removed = body.GetBodyPartChildren(limb.Id).Select(p => p.Id).Append(limb.Id).Distinct().ToArray();
            if (!containers.TryGetContainingContainer((limb.Id, null, null), out var container) ||
                !containers.Remove(limb.Id, container, reparent: false, force: true)) continue;
            // Native removal updates hands, movement and worn appearance, including child parts.
            foreach (var part in removed)
                if (entityManager.EntityExists(part)) entityManager.DeleteEntity(part);
        }
    }
}
