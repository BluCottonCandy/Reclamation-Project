using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Misfits.Weapons;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Administration.Logs;
using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - Psy-Forge (Intermediate, +25%) and Greater Psy-Forge (King, +50%). Cast while holding:
/// - armour: every protection it gives grows by that share of its normal value (capped at 90%, weaknesses untouched);
/// - a melee weapon: its hits;
/// - a gun: every shot it fires, bullets and beam lasers;
/// - bullets: a single round, or a magazine / ammo box (every round inside);
/// - a power cell: its capacity (more shots).
/// For good. Each version works once per item and they stack to +75%, always worked out from the NORMAL value.
/// </summary>
public sealed class MagicPsyForgeSystem : EntitySystem
{
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;

    private const float MaxProtection = 0.9f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicPsyForgeEvent>(OnForge);
        // Raised on the gun for every shot, and on a cartridge for the bullet it fired.
        SubscribeLocalEvent<MagicPsyForgedComponent, AmmoShotEvent>(OnShot);
        SubscribeLocalEvent<MagicPsyForgedComponent, Content.Shared.Examine.ExaminedEvent>(OnExamined);
    }

    /// <summary>Things Psy-Forge can work on.</summary>
    public bool CanForge(EntityUid item) =>
        HasComp<ArmorComponent>(item) || HasComp<MeleeWeaponComponent>(item) || HasComp<GunComponent>(item)
        || HasComp<CartridgeAmmoComponent>(item) || HasComp<BallisticAmmoProviderComponent>(item)
        || HasComp<BatteryComponent>(item);

    private void OnForge(MagicPsyForgeEvent args)
    {
        if (args.Handled)
            return;
        var user = args.Performer;

        // Checked before paying: nothing it can work on (or already forged this way) costs nothing.
        EntityUid? target = null;
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (CanForge(held))
            {
                target = held;
                break;
            }
        }
        if (target is not { } item)
        {
            _magic.Notify(user, Loc.GetString("magic-psyforge-no-armour"));
            return;
        }
        if (TryComp<MagicPsyForgedComponent>(item, out var done) && (args.Major ? done.Major : done.Minor))
        {
            _magic.Notify(user, Loc.GetString("magic-psyforge-already"));
            return;
        }

        if (!_magic.TryPayForSpell(args.Action, user))
            return;
        args.Handled = true;

        var total = Forge(item, args.Bonus, args.Major);
        _magic.Flash(item, MagicClass.Psychic, "Hit");
        _magic.Notify(user, Loc.GetString("magic-psyforge-done", ("item", item), ("total", (int) MathF.Round(total * 100f))), PopupType.Medium);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):player} psy-forged {ToPrettyString(item):item} (+{args.Bonus * 100:0}%, now +{total * 100:0}%)");
    }

    /// <summary>Adds a bonus to an item (worked out from its normal values). Returns the total bonus.</summary>
    public float Forge(EntityUid item, float bonus, bool major)
    {
        var forged = EnsureComp<MagicPsyForgedComponent>(item);
        var first = !forged.Minor && !forged.Major;
        if (first)
            Remember(item, forged);
        if (major)
            forged.Major = true;
        else
            forged.Minor = true;
        forged.Bonus += bonus;

        ApplyArmour(item, forged);
        ApplyMelee(item, forged);
        ApplyCell(item, forged);
        // A magazine or ammo box: every round inside is forged too (each round keeps its own bonus).
        if (HasComp<BallisticAmmoProviderComponent>(item) && !HasComp<GunComponent>(item))
            ForgeRounds(item, bonus, major);
        // A gun: the bullets loaded in it are forged too (its own tube or cylinder, the magazine in it, the chambered
        // round), so they keep the bonus if they are moved to another gun.
        if (HasComp<GunComponent>(item))
            ForgeLoaded(item, bonus, major);

        var ev = new MagicPsyForgedChangedEvent();
        RaiseLocalEvent(item, ref ev);

        var key = forged.Minor && forged.Major ? "magic-psyforge-name-both" : forged.Major ? "magic-psyforge-name-major" : "magic-psyforge-name-minor";
        _meta.SetEntityName(item, Loc.GetString(key, ("name", forged.BaseName)));
        return forged.Bonus;
    }

    /// <summary>First forging: remember what the item normally gives.</summary>
    private void Remember(EntityUid item, MagicPsyForgedComponent forged)
    {
        forged.BaseName = Name(item);
        if (TryComp<ArmorComponent>(item, out var armour))
        {
            forged.BaseCoefficients = new Dictionary<string, float>(armour.Modifiers.Coefficients);
            forged.BaseFlat = new Dictionary<string, float>(armour.Modifiers.FlatReduction);
        }
        if (TryComp<MeleeWeaponComponent>(item, out var melee) && !HasComp<MagicPsyBladeComponent>(item))
            forged.BaseMelee = new DamageSpecifier(melee.Damage);
        if (TryComp<GunDamageBonusComponent>(item, out var beam) && beam.BonusDamage != null)
            forged.BaseBeamBonus = new DamageSpecifier(beam.BonusDamage);
        if (TryComp<BatteryComponent>(item, out var cell))
            forged.BaseMaxCharge = cell.MaxCharge;
    }

    /// <summary>The multiplier for an item: 1 + its total bonus.</summary>
    public float Factor(EntityUid item) => TryComp<MagicPsyForgedComponent>(item, out var forged) ? 1f + forged.Bonus : 1f;

    private void ApplyArmour(EntityUid item, MagicPsyForgedComponent forged)
    {
        if (!TryComp<ArmorComponent>(item, out var armour))
            return;
        // A fresh set, never the prototype's shared one.
        var set = new DamageModifierSet();
        foreach (var (type, coefficient) in forged.BaseCoefficients)
        {
            if (coefficient >= 1f)
            {
                set.Coefficients[type] = coefficient; // a weakness stays a weakness
                continue;
            }
            var protection = MathF.Min(MaxProtection, (1f - coefficient) * (1f + forged.Bonus));
            set.Coefficients[type] = 1f - protection;
        }
        foreach (var (type, flat) in forged.BaseFlat)
            set.FlatReduction[type] = flat > 0 ? flat * (1f + forged.Bonus) : flat;
        armour.Modifiers = set;
        Dirty(item, armour);
    }

    private void ApplyMelee(EntityUid item, MagicPsyForgedComponent forged)
    {
        // The psy katana sets its own damage (drawn / sheathed) and reads the factor itself.
        if (forged.BaseMelee == null || !TryComp<MeleeWeaponComponent>(item, out var melee))
            return;
        melee.Damage = forged.BaseMelee * (1f + forged.Bonus);
        Dirty(item, melee);
    }

    /// <summary>
    /// Beam guns: the multiplier for a beam fired from this gun, from the gun's own bonus or a forged power cell
    /// loaded in it, whichever is bigger (the gun system calls this for every beam).
    /// </summary>
    public float BeamFactor(EntityUid gun)
    {
        var best = TryComp<MagicPsyForgedComponent>(gun, out var forged) ? forged.Bonus : 0f;
        foreach (var container in _containers.GetAllContainers(gun))
        {
            foreach (var held in container.ContainedEntities)
            {
                if (TryComp<MagicPsyForgedComponent>(held, out var cell) && cell.Bonus > best)
                    best = cell.Bonus;
            }
        }
        return 1f + best;
    }

    /// <summary>Examine: how much stronger Psy-Forge made it.</summary>
    private void OnExamined(Entity<MagicPsyForgedComponent> ent, ref Content.Shared.Examine.ExaminedEvent args)
    {
        if (ent.Comp.Bonus > 0f)
            args.PushMarkup(Loc.GetString("magic-psyforge-examine", ("bonus", (int) MathF.Round(ent.Comp.Bonus * 100f))));
    }

    /// <summary>Power cells hold more charge (more shots). The charge already in it scales with it.</summary>
    private void ApplyCell(EntityUid item, MagicPsyForgedComponent forged)
    {
        if (HasComp<GunComponent>(item) || !TryComp<BatteryComponent>(item, out var cell) || forged.BaseMaxCharge <= 0f)
            return;
        var fill = cell.MaxCharge > 0f ? cell.CurrentCharge / cell.MaxCharge : 0f;
        _battery.SetMaxCharge(item, forged.BaseMaxCharge * (1f + forged.Bonus), cell);
        _battery.SetCharge(item, cell.MaxCharge * fill, cell);
    }

    /// <summary>Every round in a magazine or ammo box: the ones not made yet are made first so they can be forged.</summary>
    private void ForgeRounds(EntityUid box, float bonus, bool major)
    {
        if (!TryComp<BallisticAmmoProviderComponent>(box, out var provider))
            return;
        if (provider.Proto is { } proto && provider.UnspawnedCount > 0)
        {
            var coords = Transform(box).Coordinates;
            for (var i = provider.UnspawnedCount; i > 0; i--)
            {
                var round = Spawn(proto, coords);
                if (!_containers.Insert(round, provider.Container))
                {
                    QueueDel(round);
                    break;
                }
                provider.UnspawnedCount--;
            }
            Dirty(box, provider);
        }
        foreach (var round in provider.Container.ContainedEntities)
        {
            if (HasComp<CartridgeAmmoComponent>(round))
                Forge(round, bonus, major);
        }
    }

    /// <summary>Every round loaded in a gun, wherever it sits (internal tube, magazine slot, chamber).</summary>
    private void ForgeLoaded(EntityUid gun, float bonus, bool major)
    {
        if (HasComp<BallisticAmmoProviderComponent>(gun))
            ForgeRounds(gun, bonus, major);
        foreach (var container in _containers.GetAllContainers(gun))
        {
            foreach (var held in container.ContainedEntities)
            {
                if (HasComp<CartridgeAmmoComponent>(held))
                    Forge(held, bonus, major);
                else if (HasComp<BallisticAmmoProviderComponent>(held) && !HasComp<GunComponent>(held))
                    ForgeRounds(held, bonus, major);
            }
        }
    }

    /// <summary>A forged gun or bullet: every projectile it just fired hits harder.</summary>
    private void OnShot(Entity<MagicPsyForgedComponent> ent, ref AmmoShotEvent args)
    {
        var factor = 1f + ent.Comp.Bonus;
        if (factor <= 1f)
            return;
        foreach (var shot in args.FiredProjectiles)
        {
            if (!TryComp<ProjectileComponent>(shot, out var projectile))
                continue;
            // The biggest bonus among the forged gun and the forged round wins; it's always worked out from the
            // bullet's own damage, so a burst (where the list holds every bullet so far) never boosts one twice.
            var boosted = EnsureComp<MagicPsyForgedShotComponent>(shot);
            boosted.BaseDamage ??= new DamageSpecifier(projectile.Damage);
            if (ent.Comp.Bonus <= boosted.Bonus)
                continue;
            boosted.Bonus = ent.Comp.Bonus;
            projectile.Damage = boosted.BaseDamage * factor;
            Dirty(shot, projectile);
        }
    }
}
