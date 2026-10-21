using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Content.Shared.Maps;
using Robust.Shared.Map;

namespace Content.Shared._ReclamationProject.Roofs;

[DataDefinition]
public sealed partial class RoofFloorCondition : IConstructionCondition
{
    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var tile = location.GetTileRef();
        if (tile == null || tile.Value.IsSpace() || tile.Value.GetContentTileDefinition().IsSubFloor)
            return false;

        var entities = IoCManager.Resolve<IEntityManager>();
        var roofs = new HashSet<Entity<ConstructedRoofComponent>>();
        entities.System<EntityLookupSystem>().GetLocalEntitiesIntersecting(tile.Value.GridUid, tile.Value.GridIndices, roofs);
        return roofs.Count == 0;
    }

    public ConstructionGuideEntry GenerateGuideEntry() => new()
    {
        Localization = "construction-roof-needs-floor"
    };
}
