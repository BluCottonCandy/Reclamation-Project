// #Misfits Change /Add:/ Shared system that turns the Med-X status effect into real damage reduction.
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.DrugEffects;

/// <summary>
///     Adds and removes a temporary damage protection modifier while the status component exists.
/// </summary>
public sealed class MedXProtectionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedXProtectionComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<MedXProtectionComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnInit(EntityUid uid, MedXProtectionComponent component, ComponentInit args)
    {
        if (!_prototypeManager.TryIndex(component.Modifier, out DamageModifierSetPrototype? modifier))
            return;

        var buff = EnsureComp<DamageProtectionBuffComponent>(uid);
        if (!buff.Modifiers.ContainsKey(component.Modifier))
            buff.Modifiers.Add(component.Modifier, modifier);
    }

    private void OnShutdown(EntityUid uid, MedXProtectionComponent component, ComponentShutdown args)
    {
        if (!TryComp<DamageProtectionBuffComponent>(uid, out var buff))
            return;

        buff.Modifiers.Remove(component.Modifier);
        if (buff.Modifiers.Count == 0)
            RemComp<DamageProtectionBuffComponent>(uid);
    }
}