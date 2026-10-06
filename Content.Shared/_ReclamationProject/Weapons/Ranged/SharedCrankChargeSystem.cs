// #Reclamation Project Add - Crank-charged guns (Minutemen laser musket).
// Alt-click (or right-click > Crank) winds the gun once per do-after. The server hitscan code
// (GunSystem.cs) adds GetBonusDamage() to the shot, and GunShotEvent - raised after Shoot() -
// resets the count. Paying the cell charge is server-only, see the server CrankChargeSystem.
using Content.Shared._Misfits.Weapons;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._ReclamationProject.Weapons.Ranged;

public abstract partial class SharedCrankChargeSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] protected SharedPopupSystem Popup = default!;

    protected const string MagazineSlot = "gun_magazine";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrankChargeComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<CrankChargeComponent, CrankChargeDoAfterEvent>(OnCrankDoAfter);
        SubscribeLocalEvent<CrankChargeComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<CrankChargeComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>
    /// Bonus damage for the current crank count, or null when the gun isn't cranked.
    /// </summary>
    public DamageSpecifier? GetBonusDamage(CrankChargeComponent comp)
    {
        if (comp.Charges <= 0 || comp.BonusByCharge.Count == 0)
            return null;

        var index = Math.Min(comp.Charges, comp.BonusByCharge.Count) - 1;
        var bonus = new DamageSpecifier();
        bonus.DamageDict[comp.BonusDamageType] = comp.BonusByCharge[index];
        return bonus;
    }

    /// <summary>
    /// Server only: can the cell pay for one more crank and still fire the shot?
    /// </summary>
    protected virtual bool CanAffordCrank(Entity<CrankChargeComponent> ent, EntityUid user)
    {
        return true;
    }

    /// <summary>
    /// Takes one crank's worth of charge from the cell. Only the server can, so the client
    /// returns false and waits for the networked crank count instead.
    /// </summary>
    protected abstract bool TryPayCrankCost(Entity<CrankChargeComponent> ent, EntityUid user);

    private void OnGetVerbs(Entity<CrankChargeComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (args.Hands == null || !args.CanAccess || !args.CanInteract)
            return;

        // Only while held: you crank it with the gun in your hands.
        if (!_hands.IsHolding((args.User, args.Hands), ent))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("crank-charge-verb", ("charges", ent.Comp.Charges), ("max", ent.Comp.MaxCharges)),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/rotate_cw.svg.192dpi.png")),
            Act = () => TryStartCrank(ent, user),
            // Above the cell's eject verb (priority 0), so alt-click cranks and Eject stays in the menu.
            Priority = 5,
        });
    }

    private void TryStartCrank(Entity<CrankChargeComponent> ent, EntityUid user)
    {
        if (ent.Comp.Charges >= ent.Comp.MaxCharges)
        {
            Popup.PopupClient(Loc.GetString("crank-charge-full"), ent, user);
            return;
        }

        if (_itemSlots.GetItemOrNull(ent, MagazineSlot) == null)
        {
            Popup.PopupClient(Loc.GetString("crank-charge-no-cell"), ent, user);
            return;
        }

        if (!CanAffordCrank(ent, user))
            return;

        var doAfter = new DoAfterArgs(EntityManager, user, ent.Comp.CrankDelay, new CrankChargeDoAfterEvent(), ent, used: ent)
        {
            NeedHand = true,
            BreakOnHandChange = true,
            BreakOnDropItem = true,
            BreakOnMove = false,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _audio.PlayPredicted(ent.Comp.CrankSound, ent, user);
    }

    private void OnCrankDoAfter(Entity<CrankChargeComponent> ent, ref CrankChargeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (ent.Comp.Charges >= ent.Comp.MaxCharges)
            return;

        if (!TryPayCrankCost(ent, args.User))
            return;

        ent.Comp.Charges++;
        Dirty(ent);
        Popup.PopupEntity(Loc.GetString("crank-charge-popup", ("charges", ent.Comp.Charges), ("max", ent.Comp.MaxCharges)), ent, args.User);
    }

    private void OnGunShot(Entity<CrankChargeComponent> ent, ref GunShotEvent args)
    {
        if (ent.Comp.Charges == 0)
            return;

        ent.Comp.Charges = 0;
        Dirty(ent);
    }

    private void OnExamined(Entity<CrankChargeComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("crank-charge-examine",
            ("charges", ent.Comp.Charges),
            ("max", ent.Comp.MaxCharges),
            ("damage", GetNextShotDamage(ent))));
    }

    /// <summary>
    /// Total damage of the next shot: the gun's beam, any flat GunDamageBonus, and the crank bonus.
    /// </summary>
    private FixedPoint2 GetNextShotDamage(Entity<CrankChargeComponent> ent)
    {
        var total = FixedPoint2.Zero;

        if (TryComp<GunDamageBonusComponent>(ent, out var gunBonus))
        {
            if (gunBonus.HitscanProtoOverride != null &&
                _proto.TryIndex<HitscanPrototype>(gunBonus.HitscanProtoOverride, out var hitscan) &&
                hitscan.Damage != null)
            {
                total += hitscan.Damage.GetTotal();
            }

            if (gunBonus.BonusDamage != null)
                total += gunBonus.BonusDamage.GetTotal();
        }

        if (GetBonusDamage(ent.Comp) is { } crankBonus)
            total += crankBonus.GetTotal();

        return total;
    }
}
