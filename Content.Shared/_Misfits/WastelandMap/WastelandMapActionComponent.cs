// #Misfits Change - Power armor tactical map action support
using Content.Shared.Inventory;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.WastelandMap;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(WastelandMapActionSystem))]
public sealed partial class WastelandMapActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId Action = "ActionOpenWastelandMap";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField("requiredSlot"), AutoNetworkedField]
    public SlotFlags RequiredSlot = SlotFlags.HEAD;
}