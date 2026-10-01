// #Misfits Change - Applies robot dead-frame visuals while the robot is downed but not dead.
using Content.Client.DamageState;
using Content.Shared._Misfits.Silicon;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client._Misfits.Silicon;

public sealed class RobotDownedVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RobotDownedVisualsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<StandingStateComponent, ComponentStartup>(OnStandingStartup);
        SubscribeLocalEvent<StandingStateComponent, AfterAutoHandleStateEvent>(OnStandingStateChanged);
        SubscribeLocalEvent<MobStateComponent, ComponentStartup>(OnMobStateStartup);
        SubscribeLocalEvent<MobStateComponent, AfterAutoHandleStateEvent>(OnMobStateChanged);
    }

    private void OnStartup(Entity<RobotDownedVisualsComponent> ent, ref ComponentStartup args)
    {
        UpdateVisual(ent.Owner, ent.Comp);
    }

    private void OnStandingStartup(Entity<StandingStateComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<RobotDownedVisualsComponent>(ent, out var visuals))
            return;

        UpdateVisual(ent.Owner, visuals, ent.Comp);
    }

    private void OnStandingStateChanged(Entity<StandingStateComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<RobotDownedVisualsComponent>(ent, out var visuals))
            return;

        UpdateVisual(ent.Owner, visuals, ent.Comp);
    }

    private void OnMobStateStartup(Entity<MobStateComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<RobotDownedVisualsComponent>(ent, out var visuals))
            return;

        UpdateVisual(ent.Owner, visuals, mobState: ent.Comp);
    }

    private void OnMobStateChanged(Entity<MobStateComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<RobotDownedVisualsComponent>(ent, out var visuals))
            return;

        UpdateVisual(ent.Owner, visuals, mobState: ent.Comp);
    }

    private void UpdateVisual(
        EntityUid uid,
        RobotDownedVisualsComponent visuals,
        StandingStateComponent? standing = null,
        MobStateComponent? mobState = null,
        SpriteComponent? sprite = null)
    {
        if (!Resolve(uid, ref sprite, false))
            return;

        if (!_sprite.LayerMapTryGet((uid, sprite), DamageStateVisualLayers.Base, out _, false))
            return;

        var isDead = Resolve(uid, ref mobState, false) && mobState.CurrentState == MobState.Dead;
        var isDowned = Resolve(uid, ref standing, false) && standing.CurrentState is StandingState.Lying or StandingState.GettingUp;

        _sprite.LayerSetRsiState((uid, sprite), DamageStateVisualLayers.Base, isDead || isDowned ? visuals.DownedState : visuals.StandingState);
    }
}