using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Shared._ReclamationProject.Roofs;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class ConstructedRoofComponent : Component
{
    [DataField]
    public TimeSpan SupportGrace = TimeSpan.FromSeconds(30);

    [DataField(serverOnly: true, customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? CollapseAt;

    // Set before refunding so a floor change and a support update cannot refund twice.
    public bool Removing;
}

/// <summary>Anchored wooden supports can carry connected constructed roof sections.</summary>
[RegisterComponent]
public sealed partial class RoofSupportComponent : Component;
