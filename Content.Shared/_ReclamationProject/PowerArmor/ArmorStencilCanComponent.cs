using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.PowerArmor;

[RegisterComponent]
public sealed partial class ArmorStencilCanComponent : Component
{
    [DataField(required: true)] public List<EntProtoId> Stencils = new();

    // The clicked item can be a worn attachment backed by a removable plate.
    public (EntityUid Clicked, EntityUid Surface)? Selection;
}

[Serializable, NetSerializable]
public sealed partial class SprayArmorStencilEvent : DoAfterEvent
{
    [DataField] public NetEntity Surface;
    [DataField] public EntProtoId Stencil;

    public override DoAfterEvent Clone() => new SprayArmorStencilEvent { Surface = Surface, Stencil = Stencil };
}
