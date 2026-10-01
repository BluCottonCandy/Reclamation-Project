// #Misfits Change /Add/ - Smelling salts now perform a long resuscitation interaction instead of injecting a reagent.
using Content.Server.Chat.Managers;
using Content.Server.DoAfter;
using Content.Shared._Misfits.Special;
using Content.Shared.Chat;
using Content.Shared.DoAfter;
using Content.Shared._Misfits.Medical;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Enums;

namespace Content.Server._Misfits.Medical;

public sealed class SmellingSaltsSystem : EntitySystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly ResuscitationSystem _resuscitation = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SmellingSaltsComponent, Content.Shared.Interaction.AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<SmellingSaltsComponent, SmellingSaltsDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(EntityUid uid, SmellingSaltsComponent component, Content.Shared.Interaction.AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target)
            return;

        args.Handled = TryStartUse(uid, target, args.User, component);
    }

    public bool TryStartUse(EntityUid uid, EntityUid target, EntityUid user, SmellingSaltsComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (!_resuscitation.CanResuscitate(target, false, component.CanReviveCrit))
            return false;

        if (component.UseSound != null)
            _audio.PlayPvs(component.UseSound, uid);

        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, _special.GetIntelligenceMedicalActionDelay(user, component.DoAfterDuration), new SmellingSaltsDoAfterEvent(),
            uid, target, uid)
            {
                BlockDuplicate = true,
                BreakOnHandChange = true,
                NeedHand = true,
                BreakOnMove = !component.AllowMovement,
            });

        if (started)
            _resuscitation.SendAttemptEmote(target, uid);

        return started;
    }

    private void OnDoAfter(EntityUid uid, SmellingSaltsComponent component, SmellingSaltsDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (args.Target is not { } target)
            return;

        if (!_resuscitation.CanResuscitate(target, false, component.CanReviveCrit))
            return;

        args.Handled = true;
        var result = _resuscitation.TryResuscitate(uid,
            target,
            args.User,
            component.ReviveHeal,
            "smelling-salts-revive-do");

        if (result.Rotten || !result.HasMindSession)
        {
            // Private feedback message to the user only.
            if (_playerManager.TryGetSessionByEntity(args.User, out var session)
                && session.Status == SessionStatus.InGame)
            {
                var feedbackMsg = result.Rotten
                    ? Loc.GetString("smelling-salts-rotten")
                    : Loc.GetString("smelling-salts-no-mind");
                _chatManager.ChatMessageToOne(ChatChannel.Local, feedbackMsg, feedbackMsg,
                    EntityUid.Invalid, false, session.Channel);
            }
        }

        QueueDel(uid);
    }
}
