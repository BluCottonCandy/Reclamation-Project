// #Reclamation Project - medieval weapons: the shepherd's sling.
using Content.Shared.Damage;
using Content.Shared.Item;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.Medieval;

/// <summary>
/// A sling. Its pouch holds a handful of items up to <see cref="MaxSize"/>; one shot launches all of them.
/// Whatever it launches deals <see cref="BonusDamage"/> extra if it hits someone, and a grenade is primed as it flies.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlingComponent : Component
{
    [DataField]
    public ProtoId<ItemSizePrototype> MaxSize = "Normal";

    [DataField]
    public DamageSpecifier BonusDamage = new();

    /// <summary>Pouch container. Everything in it is launched in one shot.</summary>
    [DataField]
    public string Slot = "storagebase";

    /// <summary>Degrees either side of the aim line for the extra items in a multi-item shot.</summary>
    [DataField]
    public float Spread = 15f;

    /// <summary>
    /// Fustibalus (staff sling): bullets / shells in the pouch fire as real gunshots in the same volley,
    /// everything else is thrown. When false (shepherd's sling) bullets can't even go in the pouch.
    /// </summary>
    [DataField]
    public bool FireCartridges;

    /// <summary>Speed of bullets fired in a volley.</summary>
    [DataField]
    public float BulletSpeed = 30f;
}

/// <summary>Put on an item while it flies out of a sling; removed when it lands.</summary>
[RegisterComponent]
public sealed partial class SlingProjectileComponent : Component
{
    [DataField]
    public DamageSpecifier BonusDamage = new();

    [DataField]
    public EntityUid? Shooter;
}
