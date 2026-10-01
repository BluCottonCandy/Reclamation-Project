// #Misfits Change /Add/ - Runtime state for sentry bot lighting and overload warning emitters.
using Robust.Shared.GameStates;

namespace Content.Server._Misfits.Robot;

[RegisterComponent]
public sealed partial class SentryBotOverloadLightComponent : Component
{
    [DataField]
    public bool SteadyLightEnabled = true;

    [DataField]
    public float FlashInterval = 0.35f;

    [ViewVariables]
    public bool Overloading;

    [ViewVariables]
    public bool FlashState = true;

    [ViewVariables]
    public TimeSpan NextFlashTime = TimeSpan.Zero;

    [ViewVariables]
    public List<EntityUid> OverloadLightEmitters = new();
}