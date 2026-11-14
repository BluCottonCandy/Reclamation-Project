using Content.Shared._ReclamationProject.Operators;
using Content.Shared.DoAfter;
using Content.Server.Forensics;
using Content.Shared.Forensics;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Operators;

public sealed class OperatorTrackerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private readonly Dictionary<string, List<(TimeSpan Time, EntityCoordinates Position)>> _trails = new();
    private readonly Dictionary<EntityUid, HashSet<string>> _tracked = new();
    private readonly Dictionary<string, EntityCoordinates> _lastPosition = new();
    private TimeSpan _nextSample;

    public override void Initialize()
    {
        SubscribeLocalEvent<OperatorTrackerComponent, GetVerbsEvent<InnateVerb>>(OnVerbs);
        SubscribeLocalEvent<OperatorTrackerComponent, OperatorSampleDoAfter>(OnSample);
        SubscribeLocalEvent<OperatorTrackerComponent, ComponentShutdown>((uid, _, _) => _tracked.Remove(uid));
    }

    private void OnVerbs(EntityUid uid, OperatorTrackerComponent component, GetVerbsEvent<InnateVerb> args)
    {
        if (args.User != uid || !args.CanInteract || !args.CanAccess)
            return;

        if (args.Target == uid)
        {
            if (_tracked.ContainsKey(uid))
                args.Verbs.Add(new InnateVerb
                {
                    Text = Loc.GetString("operators-stop-trail"),
                    Act = () =>
                    {
                        _tracked.Remove(uid);
                        if (TryComp<ActorComponent>(uid, out var actor))
                            RaiseNetworkEvent(new OperatorTrailEvent([]), actor.PlayerSession.Channel);
                    },
                });
            return;
        }

        if (!TryComp<ForensicsComponent>(args.Target, out var evidence) || string.IsNullOrEmpty(evidence.Scent))
            return;

        var target = args.Target;
        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("operators-track-trail"),
            Act = () => _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, uid, 2f,
                new OperatorSampleDoAfter(), uid, target)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
            }),
        });
    }

    private void OnSample(EntityUid uid, OperatorTrackerComponent component, OperatorSampleDoAfter args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target ||
            !TryComp<ForensicsComponent>(target, out var evidence) || string.IsNullOrEmpty(evidence.Scent))
            return;

        args.Handled = true;
        if (!_tracked.TryGetValue(uid, out var scents))
            _tracked[uid] = scents = [];
        scents.Add(evidence.Scent);
        _popup.PopupEntity(Loc.GetString("operators-trail-started"), uid, uid);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextSample)
            return;

        _nextSample = now + TimeSpan.FromSeconds(0.5);
        var cutoff = now - TimeSpan.FromMinutes(1);
        foreach (var (scent, trail) in _trails.ToArray())
        {
            trail.RemoveAll(step => step.Time <= cutoff || TerminatingOrDeleted(step.Position.EntityId));
            if (trail.Count == 0)
                _trails.Remove(scent);
        }

        var actors = EntityQueryEnumerator<ScentComponent, ForensicsComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var evidence, out var transform))
        {
            if (string.IsNullOrEmpty(evidence.Scent) || transform.MapUid == null)
                continue;

            var coords = transform.Coordinates.WithEntityId(transform.GridUid ?? transform.MapUid.Value, EntityManager);
            if (_lastPosition.TryGetValue(evidence.Scent, out var previous) &&
                coords.InRange(EntityManager, _transform, previous, 0.75f))
                continue;

            _lastPosition[evidence.Scent] = coords;
            if (!_trails.TryGetValue(evidence.Scent, out var trail))
                _trails[evidence.Scent] = trail = [];

            trail.Add((now, coords));
        }

        foreach (var (uid, scents) in _tracked)
        {
            if (!TryComp<ActorComponent>(uid, out var actor))
                continue;

            var origin = Transform(uid).Coordinates;
            var steps = scents.Where(_trails.ContainsKey)
                .SelectMany(scent => _trails[scent])
                .Where(step => origin.InRange(EntityManager, _transform, step.Position, 16f))
                .Select(step => GetNetCoordinates(step.Position)).ToArray();
            RaiseNetworkEvent(new OperatorTrailEvent(steps), actor.PlayerSession.Channel);
        }
    }
}
