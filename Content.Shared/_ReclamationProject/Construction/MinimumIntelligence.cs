using Content.Shared._Misfits.Special;
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Robust.Shared.Map;

namespace Content.Shared._ReclamationProject.Construction;

[DataDefinition]
public sealed partial class MinimumIntelligence : IConstructionCondition
{
    [DataField(required: true)]
    public int Minimum;

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        return IoCManager.Resolve<IEntityManager>().System<SharedSpecialSystem>()
            .HasRequirement(user, SpecialStat.Intelligence, Minimum);
    }

    public ConstructionGuideEntry GenerateGuideEntry() => new()
    {
        Localization = "construction-condition-minimum-intelligence",
        Arguments = new (string, object)[] { ("minimum", Minimum) },
    };
}
