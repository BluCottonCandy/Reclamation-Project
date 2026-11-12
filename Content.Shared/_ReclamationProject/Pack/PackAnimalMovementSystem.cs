using Content.Shared.Damage;
using Robust.Shared.Physics.Events;

namespace Content.Shared._ReclamationProject.Pack;

public sealed class PackAnimalMovementSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<PackAnimalComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<PackAnimalComponent, ModifySlowOnDamageSpeedEvent>(OnModifyWoundSlowdown);
    }

    private void OnPreventCollide(Entity<PackAnimalComponent> animal, ref PreventCollideEvent args)
    {
        if (HasComp<PackAnimalComponent>(args.OtherEntity))
            args.Cancelled = true;
    }

    private static void OnModifyWoundSlowdown(Entity<PackAnimalComponent> animal, ref ModifySlowOnDamageSpeedEvent args)
    {
        args.Speed = 1f - (1f - args.Speed) * 0.5f;
    }
}
