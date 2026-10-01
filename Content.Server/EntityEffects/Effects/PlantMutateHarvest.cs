using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class PlantMutateHarvest : EntityEffect
{
    public override void Effect(EntityEffectBaseArgs args)
    {
        var plantHolder = args.EntityManager.GetComponent<PlantHolderComponent>(args.TargetEntity);
        if (plantHolder.Seed == null)
            return;

        if (plantHolder.Seed.HarvestRepeat == HarvestType.NoRepeat)
            plantHolder.Seed.HarvestRepeat = HarvestType.Repeat;
        else if (plantHolder.Seed.HarvestRepeat == HarvestType.Repeat)
            plantHolder.Seed.HarvestRepeat = HarvestType.SelfHarvest;
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return null;
    }
}
