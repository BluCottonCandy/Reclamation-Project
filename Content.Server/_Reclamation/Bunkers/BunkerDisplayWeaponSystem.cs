using Content.Server.Mail.Components;
using Content.Shared.Access.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.PowerCell.Components;
using Content.Shared.PowerCell;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;

namespace Content.Server._Reclamation.Bunkers;

/// <summary>Display conversion is permanent, including when prototype components are recreated by loading.</summary>
public sealed class BunkerDisplayWeaponSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    private static readonly string[] Disabled =
    {
        "Gun", "BallisticAmmoProvider", "ChamberMagazineAmmoProvider", "MagazineAmmoProvider",
        "RevolverAmmoProvider", "BasicEntityAmmoProvider", "ContainerAmmoProvider", "ClothingSlotAmmoProvider",
        "HitscanBatteryAmmoProvider", "ProjectileBatteryAmmoProvider", "SolutionAmmoProvider",
        "BatterySelfRecharger", "BatteryWeaponFireModes", "ItemToggleMeleeWeapon", "ItemToggle",
        "BonusMeleeDamage", "DamageOtherOnHit", "StaminaDamageOnHit", "MeleeThrowOnHit",
        "Sharp", "EmbeddableProjectile", "MeleeChemicalInjector", "MeleeStaminaCost",
        "UseDelay", "Projectile", "Explosive", "OnUseTimerTrigger", "TriggerOnUse"
    };
    // Armor on mannequins and inside nested containers uses the same departure/save path as weapons.
    private static readonly string[] ArmorProtection =
    {
        "Armor", "PowerArmorIntegrity", "PowerArmorBrace", "Reflect", "ExplosionResistance",
        "StaminaDamageResistance", "FireProtection", "TemperatureProtection", "PressureProtection",
        "EyeProtection", "GloveHeatResistance", "Insulated", "DiseaseProtection",
        "ClothingModifyStunTime", "SpearBlock"
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<BunkerDisplayItemComponent, MapInitEvent>(OnItemMapInit);
        SubscribeLocalEvent<BunkerDisplayItemComponent, ContainerIsInsertingAttemptEvent>(OnItemInsert);
        SubscribeLocalEvent<BunkerDisplayWeaponComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BunkerDisplayArmorComponent, MapInitEvent>(OnArmorMapInit);
        SubscribeLocalEvent<BunkerDisplayArmorComponent, ExaminedEvent>(OnArmorExamine);
        SubscribeLocalEvent<BunkerDisplayWeaponComponent, ShotAttemptedEvent>(OnShoot);
        SubscribeLocalEvent<BunkerDisplayWeaponComponent, ContainerIsInsertingAttemptEvent>(OnInsert);
        SubscribeLocalEvent<BunkerDisplayWeaponComponent, MeleeHitEvent>(OnMelee);
        SubscribeLocalEvent<BunkerDisplayWeaponComponent, ExaminedEvent>(OnExamine);
    }
    private void OnMapInit(Entity<BunkerDisplayWeaponComponent> ent, ref MapInitEvent args) => Decommission(ent);
    private void OnArmorMapInit(Entity<BunkerDisplayArmorComponent> ent, ref MapInitEvent args) => DecommissionArmor(ent);
    private void OnArmorExamine(Entity<BunkerDisplayArmorComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("bunker-display-armor-examine"));

    /// <summary>Keep the original clothing visuals and slots, but permanently strip its protection.</summary>
    public void DecommissionArmor(EntityUid uid)
    {
        if (HasComp<MobStateComponent>(uid)) return;
        var protective = HasComp<ArmorComponent>(uid) || HasComp<PowerArmorIntegrityComponent>(uid)
            || HasComp<BunkerDisplayArmorComponent>(uid);
        if (!protective && HasComp<ClothingComponent>(uid))
            foreach (var name in ArmorProtection)
                if (_factory.TryGetRegistration(name, out var registration)
                    && EntityManager.HasComponent(uid, registration.Type)) protective = true;
        if (!protective) return;
        EnsureComp<BunkerDisplayArmorComponent>(uid);
        foreach (var name in ArmorProtection)
            if (_factory.TryGetRegistration(name, out var registration))
                EntityManager.RemoveComponent(uid, registration.Type);
    }

    private void OnShoot(Entity<BunkerDisplayWeaponComponent> ent, ref ShotAttemptedEvent args) => args.Cancel();
    private void OnInsert(Entity<BunkerDisplayWeaponComponent> ent, ref ContainerIsInsertingAttemptEvent args) => args.Cancel();
    private void OnExamine(Entity<BunkerDisplayWeaponComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup("[color=yellow]Permanently decommissioned display weapon. Cannot fire; melee deals 1 blunt.[/color]");
    private void OnMelee(Entity<BunkerDisplayWeaponComponent> ent, ref MeleeHitEvent args)
    {
        args.BaseDamage.DamageDict.Clear();
        args.BaseDamage.DamageDict["Blunt"] = FixedPoint2.New(1);
        args.BonusDamage = new DamageSpecifier();
        args.ModifiersList.Clear();
    }

    private void OnItemMapInit(Entity<BunkerDisplayItemComponent> ent, ref MapInitEvent args) => DecommissionItem(ent);
    private void OnItemInsert(Entity<BunkerDisplayItemComponent> ent, ref ContainerIsInsertingAttemptEvent args) => args.Cancel();

    private void RemoveComponents(EntityUid uid, params string[] names)
    {
        foreach (var name in names)
            if (_factory.TryGetRegistration(name, out var registration))
                EntityManager.RemoveComponent(uid, registration.Type);
    }

    private void DecommissionItem(EntityUid uid)
    {
        var ammo = !HasComp<GunComponent>(uid) && HasComp<BallisticAmmoProviderComponent>(uid);
        var id = HasComp<IdCardComponent>(uid);
        if (!ammo && !id && !HasComp<BunkerDisplayItemComponent>(uid)) return;
        var display = EnsureComp<BunkerDisplayItemComponent>(uid);
        if (display.OriginalName.Length == 0) display.OriginalName = Name(uid);
        display.Ammo |= ammo;
        display.Id |= id;
        if (display.Ammo)
        {
            var children = Transform(uid).ChildEnumerator;
            var cartridges = new List<EntityUid>();
            while (children.MoveNext(out var child))
                if (HasComp<AmmoComponent>(child)) cartridges.Add(child);
            foreach (var child in cartridges) Del(child);
            RemoveComponents(uid, "BallisticAmmoProvider", "MagazineAmmoProvider", "ContainerAmmoProvider", "BasicEntityAmmoProvider", "StorageFill");
            _appearance.SetData(uid, AmmoVisuals.AmmoCount, 1);
            _appearance.SetData(uid, AmmoVisuals.AmmoMax, 1);
            _appearance.SetData(uid, AmmoVisuals.HasAmmo, true);
            _appearance.SetData(uid, AmmoVisuals.MagLoaded, true);
        }
        if (display.Id)
            RemoveComponents(uid, "IdCard", "Access", "PresetIdCard", "NanoChatCard", "StationRecordKeyStorage", "AgentIDCard");
        _metadata.SetEntityName(uid, $"decorative {display.OriginalName}");
        _metadata.SetEntityDescription(uid, display.Id
            ? "A decommissioned ID kept for display. It provides no identification, access or messaging."
            : "An inert ammunition display. It cannot hold or supply ammunition.");
    }

    private void NormalizeStims(EntityUid uid)
    {
        if (!TryComp<SolutionComponent>(uid, out var component)) return;
        var solution = component.Solution;
        var canReact = solution.CanReact;
        solution.CanReact = false;
        try
        {
            foreach (var reagent in solution.Contents.ToArray())
            {
                if (reagent.Reagent.Prototype != "RobustHealingMixture" && reagent.Reagent.Prototype != "UltraRobustHealingMixture") continue;
                _solutions.RemoveReagent((uid, component), reagent);
                _solutions.TryAddReagent((uid, component), "HealingMixture", reagent.Quantity);
            }
        }
        finally { solution.CanReact = canReact; }
    }

    public void Decommission(EntityUid uid)
    {
        // Delete parcels without opening them; the mail teleporter machine is deliberately retained.
        if (HasComp<MailComponent>(uid)) { Del(uid); return; }
        NormalizeStims(uid);
        DecommissionItem(uid);
        // A toolbox is working storage, even though it can also be swung as a weapon.
        if (_tags.HasTag(uid, "Toolbox"))
        {
            RestoreToolbox(uid);
            return;
        }
        DecommissionArmor(uid);
        if (HasComp<MobStateComponent>(uid) || (!HasComp<GunComponent>(uid) && !HasComp<MeleeWeaponComponent>(uid)
            && !HasComp<BunkerDisplayWeaponComponent>(uid))) return;
        EnsureComp<BunkerDisplayWeaponComponent>(uid);
        // Loaded magazines, cartridges and power cells cannot be recovered from a display firearm.
        var children = Transform(uid).ChildEnumerator;
        var contents = new List<EntityUid>();
        while (children.MoveNext(out var child)) contents.Add(child);
        foreach (var child in contents)
        {
            if (HasComp<GunComponent>(child) || HasComp<MeleeWeaponComponent>(child)) Decommission(child);
            else if (HasComp<BallisticAmmoProviderComponent>(child)) DecommissionItem(child);
            else if (HasComp<AmmoComponent>(child) || HasComp<PowerCellComponent>(child)) Del(child);
        }
        foreach (var name in Disabled)
            if (_factory.TryGetRegistration(name, out var registration))
                EntityManager.RemoveComponent(uid, registration.Type);
        if (TryComp<MeleeWeaponComponent>(uid, out var melee))
        {
            melee.Damage = new DamageSpecifier { DamageDict = new() { ["Blunt"] = FixedPoint2.New(1) } };
            melee.BluntStaminaDamageFactor = FixedPoint2.Zero;
            melee.ClickDamageModifier = FixedPoint2.New(1);
            melee.HeavyDamageBaseModifier = 1;
            melee.DisableHeavy = true;
            melee.ResistanceBypass = false;
            Dirty(uid, melee);
        }
    }

    /// <summary>Repair toolboxes already marked as display weapons by an older bunker save.</summary>
    public void RestoreToolbox(EntityUid uid)
    {
        if (!_tags.HasTag(uid, "Toolbox") || !HasComp<BunkerDisplayWeaponComponent>(uid))
            return;

        RemComp<BunkerDisplayWeaponComponent>(uid);
        if (MetaData(uid).EntityPrototype is not { } prototype)
            return;

        // Loading recreates most prototype components, but saved melee overrides need resetting.
        if (prototype.Components.TryGetValue("MeleeWeapon", out var melee))
            EntityManager.AddComponent(uid, melee, overwrite: true);
        foreach (var name in Disabled)
            if (prototype.Components.TryGetValue(name, out var component)
                && _factory.TryGetRegistration(name, out var registration)
                && !EntityManager.HasComponent(uid, registration.Type))
                EntityManager.AddComponent(uid, component);
    }
}
