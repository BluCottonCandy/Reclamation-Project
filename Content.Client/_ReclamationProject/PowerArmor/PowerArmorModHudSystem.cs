using Content.Client.Overlays;
using Content.Client.Verbs.UI;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Inventory.Events;
using Content.Shared.Overlays;
using Content.Shared.Overlays.Switchable;
using Robust.Client.UserInterface;
using Robust.Client.Graphics;

namespace Content.Client._ReclamationProject.PowerArmor;

public sealed class PowerArmorModHudSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly ShowHealthBarsSystem _bars = default!;
    [Dependency] private readonly ShowHealthIconsSystem _icons = default!;
    [Dependency] private readonly Content.Client.Overlays.Switchable.NightVisionSystem _night = default!;

    [Dependency] private readonly IOverlayManager _overlays = default!;
    private JunkhoundPulseOverlay _junkhound = default!;

    private readonly ShowHealthBarsComponent _medicalBars = new() { DamageContainers = new() { "Biological" } };
    private readonly ShowHealthBarsComponent _armorBars = new() { ShowArmorCondition = true };
    private readonly ShowHealthIconsComponent _medicalIcons = new() { DamageContainers = new() { "Biological" } };
    private readonly NightVisionComponent _visor = new() { IsActive = true };

    public override void Initialize()
    {
        _junkhound = new JunkhoundPulseOverlay();
        _overlays.AddOverlay(_junkhound);
        SubscribeNetworkEvent<ChooseArmorModEvent>(ev => _ui.GetUIController<VerbMenuUIController>().OpenVerbMenu(ev.Plate));
        SubscribeLocalEvent<PowerArmorModWearerComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<PowerArmorModWearerComponent, RefreshEquipmentHudEvent<ShowHealthBarsComponent>>((_, comp, ev) =>
        {
            Provide(comp.MedicalHud, ev, _medicalBars);
            Provide(comp.MechanicVisor, ev, _armorBars);
        });
        SubscribeLocalEvent<PowerArmorModWearerComponent, RefreshEquipmentHudEvent<ShowHealthIconsComponent>>((_, comp, ev) => Provide(comp.MedicalHud, ev, _medicalIcons));
        SubscribeLocalEvent<PowerArmorModWearerComponent, RefreshEquipmentHudEvent<NightVisionComponent>>((_, comp, ev) => Provide(comp.NightVision, ev, _visor));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_junkhound);
        _junkhound.Dispose();
        base.Shutdown();
    }

    private void OnState(Entity<PowerArmorModWearerComponent> wearer, ref AfterAutoHandleStateEvent ev) => Refresh(wearer);

    private static void Provide<T>(bool enabled, RefreshEquipmentHudEvent<T> ev, T provider) where T : IComponent
    {
        if (!enabled) return;
        ev.Active = true;
        ev.Components.Add(provider);
    }

    private void Refresh(EntityUid wearer)
    {
        _bars.RefreshOverlay(wearer);
        _icons.RefreshOverlay(wearer);
        _night.RefreshOverlay(wearer);
    }
}
