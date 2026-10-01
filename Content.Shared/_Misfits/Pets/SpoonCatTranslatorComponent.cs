// #Misfits Add - Spoon cat translator action component and event.

using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Pets;

[RegisterComponent]
public sealed partial class SpoonCatTranslatorComponent : Component
{
    [DataField]
    public EntProtoId ActionId = "ActionSpoonCatTranslator";

    [DataField]
    public EntityUid? ActionEntity;
}

public sealed partial class SpoonCatTranslatorActionEvent : EntityTargetActionEvent;
