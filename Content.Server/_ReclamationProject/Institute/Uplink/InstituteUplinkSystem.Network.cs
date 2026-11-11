using System.Linq;
using Content.Server.Objectives.Components;
using Content.Server.Objectives.Systems;
using Content.Shared._ReclamationProject.Institute.Uplink;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Objectives.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Institute.Uplink;

// #Reclamation Project - Institute network: send points, reset PINs, hand out objectives and pay rewards.
public sealed partial class InstituteUplinkSystem
{
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedObjectivesSystem _objectives = default!;
    [Dependency] private readonly TargetObjectiveSystem _targets = default!;

    private const int MaxReward = 500;
    private const int MaxTitle = 100;
    private const int MaxText = 500;

    private static readonly Dictionary<InstituteObjectiveKind, EntProtoId> ObjectiveProtos = new()
    {
        { InstituteObjectiveKind.Steal, "InstituteStealObjective" },
        { InstituteObjectiveKind.Kill, "InstituteKillObjective" },
        { InstituteObjectiveKind.Protect, "InstituteProtectObjective" },
        { InstituteObjectiveKind.Survive, "InstituteSurviveObjective" },
        { InstituteObjectiveKind.Custom, "InstituteCustomObjective" },
    };

    /// <summary>Kill/protect target for the objective currently being created.</summary>
    private EntityUid? _pendingTarget;

    private void InitializeNetwork()
    {
        SubscribeLocalEvent<ActorComponent, OpenInstituteNetworkEvent>(OnOpenNetwork);
        SubscribeLocalEvent<InstituteObjectiveComponent, ObjectiveAssignedEvent>(OnObjectiveAssigned);
        SubscribeLocalEvent<InstituteObjectiveComponent, ObjectiveGetProgressEvent>(OnObjectiveProgress,
            after: new[] { typeof(KillPersonConditionSystem), typeof(KeepAliveConditionSystem), typeof(SurviveConditionSystem) });

        SubscribeNetworkEvent<InstituteNetworkRequestEvent>(OnNetworkRequest);
        SubscribeNetworkEvent<InstituteSendPointsEvent>(OnSendPoints);
        SubscribeNetworkEvent<InstituteResetPinEvent>(OnResetPin);
        SubscribeNetworkEvent<InstituteSetPinEvent>(OnSetPin);
        SubscribeNetworkEvent<InstituteAssignObjectiveEvent>(OnAssignObjective);
        SubscribeNetworkEvent<InstituteObjectiveActionEvent>(OnObjectiveAction);
    }

    #region Viewer checks

    private bool TryGetViewer(ICommonSession session, out EntityUid mindId, out InstituteMemberComponent member)
    {
        member = default!;
        mindId = default;
        if (!_minds.TryGetMind(session, out mindId, out _) ||
            !TryComp<InstituteMemberComponent>(mindId, out var comp))
            return false;

        member = comp;
        return true;
    }

    private static bool IsCommand(InstituteMemberComponent member)
    {
        return member.Job == Director || member.Job == DivisionHead;
    }

    /// <summary>
    /// The Director manages everyone. A Division Head manages only the jobs below them.
    /// </summary>
    private static bool CanManage(InstituteMemberComponent viewer, InstituteMemberComponent target)
    {
        // Command can always give themselves orders, even with nobody else around.
        if (viewer == target)
            return IsCommand(viewer);
        if (viewer.Job == Director)
            return true;
        return viewer.Job == DivisionHead && target.Job != Director && target.Job != DivisionHead;
    }

    #endregion

    #region Window state

    private void OnOpenNetwork(Entity<ActorComponent> ent, ref OpenInstituteNetworkEvent args)
    {
        SendState(ent.Comp.PlayerSession);
    }

    private void OnNetworkRequest(InstituteNetworkRequestEvent msg, EntitySessionEventArgs args)
    {
        SendState(args.SenderSession);
    }

    private void SendState(ICommonSession session, string? status = null)
    {
        if (!TryGetViewer(session, out var viewerMind, out var viewer))
            return;

        var state = new InstituteNetworkStateEvent
        {
            Self = GetNetEntity(viewerMind),
            IsCommand = IsCommand(viewer),
            IsDirector = viewer.Job == Director,
            Status = status,
        };

        if (MemberStore(viewerMind, out _) is { } ownStore)
            state.Balance = Balance(ownStore);

        var query = EntityQueryEnumerator<InstituteMemberComponent, MindComponent>();
        while (query.MoveNext(out var mindId, out var member, out var mind))
        {
            var entry = new InstituteMemberEntry
            {
                Mind = GetNetEntity(mindId),
                Name = mind.CharacterName ?? Loc.GetString("institute-uplink-unknown"),
                Job = _jobs.MindTryGetJobName(mindId),
                CanManage = CanManage(viewer, member),
            };

            if (entry.CanManage)
                entry.Pin = GetPin(mindId);

            if (entry.CanManage)
            {
                foreach (var objective in mind.Objectives)
                {
                    if (!TryComp<InstituteObjectiveComponent>(objective, out var inst))
                        continue;

                    entry.Objectives.Add(new InstituteObjectiveEntry
                    {
                        Objective = GetNetEntity(objective),
                        Title = Name(objective),
                        Reward = inst.Reward,
                        Done = (_objectives.GetProgress(objective, (mindId, mind)) ?? 0) >= 0.999f,
                        Paid = inst.Paid,
                    });
                }
            }

            state.Members.Add(entry);
        }

        if (state.IsDirector)
            state.Stock = StockEntries();

        FillBoardState(state, viewerMind);

        if (state.IsCommand)
        {
            foreach (var person in _minds.GetAliveHumans())
            {
                state.People.Add(new InstitutePersonEntry
                {
                    Mind = GetNetEntity(person.Owner),
                    Name = $"{person.Comp.CharacterName ?? Name(person.Owner)} ({_jobs.MindTryGetJobName(person.Owner)})",
                });
            }
            state.People.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            if (RuleComp() is { } rule)
            {
                foreach (var item in rule.StealTargets)
                {
                    if (_proto.TryIndex(item, out var proto))
                        state.StealItems.Add(new InstituteItemEntry { Prototype = item, Name = proto.Name });
                }
            }
        }

        RaiseNetworkEvent(state, session);
    }

    #endregion

    #region Points and PINs

    private void OnSendPoints(InstituteSendPointsEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetViewer(args.SenderSession, out var senderMind, out _))
            return;

        var targetMind = GetEntity(msg.Target);
        if (msg.Amount <= 0 || targetMind == senderMind ||
            MemberStore(senderMind, out var senderImplant) is not { } senderStore ||
            MemberStore(targetMind, out var targetImplant) is not { } targetStore)
            return;

        if (Balance(senderStore) < msg.Amount)
        {
            SendState(args.SenderSession, Loc.GetString("institute-network-not-enough"));
            return;
        }

        AddPoints(senderImplant, senderStore, -msg.Amount);
        AddPoints(targetImplant, targetStore, msg.Amount);

        var from = Comp<MindComponent>(senderMind).CharacterName ?? Loc.GetString("institute-uplink-unknown");
        var to = Comp<MindComponent>(targetMind).CharacterName ?? Loc.GetString("institute-uplink-unknown");
        Message(targetMind, Loc.GetString("institute-network-points-received", ("amount", msg.Amount), ("name", from)));
        SendState(args.SenderSession, Loc.GetString("institute-network-points-sent", ("amount", msg.Amount), ("name", to)));
    }

    private void OnResetPin(InstituteResetPinEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetViewer(args.SenderSession, out var viewerMind, out var viewer) || !IsCommand(viewer))
            return;

        var by = Comp<MindComponent>(viewerMind).CharacterName ?? Loc.GetString("institute-uplink-unknown");
        var count = 0;
        var query = EntityQueryEnumerator<InstituteMemberComponent>();
        while (query.MoveNext(out var mindId, out var member))
        {
            if (msg.Target is { } target && GetEntity(target) != mindId)
                continue;
            if (!CanManage(viewer, member))
                continue;

            ResetPin(mindId, by);
            count++;
        }

        SendState(args.SenderSession, Loc.GetString("institute-network-pins-reset", ("count", count)));
    }

    private void OnSetPin(InstituteSetPinEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetViewer(args.SenderSession, out var viewerMind, out var viewer) || !IsCommand(viewer))
            return;

        var target = GetEntity(msg.Target);
        if (!TryComp<InstituteMemberComponent>(target, out var member) || !CanManage(viewer, member))
            return;

        var pin = msg.Pin.Trim();
        if (!ValidPin(pin))
        {
            SendState(args.SenderSession, Loc.GetString("institute-network-pin-invalid"));
            return;
        }

        var by = Comp<MindComponent>(viewerMind).CharacterName ?? Loc.GetString("institute-uplink-unknown");
        ResetPin(target, by, pin);
        var name = Comp<MindComponent>(target).CharacterName ?? Loc.GetString("institute-uplink-unknown");
        SendState(args.SenderSession, Loc.GetString("institute-network-pin-set", ("name", name)));
    }

    #endregion

    #region Objectives

    private void OnAssignObjective(InstituteAssignObjectiveEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetViewer(args.SenderSession, out var viewerMind, out var viewer) || !IsCommand(viewer))
            return;

        var targetMind = GetEntity(msg.Target);
        if (!TryComp<InstituteMemberComponent>(targetMind, out var targetMember) ||
            !CanManage(viewer, targetMember))
            return;

        var by = Comp<MindComponent>(viewerMind).CharacterName ?? Loc.GetString("institute-uplink-unknown");
        var person = msg.Person is { } p ? GetEntity(p) : (EntityUid?) null;
        EntProtoId? item = msg.Item == null ? (EntProtoId?) null : new EntProtoId(msg.Item);
        var obj = CreateOrder(targetMind, msg.Kind, person, item, msg.Title, msg.Text, msg.Reward, by, false, out var error);
        SendState(args.SenderSession, obj is { } made
            ? Loc.GetString("institute-network-objective-given", ("title", Name(made)))
            : error);
    }

    /// <summary>
    /// Makes an Institute objective for a member and tells them. Used by command orders and the task board.
    /// Returns null with an error message when the order isn't valid.
    /// </summary>
    private EntityUid? CreateOrder(EntityUid targetMind, InstituteObjectiveKind kind, EntityUid? person, EntProtoId? item,
        string title, string text, int reward, string by, bool fromBoard, out string error)
    {
        error = Loc.GetString("institute-network-objective-failed");
        if (!TryComp<MindComponent>(targetMind, out var mind) || !ObjectiveProtos.TryGetValue(kind, out var proto))
            return null;

        reward = Math.Clamp(reward, 0, MaxReward);
        EntProtoId? stealItem = null;
        _pendingTarget = null;

        switch (kind)
        {
            case InstituteObjectiveKind.Kill:
            case InstituteObjectiveKind.Protect:
                if (person is not { } target || !HasComp<MindComponent>(target))
                {
                    error = Loc.GetString("institute-network-pick-person");
                    return null;
                }
                if (target == targetMind)
                {
                    error = Loc.GetString("institute-board-not-yourself");
                    return null;
                }
                _pendingTarget = target;
                break;
            case InstituteObjectiveKind.Steal:
                if (item == null || RuleComp() is not { } rule || !rule.StealTargets.Contains(item.Value))
                {
                    error = Loc.GetString("institute-network-pick-item");
                    return null;
                }
                stealItem = item;
                break;
            case InstituteObjectiveKind.Custom:
                if (string.IsNullOrWhiteSpace(title))
                {
                    error = Loc.GetString("institute-network-need-title");
                    return null;
                }
                break;
        }

        var objective = _objectives.TryCreateObjective(targetMind, mind, proto);
        _pendingTarget = null;
        if (objective is not { } obj)
            return null;

        if (stealItem is { } steal)
        {
            Comp<InstituteStealObjectiveComponent>(obj).Item = steal;
            var itemName = _proto.Index(steal).Name;
            _meta.SetEntityName(obj, Loc.GetString("institute-objective-steal-title", ("item", itemName)));
            _meta.SetEntityDescription(obj, Loc.GetString("institute-objective-steal-desc", ("item", itemName)));
        }
        else if (kind == InstituteObjectiveKind.Custom)
        {
            _meta.SetEntityName(obj, Trim(title, MaxTitle));
            _meta.SetEntityDescription(obj, string.IsNullOrWhiteSpace(text)
                ? Loc.GetString("institute-objective-custom-desc")
                : Trim(text, MaxText));
        }

        var inst = Comp<InstituteObjectiveComponent>(obj);
        inst.Reward = reward;
        inst.Assigner = by;
        inst.FromBoard = fromBoard;
        _minds.AddObjective(targetMind, mind, obj);

        var orders = Loc.GetString("institute-network-new-orders", ("by", by), ("title", Name(obj)), ("reward", reward));
        Message(targetMind, orders);
        if (mind.OwnedEntity is { } body)
            _popup.PopupEntity(orders, body, body);

        return obj;
    }

    private static string Trim(string text, int max)
    {
        text = text.Trim();
        return text.Length > max ? text[..max] : text;
    }

    private void OnObjectiveAssigned(Entity<InstituteObjectiveComponent> ent, ref ObjectiveAssignedEvent args)
    {
        if (_pendingTarget is { } target && TryComp<TargetObjectiveComponent>(ent, out var targetComp))
            _targets.SetTarget(ent, target, targetComp);
    }

    private void OnObjectiveAction(InstituteObjectiveActionEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetViewer(args.SenderSession, out _, out var viewer) || !IsCommand(viewer))
            return;

        var objective = GetEntity(msg.Objective);
        if (!TryComp<InstituteObjectiveComponent>(objective, out var inst))
            return;

        var query = EntityQueryEnumerator<InstituteMemberComponent, MindComponent>();
        while (query.MoveNext(out var mindId, out var member, out var mind))
        {
            var index = mind.Objectives.IndexOf(objective);
            if (index < 0)
                continue;
            if (!CanManage(viewer, member))
                return;

            var title = Name(objective);
            if (msg.Remove)
            {
                _minds.TryRemoveObjective(mindId, mind, index);
                Message(mindId, Loc.GetString("institute-network-objective-removed-member", ("title", title)));
                SendState(args.SenderSession, Loc.GetString("institute-network-objective-removed", ("title", title)));
            }
            else
            {
                inst.ForceCompleted = true;
                TryPay(mindId, mind, objective, inst);
                SendState(args.SenderSession, Loc.GetString("institute-network-objective-forced", ("title", title)));
            }
            return;
        }
    }

    private void OnObjectiveProgress(Entity<InstituteObjectiveComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (ent.Comp.ForceCompleted)
        {
            args.Progress = 1f;
            return;
        }

        if (HasComp<InstituteCustomObjectiveComponent>(ent))
        {
            args.Progress = 0f;
            return;
        }

        if (TryComp<InstituteStealObjectiveComponent>(ent, out var steal))
            args.Progress = StealProgress(args.Mind.OwnedEntity, steal);
    }

    private float StealProgress(EntityUid? body, InstituteStealObjectiveComponent steal)
    {
        if (body == null || steal.Item is not { } item)
            return 0f;

        var found = 0;
        var pending = new Stack<EntityUid>();
        var visited = new HashSet<EntityUid> { body.Value };
        pending.Push(body.Value);
        while (pending.TryPop(out var entity))
        {
            if (!TryComp<ContainerManagerComponent>(entity, out var manager))
                continue;

            foreach (var container in manager.Containers.Values)
            foreach (var contained in container.ContainedEntities)
            {
                if (!visited.Add(contained))
                    continue;
                if (MetaData(contained).EntityPrototype?.ID == item.Id)
                    found++;
                pending.Push(contained);
            }
        }

        return Math.Min(1f, found / (float) Math.Max(1, steal.Count));
    }

    private void UpdatePayouts()
    {
        var query = EntityQueryEnumerator<InstituteMemberComponent, MindComponent>();
        while (query.MoveNext(out var mindId, out _, out var mind))
        {
            foreach (var objective in mind.Objectives.ToArray())
            {
                if (TryComp<InstituteObjectiveComponent>(objective, out var inst) && !inst.Paid && inst.AutoPay)
                    TryPay(mindId, mind, objective, inst);
            }
        }
    }

    private void TryPay(EntityUid mindId, MindComponent mind, EntityUid objective, InstituteObjectiveComponent inst)
    {
        if (inst.Paid)
            return;
        if (!inst.ForceCompleted && (_objectives.GetProgress(objective, (mindId, mind)) ?? 0) < 0.999f)
            return;

        inst.Paid = true;
        if (MemberStore(mindId, out var implant) is { } store)
            AddPoints(implant, store, inst.Reward);

        Message(mindId, Loc.GetString("institute-network-objective-paid", ("title", Name(objective)), ("reward", inst.Reward)));
    }

    #endregion
}
