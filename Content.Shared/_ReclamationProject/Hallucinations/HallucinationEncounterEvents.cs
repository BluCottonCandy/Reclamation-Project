using Robust.Shared.Serialization;
namespace Content.Shared._ReclamationProject.Hallucinations;

[Serializable, NetSerializable]
public sealed class HallucinationEncounterStartedEvent(Guid encounter) : EntityEventArgs
{
    public Guid Encounter = encounter;
}
[Serializable, NetSerializable]
public sealed class HallucinationHitEvent(Guid encounter, bool ranged) : EntityEventArgs
{
    public Guid Encounter = encounter;
    public bool Ranged = ranged;
}
[Serializable, NetSerializable]
public sealed class HallucinationEncounterEndedEvent(Guid encounter) : EntityEventArgs
{
    public Guid Encounter = encounter;
}
