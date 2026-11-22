using Content.Shared.Abilities.Psionics;
using Content.Client.Chat.Managers;
using Robust.Client.Player;

namespace Content.Client.Chat
{
    public sealed class PsionicChatUpdateSystem : EntitySystem
    {
        [Dependency] private readonly IChatManager _chatManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<PsionicComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<PsionicComponent, ComponentRemove>(OnRemove);
            // #Reclamation Project - magic telepathy (Psychic/Dark spells, Psychic Link, Hivemind) also opens the channel.
            SubscribeLocalEvent<TelepathyComponent, ComponentInit>((_, _, _) => _chatManager.UpdatePermissions());
            SubscribeLocalEvent<TelepathyComponent, ComponentRemove>((_, _, _) => _chatManager.UpdatePermissions());
        }

        public PsionicComponent? Player => CompOrNull<PsionicComponent>(_playerManager.LocalPlayer?.ControlledEntity);
        // #Reclamation Project - telepathy alone (no psionic powers) is enough to use the telepathic channel.
        public bool IsPsionic => Player != null || HasComp<TelepathyComponent>(_playerManager.LocalPlayer?.ControlledEntity);

        private void OnInit(EntityUid uid, PsionicComponent component, ComponentInit args)
        {
            _chatManager.UpdatePermissions();
        }

        private void OnRemove(EntityUid uid, PsionicComponent component, ComponentRemove args)
        {
            _chatManager.UpdatePermissions();
        }
    }
}
