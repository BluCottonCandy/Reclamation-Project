using Content.Shared.Actions.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Components;

namespace Content.Shared._ReclamationProject.PowerArmor;

public sealed class ArmorMagLockSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<ArmorMagLockComponent, RefreshMovementSpeedModifiersEvent>((_, _, ev) => ev.FinalMultiplier *= 0.9f);
        SubscribeLocalEvent<ArmorMagLockComponent, DisarmAttemptEvent>((uid, _, ev) =>
        {
            if (ev.TargetUid == uid)
                ev.Cancel();
        });
        SubscribeLocalEvent<ArmorMagLockComponent, AttemptMobCollideEvent>(
            (Entity<ArmorMagLockComponent> _, ref AttemptMobCollideEvent ev) => ev.Cancelled = true);
        SubscribeLocalEvent<ArmorMagLockComponent, AttemptMeleeThrowOnHitEvent>(
            (Entity<ArmorMagLockComponent> ent, ref AttemptMeleeThrowOnHitEvent ev) =>
            {
                if (ent.Owner == ev.Hit)
                    ev = ev with { Cancelled = true, Handled = true };
            });
        SubscribeLocalEvent<ArmorMagLockComponent, ThrowPushbackAttemptEvent>((_, _, ev) => ev.Cancel());
    }
}
