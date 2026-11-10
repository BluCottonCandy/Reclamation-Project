using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._ReclamationProject.Pack;

/// <summary>Load actual arrows, whose Ammo component is not a cartridge or magazine.</summary>
public sealed class PackCompoundBowSystem : EntitySystem
{
    [Dependency] private readonly SharedGunSystem _guns = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PackCompoundBowComponent, InteractUsingEvent>(OnInsert, before: new[] { typeof(SharedGunSystem) });
        SubscribeLocalEvent<PackCompoundBowComponent, AfterInteractEvent>(OnScoop, before: new[] { typeof(SharedGunSystem) });
    }

    private void OnInsert(EntityUid ent, PackCompoundBowComponent component, InteractUsingEvent args)
    {
        if (!args.Handled && _tags.HasTag(args.Used, "Arrow"))
            args.Handled = Insert(ent, args.Used, args.User);
    }

    private void OnScoop(EntityUid ent, PackCompoundBowComponent component, AfterInteractEvent args)
    {
        if (!args.Handled && args.CanReach && args.Target is { } arrow && _tags.HasTag(arrow, "Arrow"))
            args.Handled = Insert(ent, arrow, args.User);
    }

    private bool Insert(EntityUid bow, EntityUid arrow, EntityUid user)
    {
        if (!TryComp<BallisticAmmoProviderComponent>(bow, out var provider) ||
            !TryComp<AmmoComponent>(arrow, out var ammo))
            return false;
        if (provider.AmmoCount >= provider.Capacity)
        {
            _popup.PopupPredicted(Loc.GetString("pack-compound-full"), bow, user);
            return true;
        }
        _guns.DoAmmoInsert(new List<(EntityUid?, IShootable)> { (arrow, ammo) }, provider, bow, user);
        return true;
    }
}
