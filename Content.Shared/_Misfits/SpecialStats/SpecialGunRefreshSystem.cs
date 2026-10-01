using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Hands;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._Misfits.SpecialStats;

/// <summary>
/// Refreshes held gun modifiers when holder-based SPECIAL effects can change.
/// </summary>
public sealed class SpecialGunRefreshSystem : EntitySystem
{
    [Dependency] private readonly SharedGunSystem _gun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<GunComponent, GotEquippedHandEvent>(OnGunEquipped);
        SubscribeLocalEvent<GunComponent, GotUnequippedHandEvent>(OnGunUnequipped);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args)
    {
        RefreshActiveGun(args.ChangedEntity);
    }

    private void OnStatsReady(ref SpecialStatsReadyEvent args)
    {
        RefreshActiveGun(args.Entity);
    }

    private void OnGunEquipped(Entity<GunComponent> ent, ref GotEquippedHandEvent args)
    {
        _gun.RefreshModifiers((ent.Owner, ent.Comp));
    }

    private void OnGunUnequipped(Entity<GunComponent> ent, ref GotUnequippedHandEvent args)
    {
        _gun.RefreshModifiers((ent.Owner, ent.Comp));
    }

    private void RefreshActiveGun(EntityUid user)
    {
        if (_gun.TryGetGun(user, out var gunUid, out var gun))
            _gun.RefreshModifiers((gunUid, gun));
    }
}
