using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.MeleeCharge;

// The movement lock must be restored with authoritative state during prediction rollback.
[RegisterComponent, NetworkedComponent]
public sealed partial class MeleeChargeComponent : Component;
