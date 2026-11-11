using Content.Shared._Misfits.PowerArmor; // #Reclamation Project
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._Misfits.SpecialStats.Components;
using Content.Shared.Clothing;
using Content.Shared.Hands;
using Content.Shared.Movement.Systems;

namespace Content.Shared.Item;

/// <summary>
/// This handles <see cref="HeldSpeedModifierComponent"/>
/// </summary>
public sealed class HeldSpeedModifierSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    /// <summary>
    /// #Reclamation Project - speed while a Strength 10 character carries an unworn power armour suit.
    /// Everyone else carries it at the suit's own held modifier (50%).
    /// </summary>
    private const float StrongPowerArmorCarryModifier = 0.65f;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<HeldSpeedModifierComponent, GotEquippedHandEvent>(OnGotEquippedHand);
        SubscribeLocalEvent<HeldSpeedModifierComponent, GotUnequippedHandEvent>(OnGotUnequippedHand);
        SubscribeLocalEvent<HeldSpeedModifierComponent, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnRefreshMovementSpeedModifiers);
    }

    private void OnGotEquippedHand(Entity<HeldSpeedModifierComponent> ent, ref GotEquippedHandEvent args)
    {
        _movementSpeedModifier.RefreshMovementSpeedModifiers(args.User);
    }

    private void OnGotUnequippedHand(Entity<HeldSpeedModifierComponent> ent, ref GotUnequippedHandEvent args)
    {
        _movementSpeedModifier.RefreshMovementSpeedModifiers(args.User);
    }

    private void OnRefreshMovementSpeedModifiers(EntityUid uid, HeldSpeedModifierComponent component, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        var walkMod = component.WalkModifier;
        var sprintMod = component.SprintModifier;
        if (component.MirrorClothingModifier && TryComp<ClothingSpeedModifierComponent>(uid, out var clothingSpeedModifier))
        {
            walkMod = clothingSpeedModifier.WalkModifier;
            sprintMod = clothingSpeedModifier.SprintModifier;
        }

        // #Misfits Add - strong holders can keep configurable partial slowdown on heavy bags.
        ApplyStrengthSlowdown(args.Holder, uid, ref walkMod, ref sprintMod);

        args.Args.ModifySpeed(walkMod, sprintMod);
    }

    /// <summary>
    /// #Misfits Add - Applies Strength-gated slowdown relief for held items.
    /// </summary>
    private void ApplyStrengthSlowdown(EntityUid user, EntityUid held, ref float walkMod, ref float sprintMod)
    {
        // #Reclamation Project - Strength 10 lightens a carried power armour suit, but never to full speed.
        if (HasComp<N14PowerArmorComponent>(held))
        {
            if (_special.HasRequirement(user, SpecialStat.Strength, 10))
            {
                walkMod = MathF.Max(StrongPowerArmorCarryModifier, walkMod);
                sprintMod = MathF.Max(StrongPowerArmorCarryModifier, sprintMod);
            }

            return;
        }

        if (_special.HasRequirement(user, SpecialStat.Strength, 10))
        {
            walkMod = MathF.Max(1f, walkMod);
            sprintMod = MathF.Max(1f, sprintMod);
            return;
        }

        if (!TryComp<StrengthIgnoreClothingSlowdownComponent>(held, out var ignore) ||
            !TryComp<SpecialComponent>(user, out var special))
        {
            return;
        }

        if (_special.GetEffective(user, SpecialStat.Strength, special) < ignore.MinimumStrength)
            return;

        walkMod = ignore.ApplyStrengthModifier(walkMod, sprint: false);
        sprintMod = ignore.ApplyStrengthModifier(sprintMod, sprint: true);
    }
}
