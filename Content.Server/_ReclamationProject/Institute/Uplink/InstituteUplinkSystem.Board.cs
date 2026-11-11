using System.Linq;
using Content.Shared._ReclamationProject.Institute.Uplink;
using Content.Shared.Mind;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Institute.Uplink;

// #Reclamation Project - Institute task board: a random preset list each round plus tasks command posts.
// Any member can accept up to 3 at once; command can post and remove tasks at any time.
public sealed partial class InstituteUplinkSystem
{
    public const int BoardLimit = 3;
    private const int PresetSteal = 2;
    private const int PresetPeople = 2;

    private static readonly Dictionary<InstituteObjectiveKind, int> PresetRewards = new()
    {
        { InstituteObjectiveKind.Steal, 15 },
        { InstituteObjectiveKind.Kill, 30 },
        { InstituteObjectiveKind.Protect, 20 },
        { InstituteObjectiveKind.Survive, 10 },
    };

    private void InitializeBoard()
    {
        SubscribeNetworkEvent<InstituteBoardPostEvent>(OnBoardPost);
        SubscribeNetworkEvent<InstituteBoardRemoveEvent>(OnBoardRemove);
        SubscribeNetworkEvent<InstituteBoardAcceptEvent>(OnBoardAccept);
    }

    /// <summary>The board for this round, with the preset tasks rolled the first time anyone looks.</summary>
    public List<InstituteBoardTask>? Board()
    {
        if (RuleComp() is not { } rule)
            return null;

        if (!rule.PresetsMade)
        {
            rule.PresetsMade = true;
            MakePresets(rule);
        }

        // Drop kill/protect tasks whose target is gone.
        rule.Board.RemoveAll(t => t.Person is { } person && (TerminatingOrDeleted(person) || !HasComp<MindComponent>(person)));
        return rule.Board;
    }

    private void MakePresets(InstituteRuleComponent rule)
    {
        foreach (var item in rule.StealTargets.OrderBy(_ => _random.Next()).Take(PresetSteal))
            AddTask(rule, InstituteObjectiveKind.Steal, null, item, string.Empty, string.Empty, PresetRewards[InstituteObjectiveKind.Steal], Loc.GetString("institute-board-preset-by"), true);

        // Outsiders only: the Institute doesn't put hits on its own people.
        var outsiders = _minds.GetAliveHumans()
            .Where(p => !HasComp<InstituteMemberComponent>(p.Owner))
            .OrderBy(_ => _random.Next())
            .Take(PresetPeople)
            .ToList();
        for (var i = 0; i < outsiders.Count; i++)
        {
            var kind = i % 2 == 0 ? InstituteObjectiveKind.Kill : InstituteObjectiveKind.Protect;
            AddTask(rule, kind, outsiders[i].Owner, null, string.Empty, string.Empty, PresetRewards[kind], Loc.GetString("institute-board-preset-by"), true);
        }

        AddTask(rule, InstituteObjectiveKind.Survive, null, null, string.Empty, string.Empty, PresetRewards[InstituteObjectiveKind.Survive], Loc.GetString("institute-board-preset-by"), true);
    }

    private InstituteBoardTask AddTask(InstituteRuleComponent rule, InstituteObjectiveKind kind, EntityUid? person, EntProtoId? item,
        string title, string text, int reward, string by, bool preset)
    {
        var task = new InstituteBoardTask
        {
            Id = rule.NextBoardId++,
            Kind = kind,
            Person = person,
            Item = item,
            Title = Trim(title, MaxTitle),
            Text = Trim(text, MaxText),
            Reward = Math.Clamp(reward, 0, MaxReward),
            PostedBy = by,
            Preset = preset,
        };
        rule.Board.Add(task);
        return task;
    }

    private string TaskTitle(InstituteBoardTask task)
    {
        string PersonName() => task.Person is { } p && TryComp<MindComponent>(p, out var m)
            ? m.CharacterName ?? Loc.GetString("institute-uplink-unknown")
            : Loc.GetString("institute-uplink-unknown");

        return task.Kind switch
        {
            InstituteObjectiveKind.Steal => Loc.GetString("institute-objective-steal-title",
                ("item", task.Item is { } item && _proto.TryIndex(item, out var proto) ? proto.Name : "?")),
            InstituteObjectiveKind.Kill => Loc.GetString("institute-board-kill", ("name", PersonName())),
            InstituteObjectiveKind.Protect => Loc.GetString("institute-board-protect", ("name", PersonName())),
            InstituteObjectiveKind.Survive => Loc.GetString("institute-objective-kind-survive"),
            _ => task.Title,
        };
    }

    /// <summary>Board tasks the member holds and hasn't been paid for.</summary>
    public int BoardHeld(EntityUid mindId)
    {
        if (!TryComp<MindComponent>(mindId, out var mind))
            return 0;

        return mind.Objectives.Count(o => TryComp<InstituteObjectiveComponent>(o, out var inst) && inst.FromBoard && !inst.Paid);
    }

    private void FillBoardState(InstituteNetworkStateEvent state, EntityUid viewerMind)
    {
        state.BoardLimit = BoardLimit;
        state.BoardHeld = BoardHeld(viewerMind);
        if (Board() is not { } board)
            return;

        foreach (var task in board)
        {
            state.Board.Add(new InstituteBoardEntry
            {
                Id = task.Id,
                Title = TaskTitle(task),
                Text = task.Text,
                Reward = task.Reward,
                PostedBy = task.PostedBy,
                Preset = task.Preset,
            });
        }
    }

    private void OnBoardPost(InstituteBoardPostEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetViewer(args.SenderSession, out var viewerMind, out var viewer) || !IsCommand(viewer) ||
            RuleComp() is not { } rule)
            return;

        var person = msg.Person is { } p ? GetEntity(p) : (EntityUid?) null;
        EntProtoId? item = msg.Item == null ? (EntProtoId?) null : new EntProtoId(msg.Item);

        string? error = msg.Kind switch
        {
            InstituteObjectiveKind.Kill or InstituteObjectiveKind.Protect when person == null || !HasComp<MindComponent>(person)
                => Loc.GetString("institute-network-pick-person"),
            InstituteObjectiveKind.Steal when item == null || !rule.StealTargets.Contains(item.Value)
                => Loc.GetString("institute-network-pick-item"),
            InstituteObjectiveKind.Custom when string.IsNullOrWhiteSpace(msg.Title)
                => Loc.GetString("institute-network-need-title"),
            _ => null,
        };
        if (error != null)
        {
            SendState(args.SenderSession, error);
            return;
        }

        Board();
        var by = Comp<MindComponent>(viewerMind).CharacterName ?? Loc.GetString("institute-uplink-unknown");
        var task = AddTask(rule, msg.Kind, person, item, msg.Title, msg.Text, msg.Reward, by, false);
        SendState(args.SenderSession, Loc.GetString("institute-board-posted", ("title", TaskTitle(task))));
    }

    private void OnBoardRemove(InstituteBoardRemoveEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetViewer(args.SenderSession, out _, out var viewer) || !IsCommand(viewer) ||
            Board() is not { } board)
            return;

        var task = board.FirstOrDefault(t => t.Id == msg.Id);
        if (task == null)
            return;

        board.Remove(task);
        SendState(args.SenderSession, Loc.GetString("institute-board-removed", ("title", TaskTitle(task))));
    }

    private void OnBoardAccept(InstituteBoardAcceptEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetViewer(args.SenderSession, out var viewerMind, out _) || Board() is not { } board)
            return;

        var task = board.FirstOrDefault(t => t.Id == msg.Id);
        if (task == null)
        {
            SendState(args.SenderSession, Loc.GetString("institute-board-gone"));
            return;
        }

        if (BoardHeld(viewerMind) >= BoardLimit)
        {
            SendState(args.SenderSession, Loc.GetString("institute-board-limit", ("limit", BoardLimit)));
            return;
        }

        var obj = CreateOrder(viewerMind, task.Kind, task.Person, task.Item, task.Title, task.Text, task.Reward,
            task.PostedBy, true, out var error);
        if (obj == null)
        {
            SendState(args.SenderSession, error);
            return;
        }

        board.Remove(task);
        SendState(args.SenderSession, Loc.GetString("institute-board-accepted", ("title", Name(obj.Value))));
    }
}
