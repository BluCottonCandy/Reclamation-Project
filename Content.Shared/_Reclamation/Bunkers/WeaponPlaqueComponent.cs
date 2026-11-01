using Robust.Shared.GameStates;

namespace Content.Shared._Reclamation.Bunkers;

/// <summary>Uses normal item slots; the client draws the mounted item's own sprite above the plaque.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WeaponPlaqueComponent : Component;
