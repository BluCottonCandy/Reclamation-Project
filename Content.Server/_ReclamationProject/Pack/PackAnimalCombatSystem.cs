using System.Numerics;
using Content.Shared._ReclamationProject.Pack;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Pack;

public sealed class PackAnimalCombatSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PackAnimalComponent, DamageModifyEvent>(OnDamage);
        SubscribeLocalEvent<PackAnimalComponent, MeleeHitEvent>(OnHit);
    }

    private void OnDamage(EntityUid uid, PackAnimalComponent animal, DamageModifyEvent args)
    {
        if (animal.ArmorModifiers is { } modifiers && _slots.GetItemOrNull(uid, "pack-armor") != null)
            args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, modifiers);
    }

    private void OnHit(EntityUid uid, PackAnimalComponent animal, MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        foreach (var target in args.HitEntities)
        {
            if (!_mobs.IsAlive(target))
                continue;

            if (animal.PowerBite && _timing.CurTime >= animal.NextPowerBite)
            {
                animal.NextPowerBite = _timing.CurTime + TimeSpan.FromMinutes(1);
                var bite = new DamageSpecifier(_prototypes.Index<DamageTypePrototype>("Piercing"), 15);
                _damage.TryChangeDamage(target, bite, ignoreResistances: true, origin: uid);
            }

            if (!animal.Knockback)
                continue;

            var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(uid);
            if (direction.LengthSquared() > 0.001f)
                _throwing.TryThrow(target, Vector2.Normalize(direction) * 2f, baseThrowSpeed: 4f,
                    user: uid, pushbackRatio: 0f, recoil: false, animated: false, playSound: false, doSpin: false);
        }
    }
}
