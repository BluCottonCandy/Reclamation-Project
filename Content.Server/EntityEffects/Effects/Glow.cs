using Content.Shared.EntityEffects;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class Glow : EntityEffect
{
    [DataField]
    public float Radius = 2f;

    [DataField]
    public Color Color = Color.Black;

    private static readonly List<Color> Colors =
    [
        Color.White,
        Color.Red,
        Color.Yellow,
        Color.Green,
        Color.Blue,
        Color.Purple,
        Color.Pink
    ];

    public override void Effect(EntityEffectBaseArgs args)
    {
        var lightSystem = args.EntityManager.System<SharedPointLightSystem>();
        var light = lightSystem.EnsureLight(args.TargetEntity);
        var color = Color == Color.Black
            ? IoCManager.Resolve<IRobustRandom>().Pick(Colors)
            : Color;

        lightSystem.SetRadius(args.TargetEntity, Radius, light);
        lightSystem.SetColor(args.TargetEntity, color, light);
        lightSystem.SetCastShadows(args.TargetEntity, false, light);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return null;
    }
}
