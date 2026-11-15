using System.Threading;
using Content.Server.Atmos.Components;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Events;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Server.Console;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._NF.NuclearMapFix
{
    public sealed class NuclearMapFix : EntitySystem
    {
        [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
        [Dependency] private readonly IServerConsoleHost _host = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<StationPostInitEvent>(OnStartup, after: new []{typeof(ShuttleSystem)});
            SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        }

        private void OnRoundStarted(RoundStartedEvent args)
        {
            if (_atmosphere.AtmosSimulated)
                return;

            var initialized = new HashSet<EntityUid>();
            var query = AllEntityQuery<GridAtmosphereComponent, TransformComponent>();
            while (query.MoveNext(out _, out var transform))
            {
                if (transform.MapUid is not { } map || !initialized.Add(map) || HasComp<MapAtmosphereComponent>(map))
                    continue;

                var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
                air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
                air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
                _atmosphere.SetMapAtmosphere(map, false, air);
            }
        }

        private void OnStartup(ref StationPostInitEvent args)
        {
            if (!_atmosphere.AtmosSimulated)
                return;

            Timer.Spawn(TimeSpan.FromSeconds(5), () =>
            {
                var query = AllEntityQuery<GridAtmosphereComponent, TransformComponent>();

                while (query.MoveNext(out var dummyatmos, out var comp))
                {
                    var gridUid = comp.GridUid;
                    _host.AppendCommand($"fixgridatmos {gridUid}");
                    Logger.Info($"executed command on {gridUid}");
                }
            });
        }
    }
}
