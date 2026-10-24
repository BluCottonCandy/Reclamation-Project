#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Content.Client.Lobby;
using Content.Shared.Preferences;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Network;

namespace Content.Tests.Client.Lobby;

[TestFixture]
public sealed class CharacterSelectionTest
{
    private static ClientPreferencesManager CreateManager(PlayerPreferences prefs, out NetworkProxy network)
    {
        var proxy = DispatchProxy.Create<IClientNetManager, NetworkProxy>();
        network = (NetworkProxy) (object) proxy;
        var manager = new ClientPreferencesManager();
        typeof(ClientPreferencesManager).GetField("_netManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, proxy);
        typeof(ClientPreferencesManager).GetProperty(nameof(manager.Preferences))!
            .SetValue(manager, prefs);
        return manager;
    }

    private static PlayerPreferences Preferences(params int[] slots)
    {
        var characters = new Dictionary<int, ICharacterProfile>();
        foreach (var slot in slots)
            characters.Add(slot, new HumanoidCharacterProfile());
        return new PlayerPreferences(characters, slots[^1], Color.Red);
    }

    [Test]
    public void StalePickerCannotSelectSlotMissingFromServerSnapshot()
    {
        var manager = CreateManager(Preferences(0, 2), out var network);
        var message = new MsgPreferencesAndSettings
        {
            Preferences = Preferences(0),
            Settings = new GameSettings { MaxCharacterSlots = 10 },
        };
        typeof(ClientPreferencesManager).GetMethod("HandlePreferencesAndSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(manager, new object[] { message });
        manager.SelectCharacter(2);

        Assert.That(manager.Preferences.SelectedCharacterIndex, Is.Zero);
        Assert.That(manager.Preferences.SelectedCharacter, Is.Not.Null);
        Assert.That(network.Messages, Is.Zero);
    }

    [Test]
    public void SelectedCharacterCannotBeDeleted()
    {
        var prefs = Preferences(0, 2);
        var manager = CreateManager(prefs, out var network);
        manager.DeleteCharacter(2);
        Assert.That(manager.Preferences, Is.SameAs(prefs));
        Assert.That(network.Messages, Is.Zero);
    }

    [Test]
    public void SwitchingCharactersAllowsDeletingPreviousSelection()
    {
        var manager = CreateManager(Preferences(0, 2), out var network);
        manager.SelectCharacter(0);
        manager.DeleteCharacter(2);
        Assert.That(manager.Preferences.SelectedCharacterIndex, Is.Zero);
        Assert.That(manager.Preferences.Characters.Keys, Is.EquivalentTo(new[] { 0 }));
        Assert.That(network.Messages, Is.EqualTo(2));
    }

    [Test]
    public void LastCharacterCannotBeDeleted()
    {
        var prefs = Preferences(2);
        var manager = CreateManager(prefs, out var network);
        manager.DeleteCharacter(2);
        Assert.That(manager.Preferences, Is.SameAs(prefs));
        Assert.That(network.Messages, Is.Zero);
    }

    [Test]
    public void UnknownSlotCannotBeDeleted()
    {
        var prefs = Preferences(0, 2);
        var manager = CreateManager(prefs, out var network);
        manager.DeleteCharacter(99);
        Assert.That(manager.Preferences, Is.SameAs(prefs));
        Assert.That(network.Messages, Is.Zero);
    }

    [Test]
    public void DelayedBrandReplyPreservesNewCharactersSelectionAndDraft()
    {
        var draft = new HumanoidCharacterProfile { Name = "Original", FlavorText = "Unsaved draft" };
        var prefs = new PlayerPreferences(new Dictionary<int, ICharacterProfile>
        {
            [0] = draft,
            [2] = new HumanoidCharacterProfile { Name = "New character" },
        }, 2, Color.Red);
        var manager = CreateManager(prefs, out _);
        var canonical = Content.Shared._ReclamationProject.Forged.ForgedBrandingProfiles.WithBrand(
            new HumanoidCharacterProfile { Name = "Original" },
            Content.Shared._ReclamationProject.Forged.ForgedBrandingProfiles.Forged);
        ApplyServerReply(manager, 0, canonical);

        Assert.That(manager.Preferences.SelectedCharacterIndex, Is.EqualTo(2));
        Assert.That(manager.Preferences.Characters.Count, Is.EqualTo(2));
        var updated = (HumanoidCharacterProfile) manager.Preferences.GetProfile(0);
        Assert.That(updated.FlavorText, Is.EqualTo("Unsaved draft"));
        Assert.That(updated.Appearance.Markings.Any(m => m.MarkingId ==
            Content.Shared._ReclamationProject.Forged.ForgedBrandingProfiles.Forged), Is.True);
    }

    [Test]
    public void DelayedBrandReplyDoesNotRestoreDeletedCharacter()
    {
        var prefs = Preferences(0);
        var manager = CreateManager(prefs, out _);
        ApplyServerReply(manager, 2, new HumanoidCharacterProfile());
        Assert.That(manager.Preferences, Is.SameAs(prefs));
    }

    [Test]
    public void ServerUnbrandingPreservesOtherDraftEdits()
    {
        var draft = Content.Shared._ReclamationProject.Forged.ForgedBrandingProfiles.WithBrand(
            new HumanoidCharacterProfile { Name = "Original", FlavorText = "Unsaved draft" },
            Content.Shared._ReclamationProject.Forged.ForgedBrandingProfiles.Forged);
        var prefs = new PlayerPreferences(new Dictionary<int, ICharacterProfile> { [0] = draft }, 0, Color.Red);
        var manager = CreateManager(prefs, out _);
        ApplyServerReply(manager, 0, new HumanoidCharacterProfile { Name = "Original" });
        var updated = (HumanoidCharacterProfile) manager.Preferences.SelectedCharacter;
        Assert.That(updated.Appearance.Markings, Is.Empty);
        Assert.That(updated.FlavorText, Is.EqualTo("Unsaved draft"));
    }

    private static void ApplyServerReply(ClientPreferencesManager manager, int slot, ICharacterProfile profile)
    {
        typeof(ClientPreferencesManager).GetMethod("HandleServerCharacterUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(manager, new object[] { new MsgUpdateCharacter { Slot = slot, Profile = profile } });
    }

    public class NetworkProxy : DispatchProxy
    {
        public int Messages;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "ClientSendMessage")
                Messages++;
            return method.ReturnType != typeof(void) && method.ReturnType.IsValueType
                ? Activator.CreateInstance(method.ReturnType)
                : null;
        }
    }
}
