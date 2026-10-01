// #Misfits Add - When a mind is attached to an entity that has a
// GhostRoleJobComponent, this system adds a JobComponent to the mind
// so that playtime is tracked under the specified job's playTimeTracker.

using Content.Shared._Misfits.Pets;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;

namespace Content.Server._Misfits.Pets;

public sealed class GhostRoleJobSystem : EntitySystem
{
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GhostRoleJobComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnMindAdded(EntityUid uid, GhostRoleJobComponent component, MindAddedMessage args)
    {
        // The mind was just attached to this entity. Add a JobComponent to the mind
        // so that playtime tracking picks it up via MindGetAllRolesEvent.
        if (!_minds.TryGetMind(uid, out var mindId, out _))
            return;

        // Don't add a duplicate job if one already exists on the mind
        if (HasComp<JobComponent>(mindId))
            return;

        _roles.MindAddRole(mindId, new JobComponent { Prototype = component.Job });
    }
}
