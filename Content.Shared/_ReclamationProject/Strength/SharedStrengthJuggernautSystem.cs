using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
namespace Content.Shared._ReclamationProject.Strength;

/// <summary>Uses the existing melee damage modifiers, including structural damage, once per attack.</summary>
public sealed class SharedStrengthJuggernautSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private DamageModifierSet? _bonus;

    public bool IsActive(EntityUid user) =>
        TryComp<StrengthJuggernautComponent>(user, out var ability) && ability.EndsAt > _timing.CurTime;

    public void ApplyDamageBonus(MeleeHitEvent args)
    {
        if (!IsActive(args.User))
            return;
        _bonus ??= new DamageModifierSet
        {
            Coefficients = _prototypes.EnumeratePrototypes<DamageTypePrototype>()
                .ToDictionary(type => type.ID, _ => 1.25f)
        };
        args.ModifiersList.Add(_bonus);
    }
}
