using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._ReclamationProject.Blindness;

/// <summary>The Sneak trait cannot be detected through expanded auditory awareness.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class QuietAwarenessComponent : Component
{
    public const float EchoSeconds = 2f;

    [DataField, AutoNetworkedField]
    public TimeSpan LastShotAt;

    [DataField, AutoNetworkedField]
    public EntityCoordinates LastShotCoordinates;

    [DataField, AutoNetworkedField]
    public Angle LastShotRotation;

    // Client bookkeeping; not replicated back to the server.
    public TimeSpan LastRenderedShot;
}
