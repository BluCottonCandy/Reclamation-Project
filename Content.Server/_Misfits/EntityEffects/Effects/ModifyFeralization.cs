using Content.Server._Misfits.Ghoul;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;


namespace Content.Server._Misfits.EntityEffects.Effects;

/// <summary>
/// Effect that is used to lessen or intensify a ghoul's feral status.
/// </summary>
public sealed partial class ModifyFeralization : EntityEffect
{
    /// <summary>
    /// The change to feral status per metabolization tick
    /// </summary>
    [DataField(required: true)]
    public float Delta;

    /// <summary>
    /// The minimum feral threshold at which the effect will work
    /// </summary>
    [DataField]
    public float MinimumThreshold = 0f;
    
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        // _Misfits/ghoulify.ftl
        return Loc.GetString(
            "reagent-effect-guidebook-modify-feralization",
            ("chance", Probability), ("delta", Delta), ("threshold", MinimumThreshold));
    }
    
    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent<FeralGhoulifyOverTimeComponent>(args.TargetEntity, out var comp))
            return;
        
        if (comp.CurrentFeral < MinimumThreshold)
            return;
        
        comp.CurrentFeral += Delta;
        
        if (comp.CurrentFeral < MinimumThreshold)
            comp.CurrentFeral = MinimumThreshold;
    }
}
