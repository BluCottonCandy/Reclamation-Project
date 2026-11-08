using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;

namespace Content.Shared._ReclamationProject.Institute.Nanites;

public sealed class SteadyCrawlSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SteadyCrawlComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<SteadyCrawlComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (_mobState.IsCritical(ent) && _standing.IsDown(ent))
            args.ModifySpeed(ent.Comp.CritCrawlMultiplier, ent.Comp.CritCrawlMultiplier);
    }
}
