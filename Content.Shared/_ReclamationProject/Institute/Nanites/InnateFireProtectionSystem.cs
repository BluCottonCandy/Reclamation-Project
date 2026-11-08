using Content.Shared.Atmos;

namespace Content.Shared._ReclamationProject.Institute.Nanites;

public sealed class InnateFireProtectionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InnateFireProtectionComponent, GetFireProtectionEvent>(OnGetProtection);
    }

    private void OnGetProtection(EntityUid uid, InnateFireProtectionComponent comp, GetFireProtectionEvent args)
    {
        args.Reduce(comp.Reduction);
    }
}
