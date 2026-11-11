using Content.Shared._Misfits.Special;
using Content.Shared.Examine;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._ReclamationProject.Pack;

public sealed class GunAgilityRequirementSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunAgilityRequirementComponent, AttemptShootEvent>(OnAttempt);
        SubscribeLocalEvent<GunAgilityRequirementComponent, ExaminedEvent>(OnExamined);
    }

    private void OnAttempt(Entity<GunAgilityRequirementComponent> ent, ref AttemptShootEvent args)
    {
        if (args.Cancelled || _special.HasRequirement(args.User, SpecialStat.Agility, ent.Comp.Minimum))
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString("pack-bow-agility-blocked", ("agility", ent.Comp.Minimum));
    }

    private void OnExamined(Entity<GunAgilityRequirementComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("pack-bow-agility-examine", ("agility", ent.Comp.Minimum)));
    }
}
