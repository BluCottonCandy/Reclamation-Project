using Content.Shared._ReclamationProject.Pack;
using Content.Shared.Traits;
using JetBrains.Annotations;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._ReclamationProject.Pack;

[UsedImplicitly]
public sealed partial class TraitPackWarhound : TraitFunction
{
    public override void OnPlayerSpawn(EntityUid uid, IComponentFactory factory,
        IEntityManager entityManager, ISerializationManager serializationManager)
    {
        if (entityManager.TryGetComponent<PackMemberComponent>(uid, out var member))
            EntitySystem.Get<PackTamingSystem>().TrySpawnHound((uid, member));
    }
}
