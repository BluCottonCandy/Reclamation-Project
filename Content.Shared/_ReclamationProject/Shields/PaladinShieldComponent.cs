using Content.Shared.Actions;
using Content.Shared.Damage;

namespace Content.Shared._ReclamationProject.Shields;

[RegisterComponent]
public sealed partial class PaladinShieldComponent : Component
{
    [DataField] public bool Damaged;
    [DataField] public float WearFraction = 0.65f;
    [DataField] public string BrokenPrototype = "ReclamationDamagedPaladinShield";
    [DataField] public string RepairedPrototype = "ReclamationPaladinShield";
    [DataField] public bool StaminaImmune;
    [DataField] public float DamageCoefficient = 0.7f;
    [DataField] public float ChargeDamage = 40f;
    [DataField] public float StaminaDamage = 10f;
    [DataField] public EntityUid? ChargeAction;
}

public sealed partial class PaladinShieldChargeEvent : WorldTargetActionEvent;

[ByRefEvent]
public readonly record struct ShieldWearEvent(DamageSpecifier Damage);
