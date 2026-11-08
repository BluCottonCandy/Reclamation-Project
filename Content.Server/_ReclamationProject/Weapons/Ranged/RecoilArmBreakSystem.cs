// #Reclamation Project Add - Breaks the firing arm of shooters too weak for the gun's recoil.
using Content.Server._ReclamationProject.Medical;
using Content.Server.Chat.Managers;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._ReclamationProject.Weapons.Ranged;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._ReclamationProject.Weapons.Ranged;

public sealed class RecoilArmBreakSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly TraumaSystem _trauma = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    // Popups fade after about 0.04 s per letter, so the shooter's popup is shown again
    // after this delay to keep it on screen for roughly twice as long.
    private static readonly TimeSpan RepeatPopupDelay = TimeSpan.FromSeconds(2);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RecoilArmBreakComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(Entity<RecoilArmBreakComponent> gun, ref GunShotEvent args)
    {
        var user = args.User;

        // Exempt shooters (e.g. Coursers) are built to handle the recoil.
        if (_whitelist.IsWhitelistPass(gun.Comp.Exempt, user))
            return;

        // Only characters that actually have SPECIAL stats can be too weak.
        // NPCs, turrets and stat-less mobs fire normally.
        if (!SpecialStats.IsEnabled(SpecialStat.Strength) ||
            !TryComp<SpecialComponent>(user, out var special) ||
            !_special.UsesSpecialStats(user))
            return;

        if (_special.GetEffective(user, SpecialStat.Strength, special) >= gun.Comp.MinStrength)
            return;

        // Find which hand is holding the gun, so the matching arm breaks.
        if (!_hands.IsHolding(user, gun.Owner, out var hand))
            return;

        var symmetry = hand.Location switch
        {
            HandLocation.Left => BodyPartSymmetry.Left,
            HandLocation.Right => BodyPartSymmetry.Right,
            _ => (BodyPartSymmetry?) null,
        };
        if (symmetry == null)
            return;

        foreach (var (arm, _) in _body.GetBodyChildrenOfType(user, BodyPartType.Arm, symmetry: symmetry))
        {
            if (HasComp<FractureComponent>(arm))
                continue;

            _trauma.Fracture(arm);
            var selfMessage = Loc.GetString(gun.Comp.BreakPopup, ("gun", gun.Owner));
            _popup.PopupEntity(selfMessage, user, user, PopupType.LargeCaution);
            Timer.Spawn(RepeatPopupDelay, () =>
            {
                if (!TerminatingOrDeleted(user))
                    _popup.PopupEntity(selfMessage, user, user, PopupType.LargeCaution);
            });

            // The chat line stays in the log, so the shooter can always see what happened.
            if (TryComp<ActorComponent>(user, out var actor))
            {
                var wrapped = $"[color=red][bold]{FormattedMessage.EscapeText(selfMessage)}[/bold][/color]";
                _chat.ChatMessageToOne(ChatChannel.Local, selfMessage, wrapped, EntityUid.Invalid, false, actor.PlayerSession.Channel);
            }
            _popup.PopupEntity(Loc.GetString(gun.Comp.BreakPopupOthers, ("user", user), ("gun", gun.Owner)), user,
                Filter.PvsExcept(user), true, PopupType.MediumCaution);
            break;
        }
    }
}
