using Content.Shared.Armor;
using Content.Shared._ReclamationProject.PowerArmor;
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
        SubscribeLocalEvent<ModularPowerArmorComponent, InventoryRelayedEvent<GetFireProtectionEvent>>(OnGetFrameProtection);
        SubscribeLocalEvent<ModularPowerArmorComponent, GetFireProtectionEvent>(OnFrameFire);
        SubscribeLocalEvent<PowerArmorPlateComponent, GetFireProtectionEvent>(OnPlateFire);
        SubscribeLocalEvent<FlameProjectileComponent, ProjectileHitEvent>(OnFlameHit);
        SubscribeLocalEvent<FireProtectionComponent, ArmorExamineEvent>(OnExamine);
    }

    private void OnGetProtection(Entity<FireProtectionComponent> ent, ref InventoryRelayedEvent<GetFireProtectionEvent> args)
    {
        if (HasComp<ModularPowerArmorComponent>(ent))
            return;
        args.Args.Reduce(ent.Comp.Reduction, ent.Comp.MaximumReduction);
    }

    private void OnGetFrameProtection(Entity<ModularPowerArmorComponent> ent, ref InventoryRelayedEvent<GetFireProtectionEvent> args)
    {
        if (IsWornAndSealed(ent, ent.Comp))
            args.Args.Seal();
    }

    private bool IsWornAndSealed(EntityUid uid, ModularPowerArmorComponent frame) =>
        frame.Sealed && TryComp<ClothingComponent>(uid, out var clothing) && clothing.InSlot == "outerClothing";

    private void OnFrameFire(Entity<ModularPowerArmorComponent> ent, ref GetFireProtectionEvent args)
    {
        if (IsWornAndSealed(ent, ent.Comp))
            args.Seal();
    }

    private void OnPlateFire(Entity<PowerArmorPlateComponent> ent, ref GetFireProtectionEvent args)
    {
        var parent = Transform(ent).ParentUid;
        if (TryComp<ModularPowerArmorComponent>(parent, out var frame) && IsWornAndSealed(parent, frame))
            args.Seal();
    }

    public GetFireProtectionEvent GetProtection(EntityUid target)
    {
        var protection = new GetFireProtectionEvent();
        RaiseLocalEvent(target, ref protection);
        if (TryComp<InventoryComponent>(target, out var inventory))
            _inventory.RelayEvent((target, inventory), ref protection);
        return protection;
    }

    private void OnExamine(Entity<FireProtectionComponent> ent, ref ArmorExamineEvent args)
    {
        var percentage = TryComp<ModularPowerArmorComponent>(ent, out var frame)
            ? (frame.Sealed ? 100f : 0f)
            : MathF.Round(Math.Clamp(Math.Min(ent.Comp.Reduction, ent.Comp.MaximumReduction), 0f, 1f) * 100f, 1);
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

        var protection = GetProtection(args.Target);

        // Never mutate the projectile's original damage or affect its non-heat damage.
        args.Damage = new Content.Shared.Damage.DamageSpecifier(args.Damage);
        args.Damage.DamageDict["Heat"] = heat * protection.Multiplier;
    }
}
