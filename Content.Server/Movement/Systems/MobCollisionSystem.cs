using System.Numerics;
using System.Linq;
using Content.Shared.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Player;

namespace Content.Server.Movement.Systems;

public sealed class MobCollisionSystem : SharedMobCollisionSystem
{
    private readonly HashSet<EntityUid> _buffered = new();
    protected override bool DeferContactTransform => true;

    private EntityQuery<ActorComponent> _actorQuery;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobCollisionComponent, ComponentStartup>(OnMobCollisionStartup);
        SubscribeLocalEvent<MobCollisionComponent, ComponentShutdown>(OnMobCollisionShutdown);
        _actorQuery = GetEntityQuery<ActorComponent>();
        SubscribeLocalEvent<MobCollisionComponent, MobCollisionMessage>(OnServerMobCollision);
    }

    private void OnMobCollisionStartup(EntityUid uid, MobCollisionComponent comp, ComponentStartup args)
    {
        if (comp.Colliding)
            _buffered.Add(uid);
    }

    private void OnMobCollisionShutdown(EntityUid uid, MobCollisionComponent comp, ComponentShutdown args)
    {
        _buffered.Remove(uid);
    }

    protected override void OnCollisionBufferChanged(EntityUid uid, bool colliding)
    {
        if (colliding)
            _buffered.Add(uid);
        else
            _buffered.Remove(uid);
    }

    private void OnServerMobCollision(Entity<MobCollisionComponent> ent, ref MobCollisionMessage args)
    {
        MoveMob((ent.Owner, ent.Comp, Transform(ent.Owner)), args.Direction, args.SpeedModifier);
    }

    public override void Update(float frameTime)
    {
        if (!CfgManager.GetCVar(CCVars.MovementMobPushing))
            return;

        var query = EntityQueryEnumerator<MobCollisionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_actorQuery.HasComp(uid) || !PhysicsQuery.TryComp(uid, out var physics))
                continue;

            HandleCollisions((uid, comp, physics), frameTime);
        }

        foreach (var uid in _buffered.ToArray())
        {
            if (MobQuery.TryComp(uid, out var comp) && comp.Colliding)
                UpdateCollisionBuffer(uid, comp, frameTime);
        }
    }

    protected override void RaiseCollisionEvent(EntityUid uid, Vector2 direction, float speedMod)
    {
        RaiseLocalEvent(uid, new MobCollisionMessage()
        {
            Direction = direction,
            SpeedModifier = speedMod,
        });
    }
}
