using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Player;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Server.Movement.Systems;

public sealed class MobCollisionSystem : SharedMobCollisionSystem
{
    private readonly HashSet<EntityUid> _buffered = new();
    private readonly HashSet<EntityUid> _contactCandidates = new();
    private readonly HashSet<EntityUid> _refreshContacts = new();
    private readonly List<EntityUid> _workBuffer = new();
    protected override bool DeferContactTransform => true;

    private EntityQuery<ActorComponent> _actorQuery;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobCollisionComponent, ComponentStartup>(OnMobCollisionStartup);
        SubscribeLocalEvent<MobCollisionComponent, ComponentShutdown>(OnMobCollisionShutdown);
        SubscribeLocalEvent<MobCollisionComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<MobCollisionComponent, EndCollideEvent>(OnEndCollide);
        _actorQuery = GetEntityQuery<ActorComponent>();
        SubscribeLocalEvent<MobCollisionComponent, MobCollisionMessage>(OnServerMobCollision);
    }

    private void OnMobCollisionStartup(EntityUid uid, MobCollisionComponent comp, ComponentStartup args)
    {
        if (comp.Colliding)
            _buffered.Add(uid);
        QueueContactRefresh(uid);
    }

    private void OnMobCollisionShutdown(EntityUid uid, MobCollisionComponent comp, ComponentShutdown args)
    {
        _buffered.Remove(uid);
        _contactCandidates.Remove(uid);
        QueueContactRefresh(uid);
    }

    private void OnStartCollide(Entity<MobCollisionComponent> ent, ref StartCollideEvent args)
    {
        if (!MobQuery.HasComp(args.OtherEntity))
            return;

        // Include stationary/sleeping NPCs: being bumped must work before their AI wakes.
        _contactCandidates.Add(ent.Owner);
        _contactCandidates.Add(args.OtherEntity);
    }

    private void OnEndCollide(Entity<MobCollisionComponent> ent, ref EndCollideEvent args)
    {
        _refreshContacts.Add(ent.Owner);
        _refreshContacts.Add(args.OtherEntity);
    }

    private void QueueContactRefresh(EntityUid uid)
    {
        _refreshContacts.Add(uid);
        // Adding/removing MobCollision while contacts already exist must update both sides.
        if (!TryComp<FixturesComponent>(uid, out var fixtures))
            return;

        var contacts = Physics.GetContacts((uid, fixtures));
        while (contacts.MoveNext(out var contact))
            _refreshContacts.Add(contact.OtherEnt(uid));
    }

    private void RefreshContacts()
    {
        _workBuffer.Clear();
        _workBuffer.AddRange(_refreshContacts);
        _refreshContacts.Clear();
        foreach (var uid in _workBuffer)
        {
            _contactCandidates.Remove(uid);
            if (TerminatingOrDeleted(uid) || !MobQuery.HasComp(uid)
                || !TryComp<FixturesComponent>(uid, out var fixtures))
                continue;

            var contacts = Physics.GetContacts((uid, fixtures));
            while (contacts.MoveNext(out var contact))
            {
                if (!contact.IsTouching || !MobQuery.HasComp(contact.OtherEnt(uid)))
                    continue;

                // Don't filter by FixtureId: it can be changed at runtime. HandleCollisions
                // still performs the original fixture, posture, mount and velocity checks.
                _contactCandidates.Add(uid);
                break;
            }
        }
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

        RefreshContacts();
        _workBuffer.Clear();
        _workBuffer.AddRange(_contactCandidates);
        foreach (var uid in _workBuffer)
        {
            if (TerminatingOrDeleted(uid) || _actorQuery.HasComp(uid) || MetaData(uid).EntityPaused
                || !MobQuery.TryComp(uid, out var comp) || !PhysicsQuery.TryComp(uid, out var physics))
                continue;

            HandleCollisions((uid, comp, physics), frameTime);
        }

        // Updating the collision buffer can remove entries; reuse a snapshot without
        // allocating a new array every tick.
        _workBuffer.Clear();
        _workBuffer.AddRange(_buffered);
        foreach (var uid in _workBuffer)
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
