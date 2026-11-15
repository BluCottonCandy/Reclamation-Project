using Content.Server.NPC.HTN;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSystem
{
    public void SetBlackboard(EntityUid uid, string key, object value, HTNComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
        {
            return;
        }

        if (key == NPCBlackboard.FollowTarget && value is Robust.Shared.Map.EntityCoordinates coordinates &&
            !CanCommandAnimalFollower(coordinates.EntityId, uid))
            return;

        var blackboard = component.Blackboard;
        blackboard.SetValue(key, value);
    }
}
