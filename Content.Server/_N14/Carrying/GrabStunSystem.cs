using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Stunnable;

namespace Content.Server._N14.Carrying;

/// <summary>
/// Stuns pulled entities when the puller has <see cref="GrabStunComponent"/>.
/// Carry (grab) stun is handled directly in <see cref="Content.Server.Carrying.CarryingSystem"/>.
/// </summary>
public sealed class GrabStunSystem : EntitySystem
{
    [Dependency] private readonly SharedStunSystem _stun = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GrabStunComponent, PullStartedMessage>(OnPullStarted);
    }

    private void OnPullStarted(EntityUid uid, GrabStunComponent component, PullStartedMessage args)
    {
        // Only stun if we are the puller, not the pulled.
        if (args.PullerUid != uid)
            return;

        _stun.TryStun(args.PulledUid, component.StunTime, refresh: true);
    }
}
