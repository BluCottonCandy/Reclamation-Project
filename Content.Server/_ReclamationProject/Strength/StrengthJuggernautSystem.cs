using System.Numerics;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._ReclamationProject.Strength;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;
namespace Content.Server._ReclamationProject.Strength;

public sealed class StrengthJuggernautSystem : EntitySystem
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan ExhaustionStun = TimeSpan.FromSeconds(4);
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedStrengthJuggernautSystem _ability = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
        SubscribeLocalEvent<StrengthJuggernautComponent, StrengthJuggernautActionEvent>(OnActivate);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args) => RefreshAction(args.ChangedEntity);
    private void OnStatsReady(ref SpecialStatsReadyEvent args) => RefreshAction(args.Entity);
    private void OnSpecialShutdown(ref SpecialShutdownEvent args) => RemoveAction(args.Entity);

    private void RefreshAction(EntityUid uid)
    {
        if (_special.GetEffective(uid, SpecialStat.Strength) < 10)
        {
            RemoveAction(uid);
            return;
        }

        var ability = EnsureComp<StrengthJuggernautComponent>(uid);
        if (ability.Action != null && !Deleted(ability.Action.Value))
            return;

        _actions.AddAction(uid, ref ability.Action, "ActionStrengthJuggernaut");
        if (ability.NextUseAt > _timing.CurTime)
            _actions.SetCooldown(ability.Action, ability.NextUseAt - Cooldown, ability.NextUseAt);
        Dirty(uid, ability);
    }

    private void RemoveAction(EntityUid uid)
    {
        if (!TryComp<StrengthJuggernautComponent>(uid, out var ability) || ability.Action == null)
            return;
        _actions.RemoveAction(uid, ability.Action);
        if (!Deleted(ability.Action.Value))
            QueueDel(ability.Action.Value);
        ability.Action = null;
        // Keep the cooldown even if SPECIAL is removed or temporarily drops below 10.
        Dirty(uid, ability);
    }

    private void OnActivate(Entity<StrengthJuggernautComponent> ent, ref StrengthJuggernautActionEvent args)
    {
        var now = _timing.CurTime;
        if (args.Handled || args.Performer != ent.Owner ||
            _special.GetEffective(ent, SpecialStat.Strength) < 10 || !_mob.IsAlive(ent) ||
            !_blocker.CanInteract(ent, null) || ent.Comp.NextUseAt > now || _ability.IsActive(ent))
            return;

        ent.Comp.EndsAt = now + Duration;
        ent.Comp.NextUseAt = now + Cooldown;
        _actions.SetCooldown(ent.Comp.Action, now, ent.Comp.NextUseAt);
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("strength-juggernaut-start"), ent, ent);
        args.Handled = true;
    }

    public void ApplyHitEffects(MeleeHitEvent args)
    {
        if (!args.IsHit || args.Handled || !_ability.IsActive(args.User))
            return;

        var from = _transform.GetMapCoordinates(args.User);
        foreach (var target in args.HitEntities)
        {
            if (target == args.User || !HasComp<MobStateComponent>(target) || _mob.IsDead(target) || Transform(target).Anchored)
                continue;
            var to = _transform.GetMapCoordinates(target);
            if (from.MapId != to.MapId)
                continue;

            _stun.TryKnockdown(target, TimeSpan.FromSeconds(1), refresh: true);
            var direction = to.Position - from.Position;
            if (direction.LengthSquared() < 0.0001f)
                direction = _transform.GetWorldRotation(args.User).ToVec();
            _throwing.TryThrow(target, Vector2.Normalize(direction) * 2f, baseThrowSpeed: 4f,
                user: args.User, pushbackRatio: 0f, recoil: false, animated: false, playSound: false, doSpin: false);
        }
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<StrengthJuggernautComponent>();
        while (query.MoveNext(out var uid, out var ability))
        {
            if (Paused(uid) || ability.EndsAt == TimeSpan.Zero || ability.EndsAt > _timing.CurTime)
                continue;
            ability.EndsAt = TimeSpan.Zero;
            Dirty(uid, ability);
            _stun.TryStun(uid, ExhaustionStun, refresh: true);
            _popup.PopupEntity(Loc.GetString("strength-juggernaut-end"), uid, uid);
        }
    }
}
