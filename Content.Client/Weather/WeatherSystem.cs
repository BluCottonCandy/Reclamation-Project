using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Weather;
using Robust.Client.Audio;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using AudioComponent = Robust.Shared.Audio.Components.AudioComponent;

namespace Content.Client.Weather;

public sealed class WeatherSystem : SharedWeatherSystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityUid? _hallucinationSource;
    private EntityUid? _hallucinationOwner;
    private TimeSpan _hallucinationEnd;
    private TimeSpan _hallucinationStart;

    /// <summary>Separate client-only source; never replace or mutate the real map's weather.</summary>
    public bool StartHallucination(EntityUid owner, WeatherPrototype prototype, TimeSpan duration)
    {
        StopHallucination();
        if (Transform(owner).MapUid is not { } map || duration <= TimeSpan.Zero) return false;
        _hallucinationSource = Spawn(null, new EntityCoordinates(map, Vector2.Zero));
        _hallucinationOwner = owner;
        _hallucinationStart = Timing.CurTime;
        _hallucinationEnd = Timing.CurTime + duration;
        StartWeather(_hallucinationSource.Value, EnsureComp<WeatherComponent>(_hallucinationSource.Value), prototype, _hallucinationEnd);
        return true;
    }

    public void StopHallucination()
    {
        if (_hallucinationSource is { } source && !Deleted(source))
        {
            if (TryComp<WeatherComponent>(source, out var weather))
                foreach (var id in weather.Weather.Keys.ToArray()) EndWeather(source, weather, id);
            Del(source);
        }
        _hallucinationSource = null;
        _hallucinationOwner = null;
    }

    public bool TryGetHallucinationWeather(MapId map, out WeatherPrototype prototype, out float alpha)
    {
        prototype = default!;
        alpha = 0f;
        if (_hallucinationSource is not { } source || Deleted(source) || Timing.CurTime >= _hallucinationEnd ||
            _playerManager.LocalEntity != _hallucinationOwner || Transform(source).MapID != map ||
            !TryComp<WeatherComponent>(source, out var component)) return false;
        foreach (var id in component.Weather.Keys)
        {
            if (!ProtoMan.TryIndex(id, out var found)) continue;
            prototype = found;
            alpha = HallucinationAlpha();
            return true;
        }
        return false;
    }

    private float HallucinationAlpha() => Math.Clamp((float)(Timing.CurTime - _hallucinationStart).TotalSeconds / 2f, 0f, 1f) *
        Math.Clamp((float)(_hallucinationEnd - Timing.CurTime).TotalSeconds / 2f, 0f, 1f);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_hallucinationSource is not { } source) return;
        if (Deleted(source) || _playerManager.LocalEntity != _hallucinationOwner ||
            _hallucinationOwner is not { } owner || Deleted(owner) ||
            Transform(owner).MapID != Transform(source).MapID || Timing.CurTime >= _hallucinationEnd)
            StopHallucination();
    }

    public override void Shutdown()
    {
        StopHallucination();
        base.Shutdown();
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WeatherComponent, ComponentHandleState>(OnWeatherHandleState);
    }

    protected override void Run(EntityUid uid, WeatherData weather, WeatherPrototype weatherProto, float frameTime)
    {
        base.Run(uid, weather, weatherProto, frameTime);

        var ent = _playerManager.LocalEntity;

        if (ent == null)
            return;

        var mapUid = Transform(uid).MapUid;
        var entXform = Transform(ent.Value);

        // Maybe have the viewports manage this?
        if (mapUid == null || entXform.MapUid != mapUid)
        {
            weather.Stream = _audio.Stop(weather.Stream);
            return;
        }

        if (!Timing.IsFirstTimePredicted || weatherProto.Sound == null)
            return;

        weather.Stream ??= _audio.PlayGlobal(weatherProto.Sound, Filter.Local(), true)?.Entity;
        if (!TryComp(weather.Stream, out AudioComponent? comp))
            return;

        // #Misfits Fix - Throttle audio occlusion BFS to 4 Hz instead of every tick.
        // The BFS flood-fill allocates new Queue+HashSet each run; per-tick was wasteful
        // since audio occlusion changes slowly as the player moves.
        const float AudioBFSInterval = 0.25f;
        weather.AudioBFSAccumulator += frameTime;
        var runBFS = weather.AudioBFSAccumulator >= AudioBFSInterval;

        var occlusion = comp.Occlusion; // keep last value if skipping BFS

        // Work out tiles nearby to determine volume.
        if (runBFS && TryComp<MapGridComponent>(entXform.GridUid, out var grid))
        {
            weather.AudioBFSAccumulator -= AudioBFSInterval;

            TryComp(entXform.GridUid, out RoofComponent? roofComp);
            var gridId = entXform.GridUid.Value;
            // FloodFill to the nearest tile and use that for audio.
            var seed = _mapSystem.GetTileRef(gridId, grid, entXform.Coordinates);
            var frontier = new Queue<TileRef>();
            frontier.Enqueue(seed);
            // If we don't have a nearest node don't play any sound.
            EntityCoordinates? nearestNode = null;
            var visited = new HashSet<Vector2i>();

            while (frontier.TryDequeue(out var node))
            {
                if (!visited.Add(node.GridIndices))
                    continue;

                if (!CanWeatherAffect(entXform.GridUid.Value, grid, node, roofComp))
                {
                    // Add neighbors
                    // TODO: Ideally we pick some deterministically random direction and use that
                    // We can't just do that naively here because it will flicker between nearby tiles.
                    for (var x = -1; x <= 1; x++)
                    {
                        for (var y = -1; y <= 1; y++)
                        {
                            if (Math.Abs(x) == 1 && Math.Abs(y) == 1 ||
                                x == 0 && y == 0 ||
                                (new Vector2(x, y) + node.GridIndices - seed.GridIndices).Length() > 3)
                            {
                                continue;
                            }

                            frontier.Enqueue(_mapSystem.GetTileRef(gridId, grid, new Vector2i(x, y) + node.GridIndices));
                        }
                    }

                    continue;
                }

                nearestNode = new EntityCoordinates(entXform.GridUid.Value,
                    node.GridIndices + grid.TileSizeHalfVector);
                break;
            }

            // Get occlusion to the targeted node if it exists, otherwise set a default occlusion.
            if (nearestNode != null)
            {
                var entPos = _transform.GetMapCoordinates(entXform);
                var nodePosition = _transform.ToMapCoordinates(nearestNode.Value).Position;
                var delta = nodePosition - entPos.Position;
                var distance = delta.Length();
                occlusion = _audio.GetOcclusion(entPos, delta, distance);
            }
            else
            {
                occlusion = 3f;
            }
        }

        var alpha = uid == _hallucinationSource ? HallucinationAlpha() : GetPercent(weather, uid);
        alpha *= SharedAudioSystem.VolumeToGain(weatherProto.Sound.Params.Volume);
        _audio.SetGain(weather.Stream, alpha, comp);
        comp.Occlusion = occlusion;
    }

    protected override bool SetState(EntityUid uid, WeatherState state, WeatherComponent comp, WeatherData weather, WeatherPrototype weatherProto)
    {
        if (!base.SetState(uid, state, comp, weather, weatherProto))
            return false;

        if (!Timing.IsFirstTimePredicted)
            return true;

        // TODO: Fades (properly)
        weather.Stream = _audio.Stop(weather.Stream);
        weather.Stream = _audio.PlayGlobal(weatherProto.Sound, Filter.Local(), true)?.Entity;
        return true;
    }

    private void OnWeatherHandleState(EntityUid uid, WeatherComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not WeatherComponentState state)
            return;

        foreach (var (proto, weather) in component.Weather)
        {
            // End existing one
            if (!state.Weather.TryGetValue(proto, out var stateData))
            {
                EndWeather(uid, component, proto);
                continue;
            }

            // Data update?
            weather.StartTime = stateData.StartTime;
            weather.EndTime = stateData.EndTime;
            weather.State = stateData.State;
        }

        foreach (var (proto, weather) in state.Weather)
        {
            if (component.Weather.ContainsKey(proto))
                continue;

            // New weather
            StartWeather(uid, component, ProtoMan.Index<WeatherPrototype>(proto), weather.EndTime);
        }
    }
}
