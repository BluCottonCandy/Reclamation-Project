namespace Content.Server._ReclamationProject.Institute.Relay;

/// <summary>
/// On a body while it rides inside an Institute relay signal.
/// </summary>
[RegisterComponent]
public sealed partial class InstituteRelayRiderComponent : Component
{
    [ViewVariables]
    public EntityUid? Eye;
}
