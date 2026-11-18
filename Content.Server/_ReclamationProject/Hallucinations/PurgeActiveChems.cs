using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Hallucinations;

public sealed partial class PurgeActiveChems : EntityEffect
{
    protected override string ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-purge-active-chems");

    public override void Effect(EntityEffectBaseArgs args)
        => args.EntityManager.System<ChemPurgeSystem>().Purge(args.TargetEntity);
}
