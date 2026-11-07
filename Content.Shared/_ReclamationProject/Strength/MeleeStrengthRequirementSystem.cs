using Content.Shared._Misfits.Special;
using Content.Shared.Examine;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._ReclamationProject.Strength;

public sealed class MeleeStrengthRequirementSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MeleeStrengthRequirementComponent, AttemptMeleeEvent>(OnAttempt);
        SubscribeLocalEvent<MeleeStrengthRequirementComponent, ExaminedEvent>(OnExamined);
    }

    private void OnAttempt(Entity<MeleeStrengthRequirementComponent> ent, ref AttemptMeleeEvent args)
    {
        if (args.Cancelled || _special.HasRequirement(args.PlayerUid, SpecialStat.Strength, ent.Comp.Minimum))
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString("melee-strength-requirement-blocked", ("strength", ent.Comp.Minimum));
    }

    private void OnExamined(Entity<MeleeStrengthRequirementComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("melee-strength-requirement-examine", ("strength", ent.Comp.Minimum)));
    }
}
