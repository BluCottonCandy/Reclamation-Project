// #Misfits Add: Broadcasts a chat emote when a player is successfully restrained with handcuffs.
using Content.Server.Chat.Systems;
using Content.Shared._Misfits.Cuffs;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;

namespace Content.Server._Misfits.Cuffs;

/// <summary>
/// Hooks <see cref="CuffAppliedEvent"/> to send a local-area emote chat message so nearby
/// players see "* Jane Smith restrains John Doe *" in the emote channel.
/// </summary>
public sealed class CuffingChatSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        // CuffAppliedEvent is raised on the target entity after successful restraint.
        SubscribeLocalEvent<CuffAppliedEvent>(OnCuffApplied);
    }

    /// <summary>
    /// Triggered after cuffs are successfully applied; sends an emote chat message to nearby players.
    /// </summary>
    private void OnCuffApplied(ref CuffAppliedEvent ev)
    {
        // Resolve the display name of the person being restrained.
        var targetName = Identity.Entity(ev.Target, EntityManager);

        var message = ev.User == ev.Target
            ? Loc.GetString("misfits-chat-cuff-self")
            : Loc.GetString("misfits-chat-cuff-applied", ("target", targetName));

        // Send as an emote from the user — visible to nearby players in the Emotes channel.
        _chat.TrySendInGameICMessage(ev.User, message, InGameICChatType.Emote,
            ChatTransmitRange.Normal, ignoreActionBlocker: true);
    }
}
