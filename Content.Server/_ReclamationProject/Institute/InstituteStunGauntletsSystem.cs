using Content.Server.PowerCell;
using Content.Shared._Goobstation.Clothing.Components;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Timing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._ReclamationProject.Institute;

/// <summary>
/// Ninja-style touch stun powered by the worn Institute MOD control's cell.
/// Sealing the gauntlets arms them; retracting or unsealing them disarms them.
/// </summary>
public sealed class InstituteStunGauntletsSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private const string DelayId = "institute_gauntlet_stun";
    private static readonly DamageSpecifier ShockDamage = new()
    {
        DamageDict = new() { { "Shock", 5 } }
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InventoryComponent, BeforeInteractHandEvent>(OnInteractHand);
    }

    private void OnInteractHand(Entity<InventoryComponent> user, ref BeforeInteractHandEvent args)
    {
        if (args.Handled || args.Target == user.Owner || _combat.IsInCombatMode(user.Owner) ||
            !TryComp<HandsComponent>(user.Owner, out var hands) || hands.ActiveHandEntity != null ||
            !HasComp<StaminaComponent>(args.Target) || !_interaction.InRangeUnobstructed(user.Owner, args.Target))
            return;

        if (!_inventory.TryGetSlotEntity(user.Owner, "gloves", out var gloves) ||
            !_tags.HasTag(gloves.Value, "InstituteStunGauntlets") ||
            !TryComp<SealableClothingComponent>(gloves.Value, out var seal) || !seal.IsSealed ||
            !_inventory.TryGetSlotEntity(user.Owner, "back", out var control) ||
            !_tags.HasTag(control.Value, "InstituteMODControl"))
            return;

        var delay = EnsureComp<UseDelayComponent>(gloves.Value);
        if (_useDelay.IsDelayed((gloves.Value, delay), id: DelayId))
        {
            args.Handled = true;
            return;
        }

        args.Handled = true;
        if (!_powerCell.TryUseCharge(control.Value, 36))
        {
            _popup.PopupEntity(Loc.GetString("institute-gauntlets-no-power"), user.Owner, user.Owner);
            return;
        }

        _audio.PlayPvs(new SoundCollectionSpecifier("sparks"), args.Target);
        _damage.TryChangeDamage(args.Target, ShockDamage, false, true, null, origin: user.Owner);
        _stun.TryParalyze(args.Target, TimeSpan.FromSeconds(5), refresh: false);
        _useDelay.SetLength((gloves.Value, delay), TimeSpan.FromSeconds(2), id: DelayId);
        _useDelay.TryResetDelay((gloves.Value, delay), id: DelayId);
    }
}
