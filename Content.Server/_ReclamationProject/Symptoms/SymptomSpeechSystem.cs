using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Speech.Components;
using Content.Shared._ReclamationProject.Symptoms;
using Content.Shared.ActionBlocker;
using Content.Shared.Drunk;
using Content.Shared.Chat;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Speech;
using Content.Shared.Speech.Muting;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._ReclamationProject.Symptoms;

public sealed class SymptomSpeechSystem : EntitySystem
{
    public static readonly TimeSpan GlobalCooldown = TimeSpan.FromMinutes(2);
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly BloodstreamSystem _blood = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    private readonly Dictionary<string, string> _sourceMessages = new(StringComparer.Ordinal);
    private bool _mapped;
    private TimeSpan _nextScan;

    /// <summary>True includes a known symptom suppressed by its cooldown, preventing duplicate popups.</summary>
    public bool TryReplaceNotification(EntityUid user, string message)
    {
        EnsureMapping();
        return _sourceMessages.TryGetValue(FormattedMessage.RemoveMarkup(message), out var symptom) &&
            TrySpeak(user, symptom);
    }

    public bool TryMapNotification(string message, out string? symptom)
    {
        EnsureMapping();
        return _sourceMessages.TryGetValue(FormattedMessage.RemoveMarkup(message), out symptom);
    }

    private void EnsureMapping()
    {
        if (_mapped)
            return;
        foreach (var prototype in _prototypes.EnumeratePrototypes<SymptomSpeechPrototype>())
            foreach (var source in prototype.SourceMessages)
                if (Loc.TryGetString(source, out var text))
                    _sourceMessages[FormattedMessage.RemoveMarkup(text)] = prototype.ID;
        _mapped = true;
    }

    public bool CanSpeakSymptoms(EntityUid user) =>
        HasComp<OutspokenComponent>(user) && TryComp<ActorComponent>(user, out var actor) && actor.PlayerSession.AttachedEntity == user &&
        actor.PlayerSession.Status == SessionStatus.InGame && !HasComp<GhostComponent>(user) &&
        TryComp<SpeechComponent>(user, out var speech) && speech.Enabled && _mob.IsAlive(user) &&
        // CanSpeak raises a real SpeakAttemptEvent, whose mute handler displays a popup.
        // Background symptom polling must not ask mute characters to attempt speech.
        !HasComp<MutedComponent>(user) && _blocker.CanSpeak(user);

    public bool TrySpeak(EntityUid user, string symptom)
    {
        // Keep the original private notification when speech is impossible; never bypass muteness or gags.
        if (!CanSpeakSymptoms(user) || !_prototypes.TryIndex<SymptomSpeechPrototype>(symptom, out var prototype) ||
            prototype.SpeechMessages.Count == 0)
            return false;
        var state = EnsureComp<SymptomSpeechComponent>(user);
        if (_timing.CurTime < state.NextSpeechAt || state.NextBySymptom.TryGetValue(symptom, out var next) && _timing.CurTime < next)
            return true;

        state.LastLineBySymptom.TryGetValue(symptom, out var previous);
        var candidates = prototype.SpeechMessages.Where(line => line != previous || prototype.SpeechMessages.Count == 1).ToArray();
        var key = _random.Pick(candidates);
        if (!Loc.TryGetString(key, out var speech))
            return false;
        state.NextSpeechAt = _timing.CurTime + GlobalCooldown;
        state.NextBySymptom[symptom] = _timing.CurTime + TimeSpan.FromSeconds(
            prototype.Cooldown + _random.NextFloat() * prototype.CooldownVariation);
        state.LastLineBySymptom[symptom] = key;
        _chat.TrySendInGameICMessage(user, speech, InGameICChatType.Speak, false, checkRadioPrefix: false);
        return true;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextScan)
            return;
        _nextScan = _timing.CurTime + TimeSpan.FromSeconds(5);
        // One slow scan of players, never a per-tick scan of all bloodstreams or NPCs.
        var players = EntityQueryEnumerator<OutspokenComponent, ActorComponent>();
        while (players.MoveNext(out var uid, out _, out _))
        {
            if (Paused(uid) || !CanSpeakSymptoms(uid))
                continue;
            if (TryComp<BloodstreamComponent>(uid, out var blood) &&
                _blood.GetBloodLevelPercentage(uid, blood) < blood.BloodlossThreshold)
                TrySpeak(uid, "SymptomBloodLoss");
            // Low blood and old-injury dizziness use Drunk without slurring. They must not cause drunken chatter.
            if (HasComp<DrunkComponent>(uid) && HasComp<SlurredAccentComponent>(uid))
                TrySpeak(uid, "SymptomDrunk");
        }
    }
}
