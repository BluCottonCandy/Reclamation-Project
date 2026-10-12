using System.Linq;
using Content.Server.Antag;
using Content.Server.Antag.Components;
using Content.Server.GameTicking;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Roles;
using Content.Shared._Reclamation.ActivatedSynth;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Objectives.Components;
using Content.Shared.Radio.Components;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Reclamation.ActivatedSynth;

/// <summary>Uses the sleeper-agent selection and mind/objective infrastructure without an uplink.</summary>
public sealed class ActivatedSynthSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playtime = default!;
    [Dependency] private readonly AntagSelectionSystem _selection = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly RoleSystem _roles = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly Robust.Shared.Random.IRobustRandom _random = default!;

    private EntityUid? _rule;
    private TimeSpan _nextActivation;
    private static readonly string[] Objectives =
    {
        "SynthHolodisksObjective", "SynthRifleObjective", "SynthKeysObjective",
        "SynthRailroadObjective", "SynthDiamondCityObjective", "SynthMayorObjective",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevel);
        SubscribeLocalEvent<ActivatedSynthRuleComponent, AfterAntagEntitySelectedEvent>(OnActivated);
        SubscribeLocalEvent<SynthObjectiveComponent, ObjectiveGetProgressEvent>(OnProgress);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEnd);
        SubscribeNetworkEvent<RequestSynthReportEvent>(OnRequestReport);
        SubscribeNetworkEvent<SubmitSynthReportEvent>(OnSubmitReport);
    }

    private void OnRunLevel(GameRunLevelChangedEvent args)
    {
        if (args.New != GameRunLevel.InRound)
        {
            if (args.New == GameRunLevel.PreRoundLobby)
                _rule = null;
            return;
        }
        _rule = _ticker.AddGameRule("ActivatedSynths");
        _ticker.StartGameRule(_rule.Value);
        _nextActivation = _timing.CurTime + TimeSpan.FromHours(1);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextActivation ||
            !TryComp<AntagSelectionComponent>(_rule, out var selection))
            return;

        // One batch per hour. A delayed tick must not activate several batches at once.
        _nextActivation = _timing.CurTime + TimeSpan.FromHours(1);
        ActivateBatch();
    }

    /// <summary>Also used by the admin test command; eligibility and opt-in still apply.</summary>
    public int ActivateBatch()
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || !TryComp<AntagSelectionComponent>(_rule, out var selection))
            return 0;
        var before = selection.SelectedMinds.Count;
        var eligible = _players.Sessions.Where(session =>
        {
            if (session.AttachedEntity is not {} body || !_mobState.IsAlive(body))
                return false;
            if (!_playtime.TryGetTrackerTime(session, "Overall", out _))
                return false;
            _playtime.FlushTracker(session);
            return _playtime.TryGetTrackerTime(session, "Overall", out var time) && time >= TimeSpan.FromHours(10);
        }).ToList();
        _selection.ChooseAntags((_rule!.Value, selection), eligible);
        return selection.SelectedMinds.Count - before;
    }

    private void OnActivated(Entity<ActivatedSynthRuleComponent> rule, ref AfterAntagEntitySelectedEvent args)
    {
        if (!_minds.TryGetMind(args.EntityUid, out var mindId, out var mind))
            return;
        EnsureComp<SynthReportComponent>(mindId);
        EnsureComp<ActivatedSynthComponent>(mindId);
        _roles.MindAddRole(mindId, new RoleBriefingComponent
        {
            Briefing = Loc.GetString("activated-synth-briefing"),
        }, mind, true);

        var choices = Objectives.ToList();
        for (var i = 0; i < 2; i++)
        {
            var index = _random.Next(choices.Count);
            var objective = choices[index];
            choices.RemoveAt(index);
            if (!_minds.TryAddObjective(mindId, mind, objective))
                Log.Error($"Could not assign Activated Synth objective {objective}.");
        }
        if (args.Session != null)
            RaiseNetworkEvent(new SynthActivatedEvent(), args.Session);
    }

    private void OnProgress(Entity<SynthObjectiveComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (ent.Comp.Task >= SynthTask.Railroad)
        {
            args.Progress = ent.Comp.Completed ? 1 : 0;
            return;
        }

        var items = InventoryItems(args.Mind.OwnedEntity).ToList();
        args.Progress = ent.Comp.Task switch
        {
            SynthTask.Holodisks => Math.Min(1f, items.Count(item =>
                MetaData(item).EntityPrototype?.ID.StartsWith("N14Holotape", StringComparison.Ordinal) == true) / 4f),
            SynthTask.Rifle => items.Any(item => MetaData(item).EntityPrototype?.ID == "N14WeaponLaserRifle") ? 1 : 0,
            SynthTask.Keys => KeyProgress(items),
            _ => 0,
        };
    }

    /// <summary>Only the character's containers count, including nested bags and inserted headset keys.</summary>
    private IEnumerable<EntityUid> InventoryItems(EntityUid? body)
    {
        if (body is not {} owner)
            yield break;
        var pending = new Stack<EntityUid>();
        var visited = new HashSet<EntityUid> { owner };
        pending.Push(owner);
        while (pending.TryPop(out var entity))
        {
            if (!TryComp<ContainerManagerComponent>(entity, out var manager))
                continue;
            foreach (var container in manager.Containers.Values)
            foreach (var item in container.ContainedEntities)
            {
                if (!visited.Add(item))
                    continue;
                yield return item;
                pending.Push(item);
            }
        }
    }

    private float KeyProgress(IEnumerable<EntityUid> items)
    {
        var channels = new HashSet<string>();
        foreach (var item in items)
        {
            if (TryComp<EncryptionKeyComponent>(item, out var key))
                channels.UnionWith(key.Channels);
        }
        return new[] { "Minutemen", "BrotherhoodOfSteel", "Townsfolk", "Railroad" }
            .Count(channels.Contains) / 4f;
    }

    private void OnRequestReport(RequestSynthReportEvent msg, EntitySessionEventArgs args)
    {
        if (!_minds.TryGetMind(args.SenderSession, out var mindId, out var mind) ||
            !TryComp<SynthReportComponent>(mindId, out var report))
            return;
        var state = new SynthReportStateEvent { Text = report.Text, CanEdit = _ticker.RunLevel == GameRunLevel.InRound };
        foreach (var objective in mind.AllObjectives)
        {
            if (TryComp<SynthObjectiveComponent>(objective, out var task) && task.Task >= SynthTask.Railroad &&
                MetaData(objective).EntityPrototype is {} proto)
                state.RoleplayObjectives[proto.ID] = task.Completed;
        }
        RaiseNetworkEvent(state, args.SenderSession);
    }

    private void OnSubmitReport(SubmitSynthReportEvent msg, EntitySessionEventArgs args)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || msg.Text == null || msg.CompletedObjectives == null || msg.Text.Length > 2000 ||
            !_minds.TryGetMind(args.SenderSession, out var mindId, out var mind) ||
            !TryComp<SynthReportComponent>(mindId, out var report))
            return;
        report.Text = msg.Text.Trim();
        // Only the sender's assigned roleplay objectives are editable; item progress is authoritative.
        foreach (var objective in mind.AllObjectives)
        {
            if (TryComp<SynthObjectiveComponent>(objective, out var task) && task.Task >= SynthTask.Railroad &&
                MetaData(objective).EntityPrototype is {} proto)
                task.Completed = msg.CompletedObjectives.Contains(proto.ID);
        }
        OnRequestReport(new RequestSynthReportEvent(), args);
    }

    private void OnRoundEnd(RoundEndTextAppendEvent args)
    {
        if (!TryComp<AntagSelectionComponent>(_rule, out var selection))
            return;
        foreach (var (mind, name) in selection.SelectedMinds)
        {
            if (TryComp<SynthReportComponent>(mind, out var report) && !string.IsNullOrWhiteSpace(report.Text))
                args.AddLine(Loc.GetString("activated-synth-round-report",
                    ("name", FormattedMessage.EscapeText(name)), ("report", FormattedMessage.EscapeText(report.Text))));
        }
    }
}
