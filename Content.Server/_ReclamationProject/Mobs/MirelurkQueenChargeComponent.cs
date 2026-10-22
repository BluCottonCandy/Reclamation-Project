using Robust.Shared.Map;
using Content.Shared.Damage;
using System.Numerics;

namespace Content.Server._ReclamationProject.Mobs;

/// <summary>Boss AI use of the existing melee dash and forward sweep.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class MirelurkQueenChargeComponent : Component
{
    [DataField] public bool Enraged;
    [DataField] public float EnrageDamageThreshold = 500f;
    [DataField] public float Cooldown = 4f;
    [DataField] public float Windup = 1f;
    [DataField] public float Speed = 10f;
    [DataField] public float Range = 3.5f;
    [DataField] public float MinimumRange = 1f;
    [DataField] public float StaminaDamage = 100f;
    /// <summary>Overrides the queen's impact damage with a configurable physical sweep.</summary>
    [DataField] public DamageSpecifier? ImpactDamage;
    [DataField, AutoPausedField] public TimeSpan NextCharge;
    [DataField, AutoPausedField] public TimeSpan WindupEnd;
    [DataField, AutoPausedField] public TimeSpan DashEnd;
    public bool WindingUp;
    public bool Dashing;
    public EntityUid Target;
    public MapCoordinates TargetPosition;
    public MapCoordinates DashStart;
    public Vector2 DashDirection;
    public bool ImpactApplied;
    public bool StopPending;
}
