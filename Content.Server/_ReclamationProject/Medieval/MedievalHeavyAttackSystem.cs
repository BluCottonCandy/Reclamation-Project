// #Reclamation Project - medieval weapons: special heavy (wide) swings for the weapons with more than one mode.
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Shared._ReclamationProject.Medieval;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable.Components;

namespace Content.Server._ReclamationProject.Medieval;

public sealed class MedievalHeavyAttackSystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedievalHeavyAttackComponent, MeleeHitEvent>(OnHit);
    }

    private void OnHit(EntityUid uid, MedievalHeavyAttackComponent comp, MeleeHitEvent args)
    {
        // light attacks have no direction; heavy (wide) swings do
        if (args.Direction == null || !args.IsHit || args.HitEntities.Count == 0)
            return;
        if (comp.RequireActive && !_toggle.IsActivated(uid))
            return;

        var wielded = TryComp<WieldableComponent>(uid, out var wield) && wield.Wielded;
        var stacks = wielded && comp.WieldedIgniteStacks > 0 ? comp.WieldedIgniteStacks : comp.IgniteStacks;

        foreach (var target in args.HitEntities)
        {
            if (target == args.User)
                continue;
            ApplyTo(target, args.User, uid, comp, stacks);
        }
    }

    public void ApplyTo(EntityUid target, EntityUid user, EntityUid weapon, MedievalHeavyAttackComponent comp, float stacks)
    {
        if (stacks > 0 && TryComp<FlammableComponent>(target, out var flammable))
        {
            _flammable.AdjustFireStacks(target, stacks, flammable);
            _flammable.Ignite(target, weapon, flammable, user);
        }
        if (comp.Knockdown > 0)
            _stun.TryKnockdown(target, TimeSpan.FromSeconds(comp.Knockdown), true);
        if (comp.Slow > 0)
            _stun.TrySlowdown(target, TimeSpan.FromSeconds(comp.Slow), true, comp.SlowMultiplier, comp.SlowMultiplier);
        if (comp.Bleed > 0)
            _blood.TryModifyBleedAmount(target, comp.Bleed);
    }
}
