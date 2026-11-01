using Content.Shared._Misfits.PipBoy;
namespace Content.Shared._Reclamation.Bunkers;
[RegisterComponent]
public sealed partial class BunkerComputerComponent : Component;
public static class BunkerComputerSecurity
{
    public static bool IsLocked(IEntityManager entities,EntityUid uid) => entities.HasComponent<BunkerComputerComponent>(uid) &&
        entities.TryGetComponent<PipBoyNetworkComponent>(uid,out var network) && network.IsLocked;
}
