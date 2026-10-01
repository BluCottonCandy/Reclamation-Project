// Drains the robot's chassis power cell each time its built-in gun fires.
// Server-only because BatterySystem is server-only.

using Content.Server.Power.EntitySystems;
using Content.Shared._Misfits.Robot;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server._Misfits.Robot;

public sealed class RobotGunPowerDrainSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly BatterySystem _battery = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RobotGunPowerDrainComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(EntityUid uid, RobotGunPowerDrainComponent comp, ref GunShotEvent args)
    {
        if (comp.DrainPerShot <= 0f)
            return;

        var cellEntity = _itemSlots.GetItemOrNull(uid, comp.CellSlotId);
        if (cellEntity == null)
            return;

        _battery.UseCharge(cellEntity.Value, comp.DrainPerShot);
    }
}
