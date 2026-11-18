// #Reclamation Project - medieval weapons: a crossbow must be spanned (cranked) before an arrow can go in.
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Medieval;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CrossbowComponent : Component
{
    /// <summary>Seconds of cranking (use in hand) to span the string.</summary>
    [DataField]
    public float SpanTime = 2.5f;

    /// <summary>Whether the string is drawn back. Firing releases it.</summary>
    [DataField, AutoNetworkedField]
    public bool Spanned;

    /// <summary>Added to the arrow's own damage when it hits: a crossbow hits harder than a bow.</summary>
    [DataField]
    public DamageSpecifier BonusDamage = new();

    /// <summary>Played when cranking starts (the normal bow draw sound).</summary>
    [DataField]
    public SoundSpecifier? SpanSound = new SoundPathSpecifier("/Audio/Items/bow_pull.ogg");

    /// <summary>Played when the string locks back (the normal arrow nock sound).</summary>
    [DataField]
    public SoundSpecifier? SpannedSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Misc/arrow_nock.ogg");
}

/// <summary>Put on an arrow fired from a crossbow; adds the crossbow's bonus when it hits.</summary>
[RegisterComponent]
public sealed partial class CrossbowBoltComponent : Component
{
    [DataField]
    public DamageSpecifier BonusDamage = new();
}

[Serializable, NetSerializable]
public sealed partial class CrossbowSpanDoAfterEvent : SimpleDoAfterEvent;
