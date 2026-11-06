using System.Linq;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Whitelist;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    public (int Count, int Capacity) GetFastHandsAmmo(EntityUid target)
    {
        if (TryComp<BallisticAmmoProviderComponent>(target, out var ballistic))
            return (ballistic.AmmoCount, ballistic.Capacity);
        if (TryComp<RevolverAmmoProviderComponent>(target, out var revolver))
            return (GetRevolverUnspentCount(revolver), revolver.Capacity);
        return (0, 0);
    }

    public TimeSpan GetFastHandsReloadDelay(EntityUid source)
    {
        var normal = TryComp<BallisticAmmoProviderComponent>(source, out var giver)
            ? giver.FillDelay : TimeSpan.FromSeconds(0.5);
        return TimeSpan.FromSeconds(Math.Max(0.05, normal.TotalSeconds / 2));
    }

    public bool CanFastHandsFill(EntityUid target, EntityUid source)
    {
        var (count, capacity) = GetFastHandsAmmo(target);
        if (capacity <= 0 || count >= capacity || source == target)
            return false;
        EntityWhitelist? whitelist;
        if (TryComp<BallisticAmmoProviderComponent>(target, out var ballistic))
            whitelist = ballistic.Whitelist;
        else if (TryComp<RevolverAmmoProviderComponent>(target, out var revolver))
            whitelist = revolver.Whitelist;
        else
            return false;
        if (TryComp<BallisticAmmoProviderComponent>(source, out var giver))
            return giver.MayTransfer && giver.AmmoCount > 0 && whitelist?.Tags is { } tags &&
                giver.Whitelist?.Tags is { } sourceTags && tags.Any(sourceTags.Contains);
        return TryComp<CartridgeAmmoComponent>(source, out var cartridge) && !cartridge.Spent &&
            _whitelistSystem.IsWhitelistPass(whitelist, source);
    }

    /// <summary>Transfer one real round through the normal insertion paths; the caller controls timing.</summary>
    public bool TryFastHandsFill(EntityUid target, EntityUid source, EntityUid user)
    {
        if (!CanFastHandsFill(target, source))
            return false;
        if (TryComp<BallisticAmmoProviderComponent>(target, out var ballistic))
            return TryAmmoInsert(1, source, ballistic, target, user);
        if (!TryComp<RevolverAmmoProviderComponent>(target, out var revolver))
            return false;

        // Clear spent cases only, retaining live rounds in the cylinder.
        for (var i = 0; i < revolver.Capacity; i++)
        {
            var entity = revolver.AmmoSlots[i];
            var spent = entity is { } round
                ? TryComp<CartridgeAmmoComponent>(round, out var spentRound) && spentRound.Spent
                : revolver.Chambers[i] == false;
            if (!spent)
                continue;
            if (entity is { } casing)
            {
                Containers.Remove(casing, revolver.AmmoContainer);
                _xform.SetCoordinates(casing, Transform(target).Coordinates);
            }
            else if (revolver.FillPrototype is { } prototype)
            {
                var spawnedCasing = Spawn(prototype, Transform(target).Coordinates);
                if (TryComp<CartridgeAmmoComponent>(spawnedCasing, out var cartridge))
                    SetCartridgeSpent(spawnedCasing, cartridge, true);
            }
            revolver.AmmoSlots[i] = null;
            revolver.Chambers[i] = null;
        }
        UpdateRevolverAppearance(target, revolver);
        Dirty(target, revolver);
        if (!TryComp<BallisticAmmoProviderComponent>(source, out var provider))
            return TryRevolverInsertAmmo(target, revolver, source, user);
        var ammunition = DoTakeAmmo(1, source, user);
        if (ammunition.Count == 0)
            return false;
        if (ammunition[0].Item1 is { } bullet && TryRevolverInsertAmmo(target, revolver, bullet, user))
            return true;
        DoAmmoInsert(ammunition, provider, source, user);
        return false;
    }

    public void FastHandsChamber(EntityUid uid, EntityUid user)
    {
        if (!TryComp<ChamberMagazineAmmoProviderComponent>(uid, out var chamber))
            return;
        var round = GetChamberEntity(uid);
        if (round != null && (!TryComp<CartridgeAmmoComponent>(round, out var ammo) || !ammo.Spent))
            return;
        if (chamber.BoltClosed == false)
            SetBoltClosed(uid, chamber, true, user);
        else if (chamber.CanRack)
            UseChambered(uid, chamber, user);
    }
}
