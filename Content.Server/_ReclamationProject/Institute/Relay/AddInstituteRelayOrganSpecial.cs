using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Institute.Relay;

/// <summary>
/// Puts an Institute relay implant into the new character's head.
/// List this after the special that adds the Institute faction, or the implant fries them.
/// </summary>
[UsedImplicitly]
public sealed partial class AddInstituteRelayOrganSpecial : JobSpecial
{
    [DataField(required: true)]
    public EntProtoId Implant;

    public override void AfterEquip(EntityUid mob)
    {
        IoCManager.Resolve<IEntityManager>().System<InstituteRelaySystem>().TryAddRelayOrgan(mob, Implant);
    }
}
