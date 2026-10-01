using Content.Shared.EntityEffects;
using Content.Shared.Physics;
using Content.Shared.Slippery;
using Content.Shared.StepTrigger.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class Slipify : EntityEffect
{
    public override void Effect(EntityEffectBaseArgs args)
    {
        var fixtureSystem = args.EntityManager.System<FixtureSystem>();
        var colWakeSystem = args.EntityManager.System<CollisionWakeSystem>();
        var slippery = args.EntityManager.EnsureComponent<SlipperyComponent>(args.TargetEntity);
        args.EntityManager.Dirty(args.TargetEntity, slippery);
        args.EntityManager.EnsureComponent<StepTriggerComponent>(args.TargetEntity);

        var fixtures = args.EntityManager.EnsureComponent<FixturesComponent>(args.TargetEntity);
        var body = args.EntityManager.EnsureComponent<PhysicsComponent>(args.TargetEntity);
        if (fixtures.Fixtures.TryGetValue("fix1", out var fixture))
            fixtureSystem.TryCreateFixture(args.TargetEntity, fixture.Shape, "slips", 1, false, (int) CollisionGroup.SlipLayer, manager: fixtures, body: body);

        var collisionWake = args.EntityManager.EnsureComponent<CollisionWakeComponent>(args.TargetEntity);
        colWakeSystem.SetEnabled(args.TargetEntity, false, collisionWake);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return null;
    }
}
