using Content.Shared._Shitmed.Targeting;

namespace Content.Shared.Projectiles;

/// <summary>
/// Raised directed on an entity when it embeds in another entity.
/// </summary>
[ByRefEvent]
public readonly record struct EmbedEvent(EntityUid? Shooter, EntityUid Embedded, TargetBodyPart? BodyPart)
{
    public readonly EntityUid? Shooter = Shooter;

    /// <summary>
    /// Entity that is embedded in.
    /// </summary>
    public readonly EntityUid Embedded = Embedded;

    /// <summary>
    ///   Physical part holding the object. A groin hit lands in the torso.
    /// </summary>
    public readonly TargetBodyPart? BodyPart = BodyPart == TargetBodyPart.Groin ? TargetBodyPart.Torso : BodyPart;
}

/// <summary>
/// Raised on an entity when it stops embedding in another entity.
/// </summary>
[ByRefEvent]
public readonly record struct RemoveEmbedEvent(EntityUid? Remover)
{
    public readonly EntityUid? Remover = Remover;
}
