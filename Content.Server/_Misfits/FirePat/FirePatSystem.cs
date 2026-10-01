// #Misfits Add - Server-side fire pat system.
// Inspired by fire-patting concepts from stalker-14-EN; built independently for N14.
// Allows a player to pat out flames on a burning entity by interacting with empty hands,
// as an alternative to the burn-victim rolling on the ground.
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Misfits.FirePat;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;

namespace Content.Server._Misfits.FirePat;

/// <summary>
/// Fire-patting mechanic:
/// - Use empty hands on a burning entity (must be a mob with <see cref="FlammableComponent"/>).
/// - 2.5 second do-after that cancels if the patter moves or takes damage.
/// - On success: reduces fire stacks by <see cref="PatAmount"/>. Repeat to fully extinguish.
/// - Cannot self-pat (use the existing stop-drop-and-roll for that).
/// </summary>
public sealed class FirePatSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    // How many fire stacks each successful pat removes.
    private const float PatAmount = 3f;

    // Sound: a soft slapping sound to convey patting out flames.
    private static readonly SoundPathSpecifier PatSound =
        new("/Audio/Weapons/firepunch1.ogg");

    public override void Initialize()
    {
        base.Initialize();
        // Subscribe on FlammableComponent to intercept empty-hand interactions with burning targets.
        SubscribeLocalEvent<FlammableComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<FirePatDoAfterEvent>(OnFirePatDoAfter);
    }

    private void OnInteractHand(EntityUid target, FlammableComponent flammable, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        // Target must actually be on fire.
        if (!flammable.OnFire)
            return;

        // No self-patting — use stop-drop-and-roll for that.
        if (args.User == target)
            return;

        args.Handled = true;

        _popup.PopupEntity(
            Loc.GetString("fire-pat-start-performer", ("target", target)),
            args.User, args.User, PopupType.Small);
        _popup.PopupEntity(
            Loc.GetString("fire-pat-start-target", ("user", args.User)),
            target, target, PopupType.Small);

        var doAfterArgs = new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(2.5f), new FirePatDoAfterEvent(), args.User, target)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        };
        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnFirePatDoAfter(FirePatDoAfterEvent args)
    {
        if (args.Cancelled || args.Target == null)
            return;

        var target = args.Target.Value;

        // Re-check: still on fire?
        if (!TryComp<FlammableComponent>(target, out var flammable) || !flammable.OnFire)
            return;

        // Reduce fire stacks — negative value decreases them. FlammableSystem clamps to 0 and extinguishes automatically.
        _flammable.AdjustFireStacks(target, -PatAmount, flammable);

        _popup.PopupEntity(
            Loc.GetString("fire-pat-success-performer", ("target", target)),
            args.User, args.User, PopupType.Small);
        _popup.PopupEntity(
            Loc.GetString("fire-pat-success-target", ("user", args.User)),
            target, target, PopupType.Small);

        _audio.PlayPvs(PatSound, target, AudioParams.Default.WithVolume(-3f).WithVariation(0.1f));
    }
}
