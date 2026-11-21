using Content.Shared.Vehicles;
using Content.Shared._NC.Mountable.Components;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Traits.Assorted.Components;
using Robust.Shared.Timing;

namespace Content.Shared.Movement.Systems
{
    public sealed class MovementSpeedModifierSystem : EntitySystem
    {
        [Dependency] private readonly IGameTiming _timing = default!;

        public void RefreshMovementSpeedModifiers(EntityUid uid, MovementSpeedModifierComponent? move = null)
        {
            if (!Resolve(uid, ref move, false))
                return;

            if (_timing.ApplyingState)
                return;

            var isImmune = false;
            if (HasComp<SpeedModifierImmunityComponent>(uid))
                isImmune = true;

            var ev = new RefreshMovementSpeedModifiersEvent(isImmune);
            RaiseLocalEvent(uid, ev);

            // #Misfits Add - Global 135% speed cap, excludes bikes (Motorbike) and brahmin (Mountable)
            if (!HasComp<MotorbikeComponent>(uid) && !HasComp<MountableComponent>(uid))
            {
                const float maxMod = 1.35f;
                ev.CapSpeed(maxMod);
            }

            if (MathHelper.CloseTo(ev.WalkSpeedModifier, move.WalkSpeedModifier) &&
                MathHelper.CloseTo(ev.SprintSpeedModifier, move.SprintSpeedModifier))
                return;

            move.WalkSpeedModifier = ev.WalkSpeedModifier;
            move.SprintSpeedModifier = ev.SprintSpeedModifier;
            Dirty(uid, move);
        }

        public void ChangeBaseSpeed(EntityUid uid, float baseWalkSpeed, float baseSprintSpeed, float acceleration, MovementSpeedModifierComponent? move = null)
        {
            if (!Resolve(uid, ref move, false))
                return;

            move.BaseWalkSpeed = baseWalkSpeed;
            move.BaseSprintSpeed = baseSprintSpeed;
            move.Acceleration = acceleration;
            Dirty(uid, move);
        }

        // We might want to create separate RefreshMovementFrictionModifiersEvent and RefreshMovementFrictionModifiers function that will call it
        public void ChangeFriction(EntityUid uid, float friction, float? frictionNoInput, float acceleration, MovementSpeedModifierComponent? move = null)
        {
            if (!Resolve(uid, ref move, false))
                return;

            move.Friction = friction;
            move.FrictionNoInput = frictionNoInput;
            move.Acceleration = acceleration;
            Dirty(uid, move);
        }
    }

    /// <summary>
    ///     Raised on an entity to determine its new movement speed. Any system that wishes to change movement speed
    ///     should hook into this event and set it then. If you want this event to be raised,
    ///     call <see cref="MovementSpeedModifierSystem.RefreshMovementSpeedModifiers"/>.
    /// </summary>
    public sealed class RefreshMovementSpeedModifiersEvent : EntityEventArgs, IInventoryRelayEvent
    {
        public SlotFlags TargetSlots { get; } = ~SlotFlags.POCKET;

        private float _walkSpeedModifier = 1f;
        private float _sprintSpeedModifier = 1f;
        private float _walkBonuses = 1f;
        private float _sprintBonuses = 1f;
        private float _maximumSpeed = float.PositiveInfinity;

        // Keep bonuses separately so temporary slowdown immunity is independent of
        // subscriber order and preserves buffs, including when a slowdown is zero.
        public bool IgnoreSlowdowns;
        // Equipment that sets the wearer's base movement participates in the normal speed cap.
        public float BaseSpeedMultiplier = 1f;
        // Applied after ordinary bonuses, slowdown immunity and the global speed cap.
        public float FinalMultiplier = 1f;
        public float WalkSpeedModifier => MathF.Min((IgnoreSlowdowns ? _walkBonuses : _walkSpeedModifier) * BaseSpeedMultiplier, _maximumSpeed) * FinalMultiplier;
        public float SprintSpeedModifier => MathF.Min((IgnoreSlowdowns ? _sprintBonuses : _sprintSpeedModifier) * BaseSpeedMultiplier, _maximumSpeed) * FinalMultiplier;

        /// <summary>
        ///    Whether this entity is immune to most movement speed modifiers.
        ///    Bypassable by setting bypassImmunity to true.
        /// </summary
        private bool IsImmune = false;

        public void ModifySpeed(float walk, float sprint, bool bypassImmunity = false)
        {
            if (IsImmune && !bypassImmunity)
                return;

            _walkSpeedModifier *= walk;
            _sprintSpeedModifier *= sprint;
            _walkBonuses *= MathF.Max(1f, walk);
            _sprintBonuses *= MathF.Max(1f, sprint);
        }

        // Resolve ceilings after all equipment and perks have contributed, regardless of event order.
        public void CapSpeed(float maximum)
        {
            _maximumSpeed = MathF.Min(_maximumSpeed, maximum);
        }

        public RefreshMovementSpeedModifiersEvent(bool isImmune = false)
        {
            IsImmune = isImmune;
        }

        public void ModifySpeed(float mod)
        {
            ModifySpeed(mod, mod);
        }
    }
}
