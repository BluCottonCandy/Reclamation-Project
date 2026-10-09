// #Reclamation Project Add - Crank-charged guns (Minutemen laser musket).
// Each crank adds one charge, up to MaxCharges. The next shot gets the bonus damage for that
// many charges, then the count resets. Each crank draws part of a shot's charge from the cell.
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Weapons.Ranged;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CrankChargeComponent : Component
{
    /// <summary>
    /// How many times the gun has been cranked since its last shot.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Charges;

    /// <summary>
    /// Hard cap on cranks.
    /// </summary>
    [DataField]
    public int MaxCharges = 6;

    /// <summary>
    /// How long one crank takes.
    /// </summary>
    [DataField]
    public TimeSpan CrankDelay = TimeSpan.FromSeconds(1.8);

    [DataField]
    public SoundSpecifier? CrankSound;

    /// <summary>
    /// Fraction of the inserted cell's fire cost that each crank uses up.
    /// </summary>
    [DataField]
    public float CrankCostFraction = 0.5f;

    [DataField]
    public ProtoId<DamageTypePrototype> BonusDamageType = "Heat";

    /// <summary>
    /// Bonus damage added on top of the beam's own damage, by crank count:
    /// entry 0 is for 1 crank, entry 1 for 2 cranks, and so on up to MaxCharges.
    /// </summary>
    [DataField]
    public List<FixedPoint2> BonusByCharge = new();

    /// <summary>Optional projectile spread fired instead of the inserted cell's beam.</summary>
    [DataField]
    public EntProtoId? ProjectilePrototype;

    /// <summary>Total uncranked damage across all pellets, used by examine.</summary>
    [DataField]
    public FixedPoint2 ProjectileBaseDamage;

}

[Serializable, NetSerializable]
public sealed partial class CrankChargeDoAfterEvent : SimpleDoAfterEvent
{
}
