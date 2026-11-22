using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._ReclamationProject.Magic;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - charge, then aim. Pressing a targeted spell that isn't charged asks the server to charge
/// it (you can keep walking). When it is charged the server says so and this drops you straight into aiming;
/// the next click fires it. Instant casters (chantless) aim right away. Also runs the reach overlay.
/// </summary>
public sealed class MagicChargeSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<MagicSpellChargedEvent>(OnCharged);
        _overlays.AddOverlay(new MagicAimOverlay(EntityManager, _player, _eye, _input, _proto, _transform, _ui));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<MagicAimOverlay>();
    }

    /// <summary>Called by the hotbar before it starts aiming. True = we asked for a charge instead.</summary>
    public bool TryStartCharge(EntityUid action)
    {
        if (_player.LocalEntity is not { } me
            || !HasComp<MagicSpellActionComponent>(action)
            || !TryComp<MagicCasterComponent>(me, out var caster))
            return false;

        // Already charged and held, or an instant caster: aim now.
        if (caster.HeldSpell == action || caster.MagicChantless || caster.MagicMaxMana >= caster.MagicMaxManaCap)
            return false;

        RaiseNetworkEvent(new MagicStartChargeEvent(GetNetEntity(action)));
        return true;
    }

    private void OnCharged(MagicSpellChargedEvent ev)
    {
        if (!TryGetEntity(ev.Action, out var action) || action is not { } uid)
            return;
        _ui.GetUIController<ActionUIController>().MagicStartTargeting(uid);
    }
}
