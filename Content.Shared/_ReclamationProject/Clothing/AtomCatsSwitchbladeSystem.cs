using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.MagicMirror;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Network;

namespace Content.Shared._ReclamationProject.Clothing;

public sealed class AtomCatsSwitchbladeSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedItemSystem _items = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AtomCatsSwitchbladeComponent, BeforeUseInHandEvent>(OnCycle);
        SubscribeLocalEvent<AtomCatsSwitchbladeComponent, ActivatableUIOpenAttemptEvent>(OnOpenBarberMenu);
    }

    private void OnCycle(Entity<AtomCatsSwitchbladeComponent> ent, ref BeforeUseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_net.IsClient)
            return;

        string state;
        if (TryComp<MagicMirrorComponent>(ent, out var comb))
        {
            _doAfter.Cancel(comb.DoAfter);
            _ui.CloseUi(ent.Owner, MagicMirrorUiKey.Key);
            EntityManager.RemoveComponents(ent, ent.Comp.Comb);
            state = "folded";
        }
        else if (HasComp<MeleeWeaponComponent>(ent))
        {
            EntityManager.RemoveComponents(ent, ent.Comp.Blade);
            EntityManager.AddComponents(ent, ent.Comp.Comb);
            state = "comb";
        }
        else
        {
            EntityManager.AddComponents(ent, ent.Comp.Blade);
            state = "blade";
        }

        _items.SetHeldPrefix(ent, state);
        _appearance.SetData(ent, AtomCatsSwitchbladeVisuals.Mode, state);
    }

    private void OnOpenBarberMenu(Entity<AtomCatsSwitchbladeComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!HasComp<MagicMirrorComponent>(ent))
            args.Cancel();
    }
}
