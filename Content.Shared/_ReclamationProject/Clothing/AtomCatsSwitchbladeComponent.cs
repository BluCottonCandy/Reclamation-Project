using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Clothing;

[RegisterComponent, NetworkedComponent]
public sealed partial class AtomCatsSwitchbladeComponent : Component
{
    [DataField(required: true)]
    public ComponentRegistry Blade = new();

    [DataField(required: true)]
    public ComponentRegistry Comb = new();
}

[Serializable, NetSerializable]
public enum AtomCatsSwitchbladeVisuals : byte
{
    Mode,
}
