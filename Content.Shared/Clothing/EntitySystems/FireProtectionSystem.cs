using Content.Shared.Armor;
using Content.Shared.Atmos;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Projectiles;

namespace Content.Shared.Clothing.EntitySystems;

/// <summary>
/// Handles reducing fire damage when wearing clothing with <see cref="FireProtectionComponent"/>.
/// </summary>
public sealed class FireProtectionSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FireProtectionComponent, InventoryRelayedEvent<GetFireProtectionEvent>>(OnGetProtection);
        SubscribeLocalEvent<FlameProjectileComponent, ProjectileHitEvent>(OnFlameHit);
        SubscribeLocalEvent<FireProtectionComponent, ArmorExamineEvent>(OnExamine);
    }

    private void OnGetProtection(Entity<FireProtectionComponent> ent, ref InventoryRelayedEvent<GetFireProtectionEvent> args)
    {
        args.Args.Reduce(ent.Comp.Reduction, ent.Comp.MaximumReduction);
    }

    private void OnExamine(Entity<FireProtectionComponent> ent, ref ArmorExamineEvent args)
    {
        var percentage = MathF.Round(Math.Clamp(Math.Min(ent.Comp.Reduction, ent.Comp.MaximumReduction), 0f, 1f) * 100f, 1);
        if (percentage == 0)
            return;

        args.Msg.PushNewline();
        args.Msg.AddMarkup(Loc.GetString("armor-coefficient-value",
            ("type", Loc.GetString("armor-damage-type-flame")), ("value", percentage)));
    }

    private void OnFlameHit(Entity<FlameProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (!args.Damage.DamageDict.TryGetValue("Heat", out var heat) || heat <= 0)
            return;

        var protection = new GetFireProtectionEvent();
        RaiseLocalEvent(args.Target, ref protection);
        if (TryComp<InventoryComponent>(args.Target, out var inventory))
            _inventory.RelayEvent((args.Target, inventory), ref protection);

        // Never mutate the projectile's original damage or affect its non-heat damage.
        args.Damage = new Content.Shared.Damage.DamageSpecifier(args.Damage);
        args.Damage.DamageDict["Heat"] = heat * protection.Multiplier;
    }
}
