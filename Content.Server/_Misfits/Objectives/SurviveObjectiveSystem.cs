// #Misfits Add - Sets the localized title/description for the vanilla SurviveCondition objective.
using Content.Server.Objectives.Components;
using Content.Shared.Objectives.Components;

namespace Content.Server._Misfits.Objectives;

/// <summary>
/// Sets the localized name/description for the SurviveObjective at assignment time.
/// Progress evaluation is handled by the vanilla <see cref="SurviveConditionSystem"/>.
/// </summary>
public sealed class SurviveObjectiveSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SurviveConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
    }

    private void OnAfterAssign(EntityUid uid, SurviveConditionComponent comp, ref ObjectiveAfterAssignEvent args)
    {
        _metaData.SetEntityName(uid, Loc.GetString("objective-condition-survive-title"), args.Meta);
        _metaData.SetEntityDescription(uid, Loc.GetString("objective-condition-survive-description"), args.Meta);
    }
}
