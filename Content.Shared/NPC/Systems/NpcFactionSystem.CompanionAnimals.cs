using Content.Shared._ReclamationProject.Pack;
using Content.Shared.NPC.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    // Cache prototype ancestry, not entity relationships: factions can change during play.
    private readonly Dictionary<string, (bool Warhound, bool Passive)> _companionAnimalKinds = new();

    private (bool Warhound, bool Passive) GetCompanionAnimalKind(EntityUid uid)
    {
        if (MetaData(uid).EntityPrototype is not { } prototype)
            return default;
        if (_companionAnimalKinds.TryGetValue(prototype.ID, out var kind))
            return kind;

        var simpleAnimal = false;
        var hostileAnimal = false;
        var petAnimal = false;
        foreach (var (id, _) in _proto.EnumerateAllParents<EntityPrototype>(prototype.ID, includeSelf: true))
        {
            kind.Warhound |= id == "N14MobPetDogLegion";
            kind.Passive |= id is "N14MobBrahdo" or "PackBrahdo";
            simpleAnimal |= id == "N14MobBaseSimple";
            petAnimal |= id == "N14MobPetBasic";
            hostileAnimal |= id == "N14MobBaseHostile";
        }
        kind.Passive |= simpleAnimal && !hostileAnimal || petAnimal && !kind.Warhound;
        _companionAnimalKinds[prototype.ID] = kind;
        return kind;
    }

    /// <summary>
    /// Companions do not hunt livestock, pets or passive wildlife. Predators retain their
    /// existing relationships; sharing WastelandAnimal with a rabbit is not enough to qualify.
    /// Applies before retaliation exceptions as well as ordinary faction targeting.
    /// </summary>
    private bool IsCompanionProtectedAnimal(EntityUid source, EntityUid target)
    {
        if (!HasComp<PackAnimalComponent>(source) && !GetCompanionAnimalKind(source).Warhound)
            return false;
        // A player or humanoid NPC does not become protected livestock by changing factions.
        if (HasComp<Content.Shared.Humanoid.HumanoidAppearanceComponent>(target) || IsOverrideRobot(target))
            return false;
        if (GetCompanionAnimalKind(target).Passive)
            return true;
        if (!TryComp<NpcFactionMemberComponent>(target, out var member) || member.Factions.Count == 0)
            return false;

        // Mixed hostile factions must not acquire immunity just by also belonging to a pet faction.
        foreach (var faction in member.Factions)
        {
            if (faction.Id is not ("Passive" or "PetsNT" or "AnimalFriend" or "Birb" or "Cat" or "Mouse" or "Pibble"))
                return false;
        }
        return true;
    }
}
