using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._ReclamationProject.Institute.Nanites;

/// <summary>
/// Synthetic muscle: multiplies all melee damage the owner deals, both fists and held melee weapons.
/// Shows nothing on examine, so it doesn't give a synth away.
/// </summary>
[RegisterComponent]
public sealed partial class SyntheticStrengthComponent : Component
{
    /// <summary>Melee damage multiplier (1.25 = +25%).</summary>
    [DataField]
    public float Multiplier = 1.25f;
}

public sealed class SyntheticStrengthSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        // Raised on the weapon, and again on the attacker when the weapon isn't the attacker,
        // so this catches unarmed hits (weapon == attacker) and held weapons exactly once each.
        SubscribeLocalEvent<SyntheticStrengthComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    private void OnGetMeleeDamage(Entity<SyntheticStrengthComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (args.User != ent.Owner)
            return;

        args.Damage *= ent.Comp.Multiplier;
    }
}
