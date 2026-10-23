using Content.Server._ReclamationProject.Factions;
using Content.Server.Station.Components;

namespace Content.Server.Station.Systems;

public sealed partial class StationJobsSystem
{
    private void AddRaiderFactionJobs(EntityUid station, StationJobsComponent stationJobs)
    {
        if (!TryComp<RaiderFactionJobsComponent>(station, out var factions))
            return;
        foreach (var (job, count) in factions.Jobs)
            stationJobs.SetupAvailableJobs.TryAdd(job, new List<int?> { count, count });
    }
}
