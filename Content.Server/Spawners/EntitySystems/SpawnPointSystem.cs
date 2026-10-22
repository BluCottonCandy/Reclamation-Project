using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.Spawners.EntitySystems;

public sealed class SpawnPointSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawning);
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null)
            return;

        // TODO: Cache all this if it ends up important.
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        var possiblePositions = new List<EntityCoordinates>();

        while ( points.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (args.Station != null && _stationSystem.GetOwningStation(uid, xform) != args.Station)
                continue;

            // Delta-V: Allow setting a desired SpawnPointType
            if (args.DesiredSpawnPointType != SpawnPointType.Unset)
            {
                var isMatchingJob = spawnPoint.SpawnType == SpawnPointType.Job &&
                    (args.Job == null || spawnPoint.Job?.ID == args.Job.Prototype);

                switch (args.DesiredSpawnPointType)
                {
                    case SpawnPointType.Job when isMatchingJob:
                    case SpawnPointType.LateJoin when spawnPoint.SpawnType == SpawnPointType.LateJoin:
                    case SpawnPointType.Observer when spawnPoint.SpawnType == SpawnPointType.Observer:
                        possiblePositions.Add(xform.Coordinates);
                        break;
                    default:
                        continue;
                }
            }

            if (_gameTicker.RunLevel == GameRunLevel.InRound && spawnPoint.SpawnType == SpawnPointType.LateJoin)
            {
                possiblePositions.Add(xform.Coordinates);
            }

            if (_gameTicker.RunLevel != GameRunLevel.InRound &&
                spawnPoint.SpawnType == SpawnPointType.Job &&
                (args.Job == null || spawnPoint.Job?.ID == args.Job.Prototype))
            {
                possiblePositions.Add(xform.Coordinates);
            }
        }

        if (possiblePositions.Count == 0)
        {
            // A late join may have only job markers available. Prefer the requested
            // job, then other non-observer markers on the intended station. Never
            // fall back to a marker on an unrelated expedition or deleted map.
            var fallbackPositions = new List<EntityCoordinates>();
            var points2 = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            while (points2.MoveNext(out var uid, out var point, out var xform))
            {
                if (point.SpawnType == SpawnPointType.Observer || xform.MapID == MapId.Nullspace ||
                    (args.Station != null && _stationSystem.GetOwningStation(uid, xform) != args.Station))
                    continue;

                if (point.SpawnType == SpawnPointType.Job && point.Job?.ID == args.Job?.Prototype)
                    possiblePositions.Add(xform.Coordinates);
                else
                    fallbackPositions.Add(xform.Coordinates);
            }

            if (possiblePositions.Count == 0)
                possiblePositions.AddRange(fallbackPositions);
            if (possiblePositions.Count == 0)
            {
                Log.Error($"No spawn points were available on the requested station {args.Station}!");
                return;
            }
        }

        var spawnLoc = _random.Pick(possiblePositions);

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            spawnLoc,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station);
    }
}
