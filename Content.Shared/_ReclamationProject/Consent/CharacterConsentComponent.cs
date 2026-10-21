using Content.Shared.Preferences;
using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Consent;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CharacterConsentComponent : Component
{
    [DataField, AutoNetworkedField]
    public ConsentPreference Torture;

    [DataField, AutoNetworkedField]
    public ConsentPreference Slavery;
}
