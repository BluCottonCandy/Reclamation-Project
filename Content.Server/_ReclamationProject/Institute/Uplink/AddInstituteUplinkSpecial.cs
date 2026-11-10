using Content.Shared.Roles;
using JetBrains.Annotations;

namespace Content.Server._ReclamationProject.Institute.Uplink;

/// <summary>
/// #Reclamation Project - puts the Institute uplink implant into the new character,
/// loaded with this job's starting Research Points.
/// </summary>
[UsedImplicitly]
public sealed partial class AddInstituteUplinkSpecial : JobSpecial
{
    [DataField(required: true)]
    public int Balance;

    public override void AfterEquip(EntityUid mob)
    {
        IoCManager.Resolve<IEntityManager>().System<InstituteUplinkSystem>().GiveUplink(mob, Balance);
    }
}
