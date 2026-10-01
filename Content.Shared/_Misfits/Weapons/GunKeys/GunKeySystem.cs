using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared._Misfits.Weapons.GunKeys;

// #Misfits Add - Keybinds for quick magazine eject (J) and bolt toggle (L) on the held gun.
public sealed class GunKeySystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.EjectMagazine,
                InputCmdHandler.FromDelegate(OnEjectMagazine, handle: false))
            .Bind(ContentKeyFunctions.ToggleBoltAction,
                InputCmdHandler.FromDelegate(OnToggleBolt, handle: false))
            .Register<GunKeySystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<GunKeySystem>();
    }

    private void OnEjectMagazine(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { } entity)
            return;

        if (!_hands.TryGetActiveItem(entity, out var heldItem) || heldItem == null)
            return;

        // Only eject if the held item has a magazine slot
        if (!HasComp<MagazineAmmoProviderComponent>(heldItem.Value))
            return;

        _slots.TryEject(heldItem.Value, "gun_magazine", entity, out _);
    }

    private void OnToggleBolt(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { } entity)
            return;

        if (!_hands.TryGetActiveItem(entity, out var heldItem) || heldItem == null)
            return;

        if (!TryComp<ChamberMagazineAmmoProviderComponent>(heldItem.Value, out var chamberComp))
            return;

        _gun.ToggleBolt(heldItem.Value, chamberComp, entity);
    }
}
