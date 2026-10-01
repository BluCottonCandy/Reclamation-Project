// #Misfits Add - Campfire system ported from RMC-14. Provides lit/unlit campfires
// that consume fuel (wood, scrap) and emit light + ambient sound when burning.
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Campfire;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CampfireComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Lit;

    [DataField]
    public SoundSpecifier? LitSound = new SoundPathSpecifier("/Audio/Effects/sparks4.ogg");

    /// <summary>
    /// If true, the campfire requires fuel to be lit and stay lit.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool FuelRequired = true;

    [DataField, AutoNetworkedField]
    public int Fuel;

    [DataField]
    public int MaxFuel = 5;

    [DataField]
    public int FuelPerWood = 1;

    /// <summary>
    /// How long one unit of fuel burns.
    /// </summary>
    [DataField]
    public TimeSpan BurnDuration = TimeSpan.FromSeconds(60);

    [DataField, AutoNetworkedField]
    public TimeSpan? LitAt;

    [DataField]
    public TimeSpan ExtinguishDelay = TimeSpan.FromSeconds(2);
}

[Serializable, NetSerializable]
public enum CampfireVisuals : byte
{
    Lit
}

[Serializable, NetSerializable]
public sealed partial class CampfireExtinguishDoAfterEvent : SimpleDoAfterEvent
{
}
