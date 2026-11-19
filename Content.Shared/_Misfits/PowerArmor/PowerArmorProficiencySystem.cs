using Content.Shared.Inventory.Events;
using Content.Shared._Misfits.Special;

namespace Content.Shared._Misfits.PowerArmor;

/// <summary>
///     Checks the wearer's effective Strength before they put on a servo frame.
/// </summary>
public sealed class PowerArmorProficiencySystem : EntitySystem
{
    [Dependency] private SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Subscribe to equip attempts on power armor items specifically.
        SubscribeLocalEvent<N14PowerArmorComponent, BeingEquippedAttemptEvent>(OnEquipAttempt);
    }

    private void OnEquipAttempt(Entity<N14PowerArmorComponent> item, ref BeingEquippedAttemptEvent args)
    {
        if (!item.Comp.HasServoFrame || args.Slot != "outerClothing" ||
            _special.HasRequirement(args.EquipTarget, SpecialStat.Strength, 3))
            return;

        // Deny the equip and show a localised popup message.
        args.Reason = "pa-strength-required";
        args.Cancel();
    }
}
