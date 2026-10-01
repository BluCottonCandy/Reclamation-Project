using Content.Server.Botany.Components;
using Content.Shared.Atmos;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class PlantMutateConsumeGasses : EntityEffect
{
    [DataField]
    public float MinValue = 0.01f;

    [DataField]
    public float MaxValue = 0.5f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        MutateGasses(args, true);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return null;
    }

    private void MutateGasses(EntityEffectBaseArgs args, bool consume)
    {
        var random = IoCManager.Resolve<IRobustRandom>();
        var plantHolder = args.EntityManager.GetComponent<PlantHolderComponent>(args.TargetEntity);
        if (plantHolder.Seed == null)
            return;

        var gasses = consume ? plantHolder.Seed.ConsumeGasses : plantHolder.Seed.ExudeGasses;
        var amount = random.NextFloat(MinValue, MaxValue);
        var gas = random.Pick(Enum.GetValues(typeof(Gas)).Cast<Gas>().ToList());

        if (gasses.ContainsKey(gas))
            gasses[gas] += amount;
        else
            gasses.Add(gas, amount);
    }
}

public sealed partial class PlantMutateExudeGasses : EntityEffect
{
    [DataField]
    public float MinValue = 0.01f;

    [DataField]
    public float MaxValue = 0.5f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var random = IoCManager.Resolve<IRobustRandom>();
        var plantHolder = args.EntityManager.GetComponent<PlantHolderComponent>(args.TargetEntity);
        if (plantHolder.Seed == null)
            return;

        var gasses = plantHolder.Seed.ExudeGasses;
        var amount = random.NextFloat(MinValue, MaxValue);
        var gas = random.Pick(Enum.GetValues(typeof(Gas)).Cast<Gas>().ToList());

        if (gasses.ContainsKey(gas))
            gasses[gas] += amount;
        else
            gasses.Add(gas, amount);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return null;
    }
}
