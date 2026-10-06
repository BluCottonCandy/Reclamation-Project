// #Reclamation Project Add - Client half of crank-charged guns (Minutemen laser musket).
// The server pays for each crank; the new crank count arrives through the networked component.
using Content.Shared._ReclamationProject.Weapons.Ranged;

namespace Content.Client._ReclamationProject.Weapons.Ranged;

public sealed partial class CrankChargeSystem : SharedCrankChargeSystem
{
    protected override bool TryPayCrankCost(Entity<CrankChargeComponent> ent, EntityUid user)
    {
        return false;
    }
}
