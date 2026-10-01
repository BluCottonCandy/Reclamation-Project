// #Misfits Change /Add/ - PickRandomEnemyHeadComponent
// Sets the kill target to a head (or any non-same-faction human) from a faction that is
// hostile to the player's faction. This prevents faction members from being assigned to
// kill their own comrades.
using Content.Server.Objectives.Systems;

namespace Content.Server.Objectives.Components;

/// <summary>
/// Sets the target for <see cref="TargetObjectiveComponent"/> to a random head that is
/// NOT a member of the assigning player's own NPC faction(s).
/// Falls back to any non-same-faction living human if no heads are found.
/// Cancels assignment if no valid enemy targets exist at all.
/// </summary>
[RegisterComponent, Access(typeof(KillPersonConditionSystem))]
public sealed partial class PickRandomEnemyHeadComponent : Component
{
}
