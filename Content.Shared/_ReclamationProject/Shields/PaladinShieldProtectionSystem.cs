using System.Linq;
using Content.Shared.Damage;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Blocking;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;

namespace Content.Shared._ReclamationProject.Shields;

public sealed class PaladinShieldProtectionSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PaladinShieldComponent, BeforeBlockingEvent>(OnBeforeBlocking);
        SubscribeLocalEvent<HandsComponent, DamageModifyEvent>(OnDamage);
        SubscribeLocalEvent<HandsComponent, BeforeStaminaDamageEvent>(OnStamina);
    }

    public bool IsFunctional(EntityUid uid, PaladinShieldComponent shield)
    {
        return !shield.Damaged && TryComp<DamageableComponent>(uid, out var damage) &&
               TryComp<PowerArmorIntegrityComponent>(uid, out var integrity) && damage.TotalDamage < integrity.MaxIntegrity;
    }

    public EntityUid? GetShield(EntityUid user)
    {
        EntityUid? best = null;
        var coefficient = 1f;
        if (!HasComp<HandsComponent>(user))
            return null;
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp<PaladinShieldComponent>(held, out var shield) && IsFunctional(held, shield) && (IsRaised(held) || shield.DamageCoefficient < coefficient))
            {
                if (IsRaised(held))
                    return held;
                best = held;
                coefficient = shield.DamageCoefficient;
            }
        }
        return best;
    }

    public float GetProtection(EntityUid user) => GetShield(user) is { } shield ? Comp<PaladinShieldComponent>(shield).DamageCoefficient : 1f;

    // Keep the normal riot-shield action and examine UI, but apply our durability split once.
    private void OnBeforeBlocking(EntityUid uid, PaladinShieldComponent comp, BeforeBlockingEvent args)
    {
        args.Cancel();
    }

    public bool IsRaised(EntityUid shield) => TryComp<BlockingComponent>(shield, out var blocking) && blocking.IsBlocking;

    private void OnStamina(Entity<HandsComponent> user, ref BeforeStaminaDamageEvent args)
    {
        if (args.Value <= 0)
            return;
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp<PaladinShieldComponent>(held, out var shield) && (shield.StaminaImmune || IsRaised(held)) && IsFunctional(held, shield))
            {
                args.Cancelled = true;
                return;
            }
        }
    }

    private void OnDamage(EntityUid uid, HandsComponent comp, DamageModifyEvent args)
    {
        if (GetShield(uid) is not { } item)
            return;
        var shield = Comp<PaladinShieldComponent>(item);
        var raised = IsRaised(item);
        var coefficient = raised ? 0 : shield.DamageCoefficient;
        var wear = new DamageSpecifier();
        var supported = Comp<DamageableComponent>(item).Damage.DamageDict;
        foreach (var (type, amount) in args.OriginalDamage.DamageDict)
        {
            if (amount > 0 && supported.ContainsKey(type))
                wear.DamageDict[type] = amount * (raised ? 1 : shield.WearFraction);
        }
        if (wear.AnyPositive())
        {
            var ev = new ShieldWearEvent(wear);
            RaiseLocalEvent(item, ref ev);
        }
        // Holding two shields must not stack their resistance. Healing is unaffected.
        var modifier = new DamageModifierSet();
        foreach (var type in raised ? supported.Keys.ToArray() : new[] { "Blunt", "Slash", "Piercing" })
        {
            if (args.Damage.DamageDict.TryGetValue(type, out var amount) && amount > 0)
                modifier.Coefficients[type] = coefficient;
        }
        args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, modifier);
    }
}
