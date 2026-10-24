using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Preferences;
using Robust.Client;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby
{
    /// <summary>
    ///     Receives <see cref="PlayerPreferences" /> and <see cref="GameSettings" /> from the server during the initial
    ///     connection.
    ///     Stores preferences on the server through <see cref="SelectCharacter" /> and <see cref="UpdateCharacter" />.
    /// </summary>
    public sealed class ClientPreferencesManager : IClientPreferencesManager
    {
        [Dependency] private readonly IClientNetManager _netManager = default!;
        [Dependency] private readonly IBaseClient _baseClient = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IPrototypeManager _prototypes = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;

        public event Action? OnServerDataLoaded;

        public GameSettings Settings { get; private set; } = default!;
        public PlayerPreferences Preferences { get; private set; } = default!;

        public void Initialize()
        {
            _netManager.RegisterNetMessage<MsgPreferencesAndSettings>(HandlePreferencesAndSettings);
            _netManager.RegisterNetMessage<MsgUpdateCharacter>(HandleServerCharacterUpdate);
            _netManager.RegisterNetMessage<MsgSelectCharacter>();
            _netManager.RegisterNetMessage<MsgDeleteCharacter>();
            _netManager.RegisterNetMessage<MsgRoundEndReportAnonymity>();

            _baseClient.RunLevelChanged += BaseClientOnRunLevelChanged;
        }

        private void BaseClientOnRunLevelChanged(object? sender, RunLevelChangedEventArgs e)
        {
            if (e.NewLevel == ClientRunLevel.Initialize)
            {
                Settings = default!;
                Preferences = default!;
            }
        }

        public void SelectCharacter(ICharacterProfile profile)
        {
            SelectCharacter(Preferences.IndexOfCharacter(profile));
        }

        public void SelectCharacter(int slot)
        {
            if (!Preferences.Characters.ContainsKey(slot))
                return;

            Preferences = new PlayerPreferences(Preferences.Characters, slot, Preferences.AdminOOCColor, Preferences.AnonymousRoundEndReport);
            var msg = new MsgSelectCharacter
            {
                SelectedCharacterIndex = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void UpdateCharacter(ICharacterProfile profile, int slot)
        {
            var collection = IoCManager.Instance!;
            profile.EnsureValid(_playerManager.LocalSession!, collection);
            var characters = new Dictionary<int, ICharacterProfile>(Preferences.Characters) {[slot] = profile};
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.AnonymousRoundEndReport);
            var msg = new MsgUpdateCharacter
            {
                Profile = profile,
                Slot = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void CreateCharacter(ICharacterProfile profile)
        {
            var characters = new Dictionary<int, ICharacterProfile>(Preferences.Characters);
            var lowest = Enumerable.Range(0, Settings.MaxCharacterSlots)
                .Except(characters.Keys)
                .FirstOrNull();

            if (lowest == null)
            {
                throw new InvalidOperationException("Out of character slots!");
            }

            var l = lowest.Value;
            characters.Add(l, profile);
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.AnonymousRoundEndReport);

            UpdateCharacter(profile, l);
        }

        public void DeleteCharacter(ICharacterProfile profile)
        {
            DeleteCharacter(Preferences.IndexOfCharacter(profile));
        }

        public void DeleteCharacter(int slot)
        {
            // Switch to another character before deleting this one, including stale UI requests.
            if (slot == Preferences.SelectedCharacterIndex
                || Preferences.Characters.Count <= 1
                || !Preferences.Characters.ContainsKey(slot))
                return;

            var characters = Preferences.Characters.Where(p => p.Key != slot);
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.AnonymousRoundEndReport);
            var msg = new MsgDeleteCharacter
            {
                Slot = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void SetRoundEndReportAnonymity(bool anonymous)
        {
            Preferences = new PlayerPreferences(Preferences.Characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, anonymous);
            var msg = new MsgRoundEndReportAnonymity
            {
                Anonymous = anonymous
            };
            _netManager.ClientSendMessage(msg);
        }

        private void HandleServerCharacterUpdate(MsgUpdateCharacter message)
        {
            // A delayed server save must not replace newer local characters, selection, or edits.
            // Only earned brands are server-owned; merge those into this character's latest draft.
            if (message.Profile is not HumanoidCharacterProfile saved
                || !Preferences.Characters.TryGetValue(message.Slot, out var current)
                || current is not HumanoidCharacterProfile draft
                || saved.Name != draft.Name)
                return;

            var profile = Content.Shared._ReclamationProject.Forged.ForgedBrandingProfiles.PreserveEarned(draft, saved);
            var characters = new Dictionary<int, ICharacterProfile>(Preferences.Characters) { [message.Slot] = profile };
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor, Preferences.AnonymousRoundEndReport);
            OnServerDataLoaded?.Invoke();
        }

        private void HandlePreferencesAndSettings(MsgPreferencesAndSettings message)
        {
            Preferences = message.Preferences;
            Settings = message.Settings;

            OnServerDataLoaded?.Invoke();
        }
    }
}
