using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._ReclamationProject.Institute.Nanites;

/// <summary>
/// Synthetic muscle: multiplies all melee damage the owner deals (fists and held melee weapons)
/// and the damage of anything they throw. Shows nothing on examine of the owner, so it doesn't give a synth away.
/// </summary>
[RegisterComponent]
public sealed partial class SyntheticStrengthComponent : Component
{
    /// <summary>Melee and thrown damage multiplier (1.65 = +65%).</summary>
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

        // #Reclamation Project - thrown damage is only raised on the thrown item, so check the thrower here.
        SubscribeLocalEvent<DamageOtherOnHitComponent, GetThrowingDamageEvent>(OnGetThrowingDamage);
    }

    private void OnGetMeleeDamage(Entity<SyntheticStrengthComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (args.User != ent.Owner)
            return;

        args.Damage *= ent.Comp.Multiplier;
    }

    private void OnGetThrowingDamage(Entity<DamageOtherOnHitComponent> ent, ref GetThrowingDamageEvent args)
    {
        if (args.User is not { } thrower || !TryComp<SyntheticStrengthComponent>(thrower, out var strength))
            return;

        args.Damage *= strength.Multiplier;
    }
}
