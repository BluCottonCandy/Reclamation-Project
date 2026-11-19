// #Misfits Change Add: Marker component placed on power armor item entities.
// Used by CarryingSystem to prevent non-power-armor users from picking up power-armor wearers.
// Dragging / pulling mechanics are unaffected.

using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.PowerArmor;

/// <summary>
///     Marker component for Nuclear-14 power armor items (T-45, T-51, T-60, X-01, X-02, etc.).
///     When a character has this component's parent item equipped in the outerClothing slot,
///     they are considered "in power armor" for gameplay purposes such as carry restrictions.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14PowerArmorComponent : Component
{
    /// <summary>
    ///     Powered frames require at least Strength 3. Salvaged armor opts out using its original serialized field.
    /// </summary>
    [DataField("requiresProficiency"), AutoNetworkedField]
    public bool HasServoFrame = true;
}
