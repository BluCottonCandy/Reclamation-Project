using Content.Shared._Misfits.Chat.Components;
using Content.Server.Chat.Systems;
using Content.Shared.Speech;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;
using Content.Shared.Hands.EntitySystems;

namespace Content.Server._Misfits.Chat.Systems;

/// <summary>
/// When a megaphone item is wielded, directly overwrites the holder's
/// <see cref="SpeechComponent.SpeechVerb"/> with the megaphone speech verb so
/// that ChatSystem.GetSpeechVerb() picks up the larger font size.
/// Also uppercases outgoing messages via TransformSpeechEvent (SS13-style behaviour).
/// The previous verb is restored when the item is unwielded or leaves the hand.
/// </summary>
public sealed class MegaphoneSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MegaphoneComponent, ItemWieldedEvent>(OnWielded);
        SubscribeLocalEvent<MegaphoneComponent, ItemUnwieldedEvent>(OnUnwielded);
        // TransformSpeechEvent is raised via RaiseLocalEvent(ev) — broadcast — so this fires.
        SubscribeLocalEvent<TransformSpeechEvent>(OnTransformSpeech);
    }

    private void OnWielded(EntityUid uid, MegaphoneComponent comp, ref ItemWieldedEvent args)
    {
        // The item is in a hand-slot container whose Owner is the player entity.
        if (!_container.TryGetContainingContainer(uid, out var container))
            return;

        var holder = container.Owner;
        if (!TryComp<SpeechComponent>(holder, out var speech))
            return;

        // Store current verb so we can undo the change on unwield.
        comp.PreviousSpeechVerb = speech.SpeechVerb;
        speech.SpeechVerb = comp.SpeechVerbOverride;
    }

    private void OnUnwielded(EntityUid uid, MegaphoneComponent comp, ItemUnwieldedEvent args)
    {
        if (comp.PreviousSpeechVerb == null)
            return;

        // Prefer the User provided by the event; fall back to container owner (covers force-drops).
        EntityUid? holder = args.User;
        if (holder == null && _container.TryGetContainingContainer(uid, out var container))
            holder = container.Owner;

        if (holder != null && TryComp<SpeechComponent>(holder.Value, out var speech))
        {
            // Only restore if nothing else changed it in the meantime.
            if (speech.SpeechVerb == comp.SpeechVerbOverride)
                speech.SpeechVerb = comp.PreviousSpeechVerb.Value;
        }

        comp.PreviousSpeechVerb = null;
    }

    /// <summary>
    /// Uppercases the outgoing message when the sender is actively wielding a megaphone.
    /// Mirrors SS13 megaphone behavior — obnoxious but not egregious.
    /// TransformSpeechEvent is raised broadcast so this subscription fires correctly.
    /// </summary>
    private void OnTransformSpeech(TransformSpeechEvent args)
    {
        foreach (var held in _hands.EnumerateHeld(args.Sender))
        {
            if (!TryComp<MegaphoneComponent>(held, out _) ||
                !TryComp<WieldableComponent>(held, out var wieldable) ||
                !wieldable.Wielded)
                continue;

            args.Message = args.Message.ToUpperInvariant();
            return;
        }
    }
}
