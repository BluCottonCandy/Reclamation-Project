using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._ReclamationProject.Shields;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ShieldChargeComponent : Component
{
    [DataField, AutoPausedField] public TimeSpan NextCharge;
    [DataField, AutoPausedField] public TimeSpan End;
    public EntityUid Shield;
    public MapCoordinates Start;
    public Vector2 Direction;
    public bool Active;
    public bool ImpactApplied;
    public bool StopPending;
}

/// <summary>Tracks shield carriers for explosion handling without replacing normal hand behavior.</summary>
[RegisterComponent]
public sealed partial class ShieldCarrierComponent : Component;
