// #Misfits Add - Allows ghost-role entities (e.g. pet Eyebots) to attribute
// playtime to a specific job tracker when a player takes the ghost role.
// Without this, ghost roles only get a GhostRoleMarkerRoleComponent which
// has no PlayTimeTrackerId, so no faction playtime is recorded.

using Robust.Shared.Prototypes;
using Content.Shared.Roles;

namespace Content.Shared._Misfits.Pets;

/// <summary>
/// Added to an entity that has a <see cref="GhostRoleComponent"/>.
/// When a mind is attached to this entity, a <see cref="JobComponent"/>
/// with the specified prototype is added to the mind so that playtime
/// is tracked under the corresponding <c>playTimeTracker</c>.
/// </summary>
[RegisterComponent]
public sealed partial class GhostRoleJobComponent : Component
{
    /// <summary>
    /// The job prototype ID to add to the mind when a player takes this ghost role.
    /// The job should have <c>setPreference: false</c> so it doesn't appear in
    /// the character setup screen.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<JobPrototype> Job;
}
