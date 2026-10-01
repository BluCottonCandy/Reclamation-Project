// #Misfits Add - Server-side hallucinations engine.
// Centralizes the ticker, global local-chat history, fake-chat replay, audio,
// flavor popups, optional Genetic damage, and client-targeted phantom spawning.
//
// Performance:
//  * Only entities with HallucinationsComponent are iterated each tick.
//  * Per-entity NextEvent gates how often events fire.
//  * Chat history is a fixed-size ring buffer subscribed once to EntitySpokeEvent.
//  * Owners (Stealth Boy, Paracusia) push their contributed intensity via
//    SetSourceIntensity; the system derives the effective max and adds/removes
//    HallucinationsComponent as needed so untouched players never get queried.
using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Shared._Misfits.Hallucinations;
using Content.Shared._Misfits.StealthBoy;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Hallucinations;

/// <summary>
/// Drives all hallucination effects shared by Stealth Boy radiation, the Paracusia
/// perk, and any future drug/wound source.
/// </summary>
public sealed class HallucinationsSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly BlurryVisionSystem _blurry = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TransformSystem _xform = default!;

    /// <summary>Max number of recent local-chat lines kept for echo hallucinations.</summary>
    private const int ChatHistorySize = 50;

    /// <summary>Rolling buffer of the most recent local-chat lines, station-wide.</summary>
    private readonly Queue<(string Speaker, string Message)> _chatHistory = new(ChatHistorySize);

    /// <summary>
    /// Per-entity, per-dataset shuffled-deck rotation queues. We deal indices off the
    /// top of each deck and reshuffle when empty, guaranteeing every line in a dataset
    /// plays once before any repeats. Prevents spammy duplicates the user complained about.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<string, Queue<int>>> _rotationDecks = new();

    /// <summary>Most recent echo-buffer index used per entity, so we don't echo the same line back-to-back.</summary>
    private readonly Dictionary<EntityUid, int> _lastEchoIndex = new();

    public override void Initialize()
    {
        base.Initialize();
        // Hook every IC speech event into the global ring buffer for echo hallucinations.
        SubscribeLocalEvent<EntitySpokeEvent>(OnEntitySpoke);
        // Free per-entity rotation state when the comp goes away so dicts stay bounded.
        SubscribeLocalEvent<HallucinationsComponent, ComponentShutdown>(OnHallucinationsShutdown);
        // Contribute breakdown blur into the standard BlurryVision pipeline.
        SubscribeLocalEvent<HallucinationsComponent, GetBlurEvent>(OnGetBlur);
    }

    private void OnGetBlur(Entity<HallucinationsComponent> ent, ref GetBlurEvent args)
    {
        if (ent.Comp.Breakdown)
            args.Blur += ent.Comp.BreakdownBlur;
    }

    private void OnHallucinationsShutdown(Entity<HallucinationsComponent> ent, ref ComponentShutdown args)
    {
        // Make sure the spawned distortion overlay doesn't outlive the component.
        if (ent.Comp.BreakdownDistortion is { } dist && !TerminatingOrDeleted(dist))
            QueueDel(dist);
        ent.Comp.BreakdownDistortion = null;
        _rotationDecks.Remove(ent.Owner);
        _lastEchoIndex.Remove(ent.Owner);
    }

    private void OnEntitySpoke(EntitySpokeEvent ev)
    {
        // Skip whispers/radio so the buffer stays focused on plain local speech.
        if (ev.IsWhisper || ev.Channel != null)
            return;

        var name = Name(ev.Source);
        if (string.IsNullOrEmpty(ev.Message))
            return;

        if (_chatHistory.Count >= ChatHistorySize)
            _chatHistory.Dequeue();
        _chatHistory.Enqueue((name, ev.Message));
    }

    /// <summary>
    /// Owners (Stealth Boy / Paracusia / etc.) call this with their contributed intensity.
    /// The system derives the effective max from all known sources and removes the
    /// component when no source contributes anything.
    /// </summary>
    public void RefreshIntensity(EntityUid uid)
    {
        var max = 0;

        if (TryComp<StealthBoyExposureComponent>(uid, out var stealth))
            max = Math.Max(max, stealth.CurrentTier);

        if (TryComp<MisfitsParacusiaComponent>(uid, out var paracusia))
            max = Math.Max(max, paracusia.CurrentLevel);

        // Breakdown trigger: Paracusia user is *currently* cloaked by a Stealth Boy.
        // Their already-frayed perception slams into the device's exposure for a
        // full chaotic episode — glitched text, screams, rapid-fire events.
        var breakdown = paracusia != null
            && paracusia.CurrentLevel > 0
            && HasComp<StealthBoyActiveComponent>(uid);

        if (breakdown)
            max = Math.Max(max, 5);

        if (max <= 0)
        {
            // No active source -> remove the comp so we stop iterating this entity.
            RemCompDeferred<HallucinationsComponent>(uid);
            return;
        }

        var hall = EnsureComp<HallucinationsComponent>(uid);
        hall.Intensity = max;
        hall.Breakdown = breakdown;

        // --- Visual breakdown effects: spawn an invisible SingularityDistortion
        // entity parented to the player for the SingularityToy spacetime warp,
        // and contribute heavy blur via GetBlurEvent for the crit-state vignette.
        // Both are dropped once breakdown ends so vision returns to normal.
        if (breakdown)
        {
            if (hall.BreakdownDistortion == null || TerminatingOrDeleted(hall.BreakdownDistortion.Value))
            {
                var coords = Transform(uid).Coordinates;
                var distort = Spawn(hall.BreakdownDistortionProto, coords);
                _xform.SetParent(distort, uid);
                hall.BreakdownDistortion = distort;
            }
        }
        else if (hall.BreakdownDistortion != null)
        {
            if (!TerminatingOrDeleted(hall.BreakdownDistortion.Value))
                QueueDel(hall.BreakdownDistortion.Value);
            hall.BreakdownDistortion = null;
        }

        // Re-run the standard blur pipeline so our GetBlurEvent contribution is
        // applied (or removed) whenever breakdown toggles.
        _blurry.UpdateBlurMagnitude(uid);

        // Pick dataset themes by source. Stealth Boy wins over Paracusia when both are
        // present (its symptoms are more acute and the player should know which is in play).
        if (stealth != null && stealth.CurrentTier > 0)
        {
            hall.FlavorDataset = "StealthBoyHallucinations";
            hall.WhisperDataset = "StealthBoyWhispers";
        }
        else if (paracusia != null && paracusia.CurrentLevel > 0)
        {
            hall.FlavorDataset = "ParacusiaHallucinations";
            hall.WhisperDataset = "ParacusiaWhispers";
        }

        // If the dataset changed, drop the old shuffle deck so we don't deal stale indices
        // into a smaller list and crash on out-of-range.
        if (_rotationDecks.TryGetValue(uid, out var perDataset))
            perDataset.Clear();

        // Disable damage entirely if Stealth Boy isn't a contributor (so Paracusia alone is harmless).
        // Breakdown lets Paracusia + Stealth Boy still do damage since stealth is active.
        if (stealth == null || stealth.CurrentTier < 4)
            hall.BurnoutDamage = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<HallucinationsComponent>();
        while (query.MoveNext(out var uid, out var hall))
        {
            var tier = hall.Intensity;
            if (tier <= 0)
                continue;

            if (now < hall.NextEvent)
                continue;

            // Breakdown collapses the interval window so events fire constantly.
            float min, max;
            if (hall.Breakdown)
            {
                min = 1.5f;
                max = 4f;
            }
            else
            {
                min = Math.Max(1f, hall.MinIntervalSeconds / tier);
                max = Math.Max(min + 1f, hall.MaxIntervalSeconds / tier);
            }
            hall.NextEvent = now + TimeSpan.FromSeconds(_random.NextFloat(min, max));

            if (hall.Breakdown)
                FireBreakdown(uid, hall);
            else
                FireHallucination(uid, hall, tier);

            if (tier >= 4 && hall.BurnoutDamage != null)
                _damageable.TryChangeDamage(uid, hall.BurnoutDamage, ignoreResistances: true, origin: uid);
        }
    }

    private void FireHallucination(EntityUid user, HallucinationsComponent hall, int tier)
    {
        var roll = _random.NextFloat();

        // Tier 3+: phantom entity (if configured) or whispers / fake chat / echo of real chat.
        if (tier >= 3)
        {
            if (roll < 0.15f && hall.PhantomPrototypes.Count > 0)
            {
                FirePhantom(user, hall);
                return;
            }
            if (roll < 0.4f)
            {
                SendWhisper(user, hall);
                return;
            }
            if (roll < 0.55f)
            {
                FireFakeChat(user, hall);
                return;
            }
            if (roll < 0.7f && _chatHistory.Count > 0)
            {
                FireChatEcho(user);
                return;
            }
        }
        // Tier 2+: paranoid audio (footsteps / gunshot) + flavor popup.
        else if (tier >= 2)
        {
            if (roll < 0.45f)
            {
                var sound = _random.Prob(0.5f) ? hall.FootstepSound : hall.GunshotSound;
                _audio.PlayEntity(sound, user, user);
                SendFlavor(user, hall);
                return;
            }
            if (roll < 0.65f)
            {
                FireFakeChat(user, hall);
                return;
            }
        }

        // Default: pure flavor popup.
        SendFlavor(user, hall);
    }

    private void SendFlavor(EntityUid user, HallucinationsComponent hall)
    {
        var line = PickRotated(user, hall.FlavorDataset);
        if (line == null)
            return;
        _popup.PopupEntity(line, user, user, PopupType.SmallCaution);
    }

    private void SendWhisper(EntityUid user, HallucinationsComponent hall)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        var msg = PickRotated(user, hall.WhisperDataset);
        if (msg == null)
            return;

        var wrapped = $"[font size=10][italic]{msg}[/italic][/font]";
        _chat.ChatMessageToOne(ChatChannel.Emotes, msg, wrapped, user, false, actor.PlayerSession.Channel);

        if (hall.WhisperSound != null)
            _audio.PlayEntity(hall.WhisperSound, user, user);
    }

    /// <summary>
    /// Fabricate a "local chat" line from a fake nearby NPC. Routed through the regular
    /// chat manager so it appears in the player's chat panel using the standard format.
    /// </summary>
    private void FireFakeChat(EntityUid user, HallucinationsComponent hall)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        var msg = PickRotated(user, hall.FlavorDataset);
        if (msg == null)
            return;

        var speaker = PickFakeSpeakerName(user, hall);
        var wrapped = $"[bold]{speaker}[/bold] says, \"{msg}\"";
        _chat.ChatMessageToOne(ChatChannel.Local, msg, wrapped, user, false, actor.PlayerSession.Channel);
    }

    /// <summary>
    /// Echo a real recent local-chat line back to the affected player only,
    /// optionally re-attributed to a fabricated speaker for extra dissonance.
    /// Skips if the only entry available was the last one we echoed.
    /// </summary>
    private void FireChatEcho(EntityUid user)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;
        if (_chatHistory.Count == 0)
            return;

        // Pick a different index from last time when possible.
        var lastIdx = _lastEchoIndex.TryGetValue(user, out var li) ? li : -1;
        int idx;
        if (_chatHistory.Count == 1)
            idx = 0;
        else
        {
            var attempts = 0;
            do { idx = _random.Next(_chatHistory.Count); }
            while (idx == lastIdx && ++attempts < 4);
        }
        _lastEchoIndex[user] = idx;

        var entry = _chatHistory.ElementAt(idx);
        var speaker = _random.Prob(0.3f) ? PickFakeSpeakerName(user, null) : entry.Speaker;
        var wrapped = $"[bold]{speaker}[/bold] says, \"{entry.Message}\"";
        _chat.ChatMessageToOne(ChatChannel.Local, entry.Message, wrapped, user, false, actor.PlayerSession.Channel);
    }

    private string PickFakeSpeakerName(EntityUid user, HallucinationsComponent? hall)
    {
        var datasetId = hall?.FakeNameDataset.Id ?? "names_first";
        if (!_proto.TryIndex<Content.Shared.Dataset.DatasetPrototype>(datasetId, out var dataset)
            || dataset.Values.Count == 0)
            return "Unknown";
        // Plain names list — picks aren't FTL keys, so just random-pick. Repeats here are
        // far less noticeable than repeated speech lines.
        return dataset.Values[_random.Next(dataset.Values.Count)];
    }

    /// <summary>
    /// Pull the next index off this entity's shuffled deck for the given dataset and
    /// return the localized line. Reshuffles when the deck runs out so every line
    /// plays once before any repeats. Returns null if the dataset is missing or empty.
    /// </summary>
    private string? PickRotated(EntityUid user, string datasetId)
    {
        if (!_proto.TryIndex<Content.Shared.Dataset.LocalizedDatasetPrototype>(datasetId, out var dataset)
            || dataset.Values.Count == 0)
            return null;

        if (!_rotationDecks.TryGetValue(user, out var perDataset))
        {
            perDataset = new Dictionary<string, Queue<int>>();
            _rotationDecks[user] = perDataset;
        }

        if (!perDataset.TryGetValue(datasetId, out var deck) || deck.Count == 0)
        {
            deck = ShuffleDeck(dataset.Values.Count);
            perDataset[datasetId] = deck;
        }

        var idx = deck.Dequeue();
        return Loc.GetString(dataset.Values[idx]);
    }

    private Queue<int> ShuffleDeck(int count)
    {
        var arr = new int[count];
        for (var i = 0; i < count; i++) arr[i] = i;
        // Fisher-Yates.
        for (var i = count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return new Queue<int>(arr);
    }

    /// <summary>
    /// Send a one-shot phantom-spawn event to the affected player only. Client
    /// spawns the entity locally and despawns after Lifetime seconds.
    /// </summary>
    private void FirePhantom(EntityUid user, HallucinationsComponent hall)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;
        if (hall.PhantomPrototypes.Count == 0)
            return;

        var proto = _random.Pick(hall.PhantomPrototypes);

        // Pick a random nearby tile (1..PhantomMaxRange away in either axis).
        var userXform = Transform(user);
        var dx = _random.Next(-hall.PhantomMaxRange, hall.PhantomMaxRange + 1);
        var dy = _random.Next(-hall.PhantomMaxRange, hall.PhantomMaxRange + 1);
        if (dx == 0 && dy == 0)
            dx = 1; // ensure offset

        var coords = userXform.Coordinates.Offset(new System.Numerics.Vector2(dx, dy));
        var ev = new SpawnPhantomHallucinationEvent(
            proto.Id,
            EntityManager.GetNetCoordinates(coords),
            hall.PhantomLifetime);
        RaiseNetworkEvent(ev, actor.PlayerSession.Channel);
    }

    /// <summary>
    /// Breakdown effect (intensity 5, Paracusia + active Stealth Boy). Picks a random
    /// effect kind, mangles any text it produces with glitch chars + uppercase + scattered
    /// extra punctuation, and stacks a scream sound on top so the player feels overwhelmed.
    /// </summary>
    private void FireBreakdown(EntityUid user, HallucinationsComponent hall)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;
        var channel = actor.PlayerSession.Channel;
        var roll = _random.NextFloat();

        // Always layer the scream so the breakdown is unmistakable.
        _audio.PlayEntity(hall.ScreamSound, user, user);

        // Roll one of: glitched flavor popup, glitched whisper, glitched fake chat, glitched echo, phantom.
        if (roll < 0.20f && hall.PhantomPrototypes.Count > 0)
        {
            FirePhantom(user, hall);
            return;
        }

        if (roll < 0.45f)
        {
            var line = PickRotated(user, hall.WhisperDataset);
            if (line == null) return;
            var glitched = Glitch(line);
            var wrapped = $"[font size=11][bold][italic]{glitched}[/italic][/bold][/font]";
            _chat.ChatMessageToOne(ChatChannel.Emotes, glitched, wrapped, user, false, channel);
            return;
        }

        if (roll < 0.70f)
        {
            // Fake chat from a glitched speaker name as well.
            var line = PickRotated(user, hall.FlavorDataset);
            if (line == null) return;
            var speaker = Glitch(PickFakeSpeakerName(user, hall));
            var glitched = Glitch(line);
            var wrapped = $"[bold]{speaker}[/bold] [color=#aa3333]screams[/color], \"{glitched}\"";
            _chat.ChatMessageToOne(ChatChannel.Local, glitched, wrapped, user, false, channel);
            return;
        }

        if (roll < 0.85f && _chatHistory.Count > 0)
        {
            var entry = _chatHistory.ElementAt(_random.Next(_chatHistory.Count));
            var glitched = Glitch(entry.Message);
            var speaker = Glitch(entry.Speaker);
            var wrapped = $"[bold]{speaker}[/bold] [color=#aa3333]screams[/color], \"{glitched}\"";
            _chat.ChatMessageToOne(ChatChannel.Local, glitched, wrapped, user, false, channel);
            return;
        }

        // Default: glitched flavor popup.
        var flavor = PickRotated(user, hall.FlavorDataset);
        if (flavor != null)
            _popup.PopupEntity(Glitch(flavor), user, user, PopupType.LargeCaution);
    }

    /// <summary>
    /// Mangle a string for breakdown effect: random uppercase, scattered Unicode combining
    /// marks ("zalgo"), occasional letter duplication, scattered exclamation punctuation.
    /// Cheap per-character pass; no allocations beyond the StringBuilder.
    /// </summary>
    private string Glitch(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // A few combining diacritical marks. Stacking 1-3 per char gives the corrupted look.
        ReadOnlySpan<char> zalgo = stackalloc char[]
        {
            '\u0300', '\u0301', '\u0302', '\u0303', '\u0304', '\u0305', '\u0306',
            '\u0307', '\u0308', '\u0309', '\u030A', '\u030B', '\u030C', '\u0316',
            '\u0317', '\u0318', '\u0319', '\u031C', '\u031D', '\u031E', '\u033F',
        };

        var sb = new System.Text.StringBuilder(input.Length * 3);
        foreach (var rawCh in input)
        {
            var ch = rawCh;
            // 60% uppercase scream.
            if (char.IsLetter(ch) && _random.Prob(0.6f))
                ch = char.ToUpperInvariant(ch);
            sb.Append(ch);
            // 10% double up the letter ("Hheelloo").
            if (char.IsLetter(ch) && _random.Prob(0.1f))
                sb.Append(ch);
            // 1-3 zalgo marks per character.
            var marks = _random.Next(1, 4);
            for (var i = 0; i < marks; i++)
                sb.Append(zalgo[_random.Next(zalgo.Length)]);
        }

        // Trailing scream punctuation.
        var bangs = _random.Next(1, 4);
        for (var i = 0; i < bangs; i++)
            sb.Append('!');
        return sb.ToString();
    }
}
