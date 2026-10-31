using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Shared._ReclamationProject.Charisma;

/// <summary>A snapshot Rally buff, independent of faction warcries and their expiry.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class CharismaRallyBuffComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan ExpiresAt;
}
