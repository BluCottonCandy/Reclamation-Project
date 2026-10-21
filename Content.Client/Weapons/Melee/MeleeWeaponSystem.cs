using System.Linq;
using Content.Shared._ReclamationProject.GunAttachments;
using Content.Client._Misfits.Movement;
using Content.Client.Gameplay;
using Content.Shared.CCVar;
using Content.Shared.CombatMode;
using Content.Shared.Effects;
using Content.Shared.Damage;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.StatusEffect;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client.Weapons.Melee;

public sealed partial class MeleeWeaponSystem : SharedMeleeWeaponSystem
{
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly GunAttachmentsSystem _gunAttachments = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;
    [Dependency] private readonly InputSystem _inputSystem = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _color = default!;
    [Dependency] private readonly MisfitsLagCompensationSystem _lagComp = default!;

    private EntityQuery<TransformComponent> _xformQuery;

    private const string MeleeLungeKey = "melee-lunge";

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
        SubscribeNetworkEvent<MeleeLungeEvent>(OnMeleeLunge);
        UpdatesOutsidePrediction = true;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        UpdateEffects();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Timing.IsFirstTimePredicted)
            return;

        var entityNull = _player.LocalEntity;

        if (entityNull == null)
            return;

        var entity = entityNull.Value;

        if (!TryGetWeapon(entity, out var weaponUid, out var weapon))
            return;

        if (!CombatMode.IsInCombatMode(entity) || !Blocker.CanAttack(entity))
        {
            weapon.Attacking = false;
            return;
        }

        var useDown = _inputSystem.CmdStates.GetState(EngineKeyFunctions.Use) == BoundKeyState.Down;
        var altDown = _inputSystem.CmdStates.GetState(EngineKeyFunctions.UseSecondary) == BoundKeyState.Down;

        // Disregard inputs to the shoot binding
        if (TryComp<GunComponent>(weaponUid, out var gun) &&
            // Except if can't shoot due to being unwielded
            (!HasComp<GunRequiresWieldComponent>(weaponUid) ||
            (TryComp<WieldableComponent>(weaponUid, out var wieldable) && wieldable.Wielded)))
        {
            if (gun.UseKey)
                useDown = false;
            else
                altDown = false;
        }

        // Gun fire has already consumed its binding above. The remaining melee
        // input is a direct bayonet thrust, independent of the gun's old bash keys.
        var bayonetStab = TryComp<GunAttachmentsComponent>(weaponUid, out var attachments) &&
                          _gunAttachments.HasBayonet(attachments);

        if (weapon.AutoAttack || !useDown && !altDown)
        {
            if (weapon.Attacking)
            {
                RaisePredictiveEvent(new StopAttackEvent(GetNetEntity(weaponUid)));
            }
        }

        if (weapon.Attacking || weapon.NextAttack > Timing.CurTime || (!useDown && !altDown))
        {
            return;
        }

        // TODO using targeted actions while combat mode is enabled should NOT trigger attacks.

        var mousePos = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);

        if (mousePos.MapId == MapId.Nullspace)
        {
            return;
        }

        EntityCoordinates coordinates;

        if (MapManager.TryFindGridAt(mousePos, out var gridUid, out _))
        {
            coordinates = EntityCoordinates.FromMap(gridUid, mousePos, TransformSystem, EntityManager);
        }
        else
        {
            coordinates = EntityCoordinates.FromMap(MapManager.GetMap(mousePos.MapId), mousePos, TransformSystem, EntityManager);
        }

        if (bayonetStab && (useDown || altDown))
        {
            // Use the spear's existing directional arc attack, without a sprite click.
            var hovered = _stateManager.CurrentState is GameplayStateBase screen
                ? screen.GetClickedEntity(mousePos)
                : null;
            ClientHeavyAttack(entity, GetBayonetAimCoordinates(coordinates, hovered), weaponUid, weapon);
            return;
        }

        // Heavy attack.
        if (!bayonetStab && !weapon.DisableHeavy &&
            (!weapon.SwapKeys ? altDown : useDown))
        {
            // If it's an unarmed attack then do a disarm
            if (weapon.AltDisarm && weaponUid == entity)
            {
                EntityUid? target = null;

                if (_stateManager.CurrentState is GameplayStateBase screen)
                {
                    target = screen.GetClickedEntity(mousePos);
                }

                EntityManager.RaisePredictiveEvent(new DisarmAttackEvent(GetNetEntity(target), GetNetCoordinates(coordinates))
                {
                    LastRealTick = _lagComp.GetLastRealTick(),
                });
                return;
            }

            ClientHeavyAttack(entity, coordinates, weaponUid, weapon);
            return;
        }

        // Light attack
        if (!weapon.DisableClick &&
            (bayonetStab ? useDown || altDown : !weapon.SwapKeys ? useDown : altDown))
        {
            var attackerPos = TransformSystem.GetMapCoordinates(entity);

            if (mousePos.MapId != attackerPos.MapId ||
                (attackerPos.Position - mousePos.Position).Length() > weapon.Range)
            {
                if (!bayonetStab && weapon.HeavyOnLightMiss)
                    ClientHeavyAttack(entity, coordinates, weaponUid, weapon);

                return;
            }

            EntityUid? target = null;

            if (_stateManager.CurrentState is GameplayStateBase screen)
            {
                target = screen.GetClickedEntity(mousePos);
            }

            // Don't light-attack if interaction will be handling this instead
            if (Interaction.CombatModeCanHandInteract(entity, target))
                return;

            if (!bayonetStab && weapon.HeavyOnLightMiss && !CanDoLightAttack(entity, target, weapon, out _))
            {
                ClientHeavyAttack(entity, coordinates, weaponUid, weapon);
                return;
            }

            RaisePredictiveEvent(new LightAttackEvent(GetNetEntity(target), GetNetEntity(weaponUid), GetNetCoordinates(coordinates))
            {
                LastRealTick = _lagComp.GetLastRealTick(),
            });
        }
    }

    private EntityCoordinates GetBayonetAimCoordinates(EntityCoordinates cursor, EntityUid? hovered)
    {
        // Humanoid art extends above its physics body. Aim a hovered stab at the
        // body's coordinates, so clicking a head does not thrust over its hitbox.
        // Empty-space stabs still use the cursor direction and the normal spear arc.
        return hovered is { } target && HasComp<DamageableComponent>(target) &&
               _xformQuery.TryGetComponent(target, out var transform)
            ? transform.Coordinates
            : cursor;
    }

    protected override bool InRange(EntityUid user, EntityUid target, float range, ICommonSession? session, GameTick? lastRealTick = null)
    {
        var xform = Transform(target);
        var targetCoordinates = xform.Coordinates;
        var targetLocalAngle = xform.LocalRotation;

        return Interaction.InRangeUnobstructed(user, target, targetCoordinates, targetLocalAngle, range, useCenterRange: HasComp<ActorComponent>(user));
    }

    protected override void DoDamageEffect(List<EntityUid> targets, EntityUid? user, TransformComponent targetXform)
    {
        // Server never sends the event to us for predictiveeevent.
        _color.RaiseEffect(Color.Red, targets, Filter.Local());
    }

    protected override bool DoDisarm(EntityUid user, DisarmAttackEvent ev, EntityUid meleeUid, MeleeWeaponComponent component, ICommonSession? session)
    {
        if (!base.DoDisarm(user, ev, meleeUid, component, session))
            return false;

        if (!TryComp<CombatModeComponent>(user, out var combatMode) ||
            combatMode.CanDisarm != true)
        {
            return false;
        }

        var target = GetEntity(ev.Target);

        // They need to either have hands...
        if (!HasComp<HandsComponent>(target!.Value))
        {
            // or just be able to be shoved over.
            if (TryComp<StatusEffectsComponent>(target, out var status) && status.AllowedEffects.Contains("KnockedDown"))
                return true;

            if (Timing.IsFirstTimePredicted && HasComp<MobStateComponent>(target.Value))
                PopupSystem.PopupEntity(Loc.GetString("disarm-action-disarmable", ("targetName", target.Value)), target.Value);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Raises a heavy attack event with the relevant attacked entities.
    /// This is to avoid lag effecting the client's perspective too much.
    /// </summary>
    private void ClientHeavyAttack(EntityUid user, EntityCoordinates coordinates, EntityUid meleeUid, MeleeWeaponComponent component)
    {
        // Only run on first prediction to avoid the potential raycast entities changing.
        if (!_xformQuery.TryGetComponent(user, out var userXform) ||
            !Timing.IsFirstTimePredicted)
        {
            return;
        }

        var targetMap = coordinates.ToMap(EntityManager, TransformSystem);

        if (targetMap.MapId != userXform.MapID)
            return;

        var userPos = TransformSystem.GetWorldPosition(userXform);
        var direction = targetMap.Position - userPos;
        // [Changed by MisfitsCrew/Operator] Wide swings use cursor angle with full heavy reach;
        // clamping to cursor distance made hits require hovering the target.
        var distance = GetMeleeReach(user, component) * component.HeavyRangeModifier;

        // This should really be improved. GetEntitiesInArc uses pos instead of bounding boxes.
        // Server will validate it with InRangeUnobstructed.
        var entities = GetNetEntityList(ArcRayCast(userPos, direction.ToWorldAngle(), component.Angle, distance, userXform.MapID, user).ToList());
        RaisePredictiveEvent(new HeavyAttackEvent(GetNetEntity(meleeUid), entities.GetRange(0, Math.Min(component.MaxTargets, entities.Count)), GetNetCoordinates(coordinates))
        {
            LastRealTick = _lagComp.GetLastRealTick(),
        });
    }

    private void OnMeleeLunge(MeleeLungeEvent ev)
    {
        var ent = GetEntity(ev.Entity);
        var entWeapon = GetEntity(ev.Weapon);

        // Entity might not have been sent by PVS.
        if (Exists(ent) && Exists(entWeapon))
            DoLunge(ent, entWeapon, ev.Angle, ev.LocalPos, ev.Animation);
    }
}
