using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared._ReclamationProject.Pack;

namespace Content.Server._ReclamationProject.Pack;

/// <summary>Re-nocks bows from real arrows in the user's equipped quiver.</summary>
public sealed class PackBowAutoloadSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedGunSystem _guns = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PackCompoundBowComponent, AttemptShootEvent>(OnAttempt, after: new[] { typeof(GunAgilityRequirementSystem) });
        SubscribeLocalEvent<PackCompoundBowComponent, GunShotEvent>(OnShot);
    }

    private bool IsBow(EntityUid uid) => HasComp<GunComponent>(uid) && HasComp<PackCompoundBowComponent>(uid);

    private void OnAttempt(EntityUid uid, PackCompoundBowComponent component, ref AttemptShootEvent args)
    {
        if (!args.Cancelled && IsBow(uid))
            TryLoadNext(args.User, uid, onlyWhenEmpty: true);
    }

    private void OnShot(EntityUid uid, PackCompoundBowComponent component, ref GunShotEvent args)
    {
        if (args.Ammo.Count > 0 && IsBow(uid))
            TryLoadNext(args.User, uid);
    }

    public bool TryLoadNext(EntityUid user, EntityUid bow, bool onlyWhenEmpty = false)
    {
        if (!IsBow(bow) ||
            !_inventory.TryGetSlotEntity(user, "belt", out var quiver) ||
            !HasComp<BowQuiverComponent>(quiver) ||
            !TryComp<StorageComponent>(quiver, out var storage))
            return false;

        if (TryComp<BallisticAmmoProviderComponent>(bow, out var ballistic))
        {
            if (ballistic.AmmoCount >= ballistic.Capacity || onlyWhenEmpty && ballistic.AmmoCount > 0)
                return false;
        }
        else if (TryComp<Content.Shared.Containers.ItemSlots.ItemSlotsComponent>(bow, out var slots))
        {
            if (!slots.Slots.TryGetValue("projectiles", out var slot) || slot.HasItem)
                return false;
        }
        else
            return false;

        foreach (var arrow in storage.Container.ContainedEntities)
        {
            if (!_tags.HasTag(arrow, "Arrow") || !TryComp<AmmoComponent>(arrow, out var ammo))
                continue;

            if (ballistic != null)
            {
                _guns.DoAmmoInsert(new List<(EntityUid?, IShootable)> { (arrow, ammo) }, ballistic, bow, user);
                return Transform(arrow).ParentUid == bow;
            }

            return _slots.TryInsert(bow, "projectiles", arrow, user);
        }

        return false;
    }
}
