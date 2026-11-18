// #Reclamation Project - medieval weapons: the shepherd's sling throws bullets like stones instead of firing them.
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._ReclamationProject.Medieval;

public sealed class SlingThrowSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlingComponent, AttemptShootEvent>(OnAttemptShoot);
    }

    private void OnAttemptShoot(EntityUid uid, SlingComponent comp, ref AttemptShootEvent args)
    {
        // same switch the pneumatic cannon uses: ammo is hurled, never fired. The fustibalus fires its bullets.
        if (!comp.FireCartridges)
            args.ThrowItems = true;
    }
}
