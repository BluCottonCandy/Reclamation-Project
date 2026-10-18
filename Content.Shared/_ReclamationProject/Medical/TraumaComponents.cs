using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;

namespace Content.Shared._ReclamationProject.Medical;

[Serializable, NetSerializable]
public enum TraumaKind { Fracture, InternalBleeding, Lungs, Heart, Liver, Kidneys, Eyes, Stomach }

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
    [DataField] public float AppliedMetabolism = 1f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class TraumaLimbComponent : Component;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FractureComponent : Component
{
    [DataField, AutoNetworkedField] public bool Splinted;
}

/// <summary>Used on both a surgery and its final repair step.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TraumaRepairComponent : Component
{
    [DataField, AutoNetworkedField] public TraumaKind Kind;
}

[RegisterComponent]
public sealed partial class TraumaSplintComponent : Component;

[ByRefEvent]
public record struct TraumaRepairEvent(EntityUid Part, TraumaKind Kind);

[ByRefEvent]
public record struct NutritionAbsorptionEvent(float Amount);

[Serializable, NetSerializable]
public sealed partial class TraumaSplintDoAfterEvent : SimpleDoAfterEvent;
