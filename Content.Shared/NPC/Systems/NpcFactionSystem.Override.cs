using Content.Shared._ReclamationProject.Intelligence;
using Content.Shared.Body.Components;
using Content.Shared.Mind.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Silicon.Components;
using Content.Shared.Tag;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    [Dependency] private readonly TagSystem _overrideTags = default!;

    // Keep disconnected player bodies protected too, rather than checking only the current controller.
    public bool IsOverrideProtectedPlayer(EntityUid uid) => HasComp<ActorComponent>(uid) ||
        TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind;

    public bool IsOverrideRobot(EntityUid uid) => HasComp<SiliconComponent>(uid) ||
        _overrideTags.HasTag(uid, "SiliconEmotes") ||
        TryComp<BodyComponent>(uid, out var body) && body.Prototype?.Id == "Bot";

    public void SetOverrideFaction(EntityUid uid)
    {
        EnsureComp<Content.Shared._ReclamationProject.Intelligence.OverriddenRobotComponent>(uid);
        ClearFactions(uid);
        AddFaction(uid, "Wastelander");
        Comp<NpcFactionMemberComponent>(uid).FriendlyOrderable = false;
    }

    private bool TryOverrideRelationship(EntityUid source, EntityUid target, out bool friendly)
    {
        friendly = false;
        var sourceOverride = HasComp<OverriddenRobotComponent>(source);
        var targetOverride = HasComp<OverriddenRobotComponent>(target);
        if (!sourceOverride && !targetOverride)
            return false;
        var other = sourceOverride ? target : source;
        if (sourceOverride && targetOverride || IsOverrideProtectedPlayer(other))
        {
            friendly = true;
            return true;
        }
        // Other AI robots are hostile, while ordinary NPCs use the existing Wastelander relationships.
        return IsOverrideRobot(other);
    }

    private IEnumerable<EntityUid> GetOverrideHostiles(Entity<NpcFactionMemberComponent?, FactionExceptionComponent?> ent, float range)
    {
        var factions = _factions.Keys.Select(id => (ProtoId<NpcFactionPrototype>) id).ToHashSet();
        return GetNearbyFactions(ent, range, factions)
            .Where(target => IsEntityHostile(ent, target) && !IsEntityFriendly(ent.Owner, target) &&
                !IsIgnored((ent.Owner, ent.Comp2), target));
    }
}
