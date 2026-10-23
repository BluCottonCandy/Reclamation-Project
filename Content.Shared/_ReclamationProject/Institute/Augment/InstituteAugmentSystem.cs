using Content.Shared.Damage;

namespace Content.Shared._ReclamationProject.Institute.Augment;

public sealed class InstituteAugmentSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InstituteAugmentComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnDamageModify(EntityUid uid, InstituteAugmentComponent component, DamageModifyEvent args)
    {
        // Only shrink damage; negative entries are healing and pass through untouched.
        var reduced = new DamageSpecifier(args.Damage);
        foreach (var (type, amount) in args.Damage.DamageDict)
        {
            if (amount > 0)
                reduced.DamageDict[type] = amount * component.DamageMultiplier;
        }

        args.Damage = reduced;
    }
}
