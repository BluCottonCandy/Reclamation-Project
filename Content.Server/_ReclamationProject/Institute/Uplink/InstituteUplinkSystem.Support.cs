using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Station.Systems;
using Content.Server.Store.Components;
using Content.Server.Store.Systems;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
using Content.Shared.Store.Components;
using Robust.Server.GameObjects;

namespace Content.Server._ReclamationProject.Institute.Uplink;

// #Reclamation Project - bought reinforcements (ghost roles) and synth grenades (temporary AI synths).
public sealed partial class InstituteUplinkSystem
{
    [Dependency] private readonly GhostRoleSystem _ghostRoles = default!;
    [Dependency] private readonly StationSpawningSystem _spawning = default!;
    [Dependency] private readonly TransformSystem _xform = default!;

    private void InitializeSupport()
    {
        SubscribeLocalEvent<InstituteReinforcementBeaconComponent, MapInitEvent>(OnBeaconInit);
        SubscribeLocalEvent<InstituteReinforcementBeaconComponent, TakeGhostRoleEvent>(OnBeaconTaken);
        SubscribeLocalEvent<InstituteSynthDropComponent, TriggerEvent>(OnSynthDrop);
        SubscribeLocalEvent<InstituteTemporarySynthComponent, MobStateChangedEvent>(OnSynthState);
    }

    private void UpdateSupport()
    {
        var now = _timing.CurTime;

        var beacons = EntityQueryEnumerator<InstituteReinforcementBeaconComponent>();
        while (beacons.MoveNext(out var uid, out var beacon))
        {
            if (now >= beacon.RefundAt)
                RefundBeacon(uid, beacon);
        }

        var synths = EntityQueryEnumerator<InstituteTemporarySynthComponent>();
        while (synths.MoveNext(out var uid, out var synth))
        {
            if (now >= synth.DespawnAt)
                Poof(uid);
        }
    }

    #region Reinforcements

    private void OnBeaconInit(Entity<InstituteReinforcementBeaconComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.RefundAt = _timing.CurTime + ent.Comp.Timeout;
    }

    private void RefundBeacon(EntityUid uid, InstituteReinforcementBeaconComponent beacon)
    {
        // StoreSystem adds StoreRefundComponent to every product it hands out.
        if (TryComp<StoreRefundComponent>(uid, out var refund) &&
            refund.StoreEntity is { } storeUid &&
            TryComp<StoreComponent>(storeUid, out var store))
        {
            AddPoints(storeUid, store, beacon.Refund);
            if (TryComp<InstituteUplinkComponent>(storeUid, out var uplink) && uplink.OwnerMind is { } owner)
                Message(owner, Loc.GetString("institute-reinforcement-refunded", ("amount", beacon.Refund)));
        }

        Spawn(RelayOut, Transform(uid).Coordinates);
        QueueDel(uid);
    }

    private void OnBeaconTaken(Entity<InstituteReinforcementBeaconComponent> ent, ref TakeGhostRoleEvent args)
    {
        if (args.Cancelled || !TryComp<GhostRoleComponent>(ent, out var ghostRole) || ghostRole.Taken)
        {
            args.TookRole = false;
            return;
        }

        var coords = Transform(ent).Coordinates;
        var profile = HumanoidCharacterProfile.RandomWithSpecies(ent.Comp.Species);
        var mob = _spawning.SpawnPlayerMob(coords, new JobComponent { Prototype = ent.Comp.Job }, profile, null);
        _xform.AttachToGridOrMap(mob);

        _ghostRoles.GhostRoleInternalCreateMindAndTransfer(args.Player, ent, mob, ghostRole);

        if (_minds.TryGetMind(mob, out var mindId, out var mind))
        {
            _roles.MindAddRole(mindId, new JobComponent { Prototype = ent.Comp.Job }, mind, true);

            // Reinforcements arrive with an empty wallet, so buying them can't print points.
            if (FindImplant(mob) is { } implant && TryComp<StoreComponent>(implant, out var store))
            {
                store.Balance[Currency] = 0;
                RegisterMember(mindId, mind, implant, ent.Comp.Job);
            }
        }

        // RemComp below stops a second taker; the registry entry goes with the beacon.
        _ghostRoles.UnregisterGhostRole((ent, ghostRole));
        RemComp<InstituteReinforcementBeaconComponent>(ent);
        Spawn(RelayIn, coords);
        QueueDel(ent);
        args.TookRole = true;
    }

    #endregion

    #region Synth grenades

    private void OnSynthDrop(Entity<InstituteSynthDropComponent> ent, ref TriggerEvent args)
    {
        var coords = _xform.GetMapCoordinates(ent);
        for (var i = 0; i < ent.Comp.Count; i++)
        {
            var offset = new Vector2(_random.NextFloat(-0.6f, 0.6f), _random.NextFloat(-0.6f, 0.6f));
            var spot = coords.Offset(offset);
            var synth = Spawn(ent.Comp.Synth, spot);
            EnsureComp<InstituteTemporarySynthComponent>(synth).DespawnAt = _timing.CurTime + ent.Comp.Lifetime;
            Spawn(RelayIn, spot);
        }

        QueueDel(ent);
        args.Handled = true;
    }

    private void OnSynthState(Entity<InstituteTemporarySynthComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Poof(ent);
    }

    /// <summary>Relays a temporary synth out. Its gear goes with it, so nothing is left to loot.</summary>
    private void Poof(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;

        Spawn(RelayOut, Transform(uid).Coordinates);
        RemComp<InstituteTemporarySynthComponent>(uid);
        QueueDel(uid);
    }

    #endregion
}
