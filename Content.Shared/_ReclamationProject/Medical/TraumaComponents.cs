using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;

namespace Content.Shared._ReclamationProject.Medical;

[Serializable, NetSerializable]
public enum TraumaKind { Fracture, InternalBleeding, Lungs, Heart, Liver, Kidneys, Eyes, Stomach, LimbDamage }

/// <summary>Persistent structural injuries, independent of ordinary healable damage.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TraumaBodyComponent : Component
{
    [DataField, AutoNetworkedField] public float InternalBleeding;
    [DataField, AutoNetworkedField] public float LungDamage;
    [DataField, AutoNetworkedField] public float HeartDamage;
    [DataField, AutoNetworkedField] public float LiverDamage;
    [DataField, AutoNetworkedField] public float KidneyDamage;
    [DataField, AutoNetworkedField] public float EyeDamage;
    [DataField, AutoNetworkedField] public float StomachDamage;
    [DataField] public Dictionary<TraumaKind, int> OriginalOrgans = new();
    [DataField] public float AppliedStamina = 1f;
    [DataField] public float AppliedRecovery = 1f;
    [DataField] public float AppliedHeart = 1f;
    [DataField] public float PreviousHunger;
    public float LastMedXAmount;
    public int PendingMedXDoses;
    public bool UpdatingMedX;
    public bool SuppressInjuryRolls;
    public float TickAccumulator;
    public float SymptomAccumulator;
}

/// <summary>Damage follows an organ when removed or transplanted. Brains never receive this component.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TraumaOrganComponent : Component
{
    [DataField, AutoNetworkedField] public TraumaKind Kind;
    [DataField, AutoNetworkedField] public float Damage;
    [DataField, AutoNetworkedField] public float MaximumHealth = 100f;
    [DataField] public bool FailureRecorded;
    public bool Detaching;
    /// <summary>
    /// #Reclamation Project - Maximum health that Institute nanites rebuilt after a failure.
    /// Medical scans still subtract it, so the scar reads exactly like a human's while the part keeps full health.
    /// </summary>
    [DataField] public float HiddenScarLoss;

    public float CurrentHealth => Math.Clamp(MaximumHealth - Damage, 0f, MaximumHealth);
    public float DamagePercent => MaximumHealth <= 0f ? 100f : Math.Clamp(Damage / MaximumHealth * 100f, 0f, 100f);
    [DataField] public float AppliedMetabolism = 1f;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class TraumaLimbComponent : Component
{
    [DataField, AutoNetworkedField] public float TemporaryHealth;
    [DataField, AutoNetworkedField] public float MaximumHealth = 100f;
    [DataField, AutoNetworkedField] public float OriginalFailureThreshold;
    [DataField] public float AppliedMaximumHealth = 100f;
    [DataField] public bool FailureRecorded;
    /// <summary>
    /// #Reclamation Project - Maximum health that Institute nanites rebuilt after a failure.
    /// Medical scans still subtract it, so the scar reads exactly like a human's while the part keeps full health.
    /// </summary>
    [DataField] public float HiddenScarLoss;
}

[ByRefEvent]
public record struct TraumaLimbStateChangeEvent(bool Enabled);

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FractureComponent : Component
{
    [DataField, AutoNetworkedField] public bool Splinted;
    [DataField, AutoNetworkedField] public float HealingProgress;
}

[RegisterComponent]
public sealed partial class TraumaRegenerationComponent : Component;

/// <summary>Used on both a surgery and its final repair step.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TraumaRepairComponent : Component
{
    [DataField, AutoNetworkedField] public TraumaKind Kind;
}

[RegisterComponent]
public sealed partial class TraumaSplintComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class TraumaWalkingSupportComponent : Component;

[ByRefEvent]
public record struct GetBodyStandingSupportEvent(int AdditionalLegs = 0);

[ByRefEvent]
public record struct TraumaRepairEvent(EntityUid Part, TraumaKind Kind);

[ByRefEvent]
public record struct NutritionAbsorptionEvent(float Amount);

[Serializable, NetSerializable]
public sealed partial class TraumaSplintDoAfterEvent : SimpleDoAfterEvent
{
    public NetEntity Part;

    public override DoAfterEvent Clone() => new TraumaSplintDoAfterEvent { Part = Part };
}
