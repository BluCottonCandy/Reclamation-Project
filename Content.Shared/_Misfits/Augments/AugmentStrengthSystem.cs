// #Misfits Add - Applies AugmentStrengthComponent's melee damage modifier.
// Ported and adapted from Goob-Station (Content.Goobstation.Shared.Augments.AugmentStrengthSystem).
// Adaptation note: upstream uses GetUserMeleeDamageEvent (raised on body).
// N14 raises GetMeleeDamageEvent on the weapon with args.User set to the attacker,
// so we check the attacker's installed augments here instead.

using Content.Shared.Item.ItemToggle;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._Misfits.Augments;

public sealed class AugmentStrengthSystem : EntitySystem
{
    [Dependency] private readonly ItemToggleSystem _toggle = default!;

    private EntityQuery<InstalledAugmentsComponent> _installedQuery;

    public override void Initialize()
    {
        base.Initialize();

        _installedQuery = GetEntityQuery<InstalledAugmentsComponent>();

        // GetMeleeDamageEvent fires on the weapon entity; args.User is the attacker.
        SubscribeLocalEvent<GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    private void OnGetMeleeDamage(ref GetMeleeDamageEvent args)
    {
        // Check if the wielder has any cyberware installed.
        if (!_installedQuery.TryComp(args.User, out var installed))
            return;

        foreach (var netEnt in installed.InstalledAugments)
        {
            var aug = GetEntity(netEnt);

            if (!TryComp<AugmentStrengthComponent>(aug, out var strength))
                continue;

            // IsActivated returns true if no ItemToggleComponent present (always-on passive).
            if (!_toggle.IsActivated(aug))
                continue;

            args.Damage *= strength.Modifier;
        }
    }
}
