// #Misfits Add - Adds a point-blank gun execution verb when pointing a gun at an
// incapacitated target, consuming one round and applying lethal damage.
// Ported and adapted from Goob-Station (Content.Goobstation.Shared.Execution.SharedGunExecutionSystem).
// Simplified to server-only to avoid prediction complexity; camera recoil omitted.

using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Misfits.Execution;
using Content.Shared.ActionBlocker;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Execution;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Ranged;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server._Misfits.Execution;

/// <summary>
/// Adds a "Execute" utility verb to guns when targeting an incapacitated mob.
/// A 4-second DoAfter consumes one round and deals lethal damage.
/// </summary>
public sealed class GunExecutionSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedSuicideSystem _suicide = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedExecutionSystem _execution = default!;
    [Dependency] private readonly SharedGunSystem _gunSystem = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;

    // How long the execution animation plays before firing.
    private const float GunExecutionTime = 4.0f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunComponent, GetVerbsEvent<UtilityVerb>>(OnGetVerbs);
        SubscribeLocalEvent<GunComponent, ExecutionDoAfterEvent>(OnDoAfter);
    }

    // ── Verb ─────────────────────────────────────────────────────────────────

    private void OnGetVerbs(EntityUid uid, GunComponent gun, GetVerbsEvent<UtilityVerb> args)
    {
        if (args.Hands == null || args.Using == null || !args.CanAccess || !args.CanInteract)
            return;

        var weapon = args.Using.Value;
        var attacker = args.User;
        var victim = args.Target;

        // Blacklisted guns can't execute.
        if (HasComp<GunExecutionBlacklistComponent>(weapon)
            || !CanExecuteWithGun(weapon, victim, attacker))
            return;

        var verb = new UtilityVerb
        {
            Act = () => TryStartExecutionDoAfter(weapon, victim, attacker),
            Impact = LogImpact.High,
            Text = Loc.GetString("execution-verb-name"),
            Message = Loc.GetString("execution-verb-message"),
        };

        args.Verbs.Add(verb);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool CanExecuteWithGun(EntityUid weapon, EntityUid victim, EntityUid attacker)
    {
        // Re-use the shared execution check (victim incapacitated, attacker can attack, etc.)
        if (!_execution.CanBeExecuted(victim, attacker, weapon))
            return false;

        // Gun must have ammo to fire.
        if (TryComp<GunComponent>(weapon, out var gun) && !_gunSystem.CanShoot(gun))
            return false;

        return true;
    }

    private void TryStartExecutionDoAfter(EntityUid weapon, EntityUid victim, EntityUid attacker)
    {
        if (!CanExecuteWithGun(weapon, victim, attacker))
            return;

        // Show flavour popups.
        if (attacker == victim)
        {
            ShowInternalPopup("suicide-popup-gun-initial-internal", attacker, victim, weapon);
            ShowExternalPopup("suicide-popup-gun-initial-external", attacker, victim, weapon);
        }
        else
        {
            ShowInternalPopup("execution-popup-gun-initial-internal", attacker, victim, weapon);
            ShowExternalPopup("execution-popup-gun-initial-external", attacker, victim, weapon);
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, attacker, GunExecutionTime,
            new ExecutionDoAfterEvent(), weapon, target: victim, used: weapon)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            MultiplyDelay = false,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    // ── DoAfter ───────────────────────────────────────────────────────────────

    private void OnDoAfter(EntityUid uid, GunComponent gun, ExecutionDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used == null || args.Target == null)
            return;

        var attacker = args.User;
        var victim = args.Target.Value;
        var weapon = args.Used.Value;

        if (!CanExecuteWithGun(weapon, victim, attacker)
            || !TryComp<DamageableComponent>(victim, out var damageable))
            return;

        // Take one round from the gun.
        var fromCoords = Transform(attacker).Coordinates;
        var ammoEv = new TakeAmmoEvent(1, new List<(EntityUid? Entity, IShootable Shootable)>(), fromCoords, attacker);
        RaiseLocalEvent(weapon, ammoEv);

        if (ammoEv.Ammo.Count == 0)
        {
            _audio.PlayPvs(gun.SoundEmpty, uid);
            ShowInternalPopup("execution-popup-gun-empty", attacker, victim, weapon);
            ShowExternalPopup("execution-popup-gun-empty", attacker, victim, weapon);
            return;
        }

        // Determine damage type from the projectile prototype.
        var mainDamageType = GetMainDamageType(ammoEv.Ammo[0]);

        // Fire the execution shot.
        var prevCombat = _combat.IsInCombatMode(attacker);
        _combat.SetInCombatMode(attacker, true);

        if (attacker == victim)
        {
            ShowInternalPopup("suicide-popup-gun-complete-internal", attacker, victim, weapon);
            ShowExternalPopup("suicide-popup-gun-complete-external", attacker, victim, weapon);
        }
        else
        {
            ShowInternalPopup("execution-popup-gun-complete-internal", attacker, victim, weapon);
            ShowExternalPopup("execution-popup-gun-complete-external", attacker, victim, weapon);
        }

        _audio.PlayPvs(gun.SoundGunshot, uid);
        _suicide.ApplyLethalDamage((victim, damageable), mainDamageType);

        _combat.SetInCombatMode(attacker, prevCombat);
        args.Handled = true;
    }

    // ── Private utils ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the dominant damage type for the given ammo entry so we can pass
    /// it to <see cref="SharedSuicideSystem.ApplyLethalDamage"/>.
    /// Falls back to "Blunt" if nothing meaningful is found.
    /// </summary>
    private string GetMainDamageType((EntityUid? Entity, IShootable Shootable) ammo)
    {
        DamageSpecifier? damage = null;

        switch (ammo.Shootable)
        {
            case CartridgeAmmoComponent cartridge:
            {
                if (cartridge.Spent)
                    return "Blunt"; // spent cartridge — treat as empty

                if (_prototypeManager.TryIndex<EntityPrototype>(cartridge.Prototype, out var proto)
                    && proto.TryGetComponent<ProjectileComponent>(out var projectile, _componentFactory))
                {
                    damage = projectile.Damage;
                }

                cartridge.Spent = true;
                _appearance.SetData(ammo.Entity!.Value, AmmoVisuals.Spent, true);
                Dirty(ammo.Entity.Value, cartridge);
                break;
            }
            case AmmoComponent newAmmo:
            {
                if (TryComp<ProjectileComponent>(ammo.Entity, out var projectile))
                    damage = projectile.Damage;

                if (ammo.Entity != null)
                    Del(ammo.Entity.Value);
                break;
            }
        }

        if (damage == null || damage.DamageDict.Count == 0)
            return "Blunt";

        // Pick the damage type with the highest value.
        var best = damage.DamageDict
            .Where(kv => !string.Equals(kv.Key, "Structural", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value)
            .FirstOrDefault();

        return best.Key ?? "Blunt";
    }

    private void ShowInternalPopup(string locKey, EntityUid attacker, EntityUid victim, EntityUid weapon)
    {
        _popup.PopupEntity(
            Loc.GetString(locKey, ("attacker", attacker), ("victim", victim), ("weapon", weapon)),
            attacker,
            attacker,
            PopupType.MediumCaution);
    }

    private void ShowExternalPopup(string locKey, EntityUid attacker, EntityUid victim, EntityUid weapon)
    {
        _popup.PopupEntity(
            Loc.GetString(locKey, ("attacker", attacker), ("victim", victim), ("weapon", weapon)),
            attacker,
            Robust.Shared.Player.Filter.PvsExcept(attacker),
            true,
            PopupType.MediumCaution);
    }
}
