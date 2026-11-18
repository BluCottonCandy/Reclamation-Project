// #Reclamation Project - medieval weapons: weapons that change look when toggled, wielded or loaded.
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Medieval;

/// <summary>
/// Swaps a medieval weapon's sprite (and in-hand sprite) when it is toggled on with ItemToggle,
/// e.g. a lit sword, an open folding knife or a charged mace.
/// The world sprite reads <see cref="MedievalLookVisuals.State"/> through a GenericVisualizer.
/// In-hand states are named "{state}-inhand-left" and "{state}-wielded-inhand-left".
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MedievalLookComponent : Component
{
    /// <summary>Sprite state while toggled on. Null means the item has no toggle look.</summary>
    [DataField]
    public string? OnState;

    /// <summary>Sprite state while toggled on AND wielded (the sword of state's full blaze).</summary>
    [DataField]
    public string? WieldedOnState;
}

[Serializable, NetSerializable]
public enum MedievalLookVisuals : byte
{
    State,
}
