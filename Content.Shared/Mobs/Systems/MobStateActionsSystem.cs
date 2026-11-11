using Content.Shared.Actions;
using Content.Shared._ReclamationProject.Hallucinations;
using Content.Shared.Mobs.Components;

namespace Content.Shared.Mobs.Systems;

/// <summary>
///     Adds and removes defined actions when a mob's <see cref="MobState"/> changes.
/// </summary>
public sealed class MobStateActionsSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MobStateActionsComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<MobStateComponent, ComponentInit>(OnMobStateComponentInit);
    }

    private void OnMobStateChanged(EntityUid uid, MobStateActionsComponent component, MobStateChangedEvent args)
    {
        ComposeActions(uid, component, args.NewMobState);
    }

    private void OnMobStateComponentInit(EntityUid uid, MobStateComponent component, ComponentInit args)
    {
        if (!TryComp<MobStateActionsComponent>(uid, out var mobStateActionsComp))
            return;

        ComposeActions(uid, mobStateActionsComp, component.CurrentState);
    }

    /// <summary>
    /// Adds or removes actions from a mob based on mobstate.
    /// </summary>
    /// <summary>Recompose after a temporary crit ends without a mob-state transition.</summary>
    public void RefreshActions(EntityUid uid)
    {
        if (TryComp<MobStateActionsComponent>(uid, out var actions) && TryComp<MobStateComponent>(uid, out var state))
            ComposeActions(uid, actions, state.CurrentState);
    }

    private void ComposeActions(EntityUid uid, MobStateActionsComponent component, MobState newMobState)
    {
        if (!TryComp<ActionsComponent>(uid, out var action))
            return;

        foreach (var act in component.GrantedActions)
        {
            Del(act);
        }
        component.GrantedActions.Clear();

        // Hallucination crit must not grant lethal/last-words actions, even for one frame.
        if (newMobState == MobState.Critical && TryComp<HallucinationPainComponent>(uid, out var pain) && pain.Collapsing)
            return;

        if (!component.Actions.TryGetValue(newMobState, out var toGrant))
            return;

        foreach (var id in toGrant)
        {
            EntityUid? act = null;
            if (_actions.AddAction(uid, ref act, id, uid, action))
                component.GrantedActions.Add(act.Value);
        }
    }
}
