using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._ReclamationProject.Factions;

/// <summary>Until a faction base is mapped, use the station's normal arrivals instead of an arbitrary fallback.</summary>
public sealed class RaiderArrivalSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly StationSpawningSystem _spawning = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawningEvent>(OnSpawning,
            before: new[] { typeof(ContainerSpawnPointSystem), typeof(SpawnPointSystem) });
    }

    private void OnSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null || args.Station is not { } station || args.Job?.Prototype is not { } job
            || !TryComp<RaiderFactionJobsComponent>(station, out var factions) || !factions.Jobs.ContainsKey(job.Id))
            return;
        var arrivals = new List<EntityCoordinates>();
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (points.MoveNext(out var uid, out var point, out var xform))
        {
            if (_stations.GetOwningStation(uid, xform) != station)
                continue;
            // A mapped faction spawn always wins, including for latejoins.
            if (point.SpawnType == SpawnPointType.Job && point.Job?.ID == job.Id)
                return;
            if (point.SpawnType == SpawnPointType.LateJoin)
                arrivals.Add(xform.Coordinates);
        }
        var containers = EntityQueryEnumerator<ContainerSpawnPointComponent, TransformComponent>();
        while (containers.MoveNext(out var uid, out var point, out var xform))
        {
            if (_stations.GetOwningStation(uid, xform) == station
                && point.SpawnType == SpawnPointType.Job && point.Job == job.Id)
                return;
        }
        if (arrivals.Count == 0)
            return;
        args.SpawnResult = _spawning.SpawnPlayerMob(_random.Pick(arrivals), args.Job,
            args.HumanoidCharacterProfile, station);
    }
}
