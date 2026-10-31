using System.Numerics;
using Content.Shared._Misfits.MeleeCharge;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Mobs.Systems;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Shared._ReclamationProject.Agility;

/// <summary>Predict the complete dash and cooldown on both sides, just like ordinary movement.</summary>
public sealed class AgilityQuickstepMovementSystem : EntitySystem
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    public const float Range = 5f;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly MeleeChargeSystem _dash = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AgilityQuickstepComponent, AgilityQuickstepActionEvent>(OnActivate);
    }

    private void OnActivate(Entity<AgilityQuickstepComponent> ent, ref AgilityQuickstepActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner || ent.Comp.NextUseAt > _timing.CurTime ||
            _special.GetEffective(ent, SpecialStat.Agility) < 10 || !_mob.IsAlive(ent) ||
            !_blocker.CanMove(ent) || !_blocker.CanInteract(ent, null) || Transform(ent).Anchored ||
            HasComp<MeleeChargeComponent>(ent) || HasComp<ThrownItemComponent>(ent) ||
            !TryComp<PhysicsComponent>(ent, out var body))
            return;

        var start = _transform.GetMapCoordinates(ent);
        var direction = body.LinearVelocity;
        if (direction.LengthSquared() < 0.01f)
            direction = _transform.GetWorldRotation(ent).ToWorldVec();
        var target = new MapCoordinates(start.Position + Vector2.Normalize(direction) * Range, start.MapId);
        // Reuse the existing physical dash. No ActionStaminaCost, teleport, attack or invulnerability.
        _dash.PerformDash(ent, _transform.ToCoordinates(target), speed: 14f, maxDistance: Range);
        if (!HasComp<ThrownItemComponent>(ent))
        {
            RemCompDeferred<MeleeChargeComponent>(ent);
            return;
        }
        ent.Comp.NextUseAt = _timing.CurTime + Cooldown;
        _actions.SetCooldown(ent.Comp.Action, _timing.CurTime, ent.Comp.NextUseAt);
        Dirty(ent);
        args.Handled = true;
    }
}
