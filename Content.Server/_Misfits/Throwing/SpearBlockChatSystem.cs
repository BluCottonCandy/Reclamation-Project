// #Misfits Add - Server-side handler for SpearBlockedEvent.
// Converts the legacy SharedPopupSystem broadcast into a TrySendInGameICMessage emote
// so nearby players see the deflection in chat rather than as a sprite popup.
using Content.Server.Chat.Systems;
using Content.Shared._Misfits.Throwing;
using Content.Shared._Misfits.Throwing.Components;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;

namespace Content.Server._Misfits.Throwing;

public sealed class SpearBlockChatSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        // SpearBlockedEvent is raised on the blocker entity (which has SpearBlockUserComponent).
        SubscribeLocalEvent<SpearBlockUserComponent, SpearBlockedEvent>(OnSpearBlocked);
    }

    private void OnSpearBlocked(EntityUid uid, SpearBlockUserComponent comp, ref SpearBlockedEvent ev)
    {
        var spearName = Name(ev.Thrown);
        var throwerName = ev.Thrower is { } throwerId
            ? Identity.Name(throwerId, EntityManager)
            : Loc.GetString("spear-block-unknown-thrower");

        string message;

        if (ev.BlockEntity.HasValue)
        {
            var shieldName = Name(ev.BlockEntity.Value);
            message = Loc.GetString("spear-block-embedded-emote",
                ("thrower", throwerName),
                ("spear", spearName),
                ("shield", shieldName));
        }
        else
        {
            message = Loc.GetString("spear-block-deflected-emote",
                ("thrower", throwerName),
                ("spear", spearName));
        }

        // Emote broadcast — emote system prefixes the blocker's name automatically.
        _chat.TrySendInGameICMessage(uid, message,
            InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
    }
}
