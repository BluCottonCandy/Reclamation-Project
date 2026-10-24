using Content.Shared._Goobstation.Clothing.Components;
using Content.Shared._ReclamationProject.Institute.Augment;
using Content.Shared.Weapons.Reflect;

namespace Content.Server._ReclamationProject.Institute;

/// <summary>
/// Turns an Institute MOD suit's bullet/laser deflect on while the suit is sealed and powered,
/// and off the moment it's unsealed or the cell runs dry.
/// </summary>
public sealed class InstituteSuitDeflectSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.25);
    private TimeSpan _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InstituteSuitDeflectComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<InstituteSuitDeflectComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<ReflectComponent>(ent, out var reflect))
            return;

        ent.Comp.ActiveReflects = reflect.Reflects;
        UpdateDeflect(ent, reflect);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += TimeSpan.FromSeconds(frameTime);
        if (_accumulator < CheckInterval)
            return;
        _accumulator = TimeSpan.Zero;

        var query = EntityQueryEnumerator<InstituteSuitDeflectComponent, ReflectComponent>();
        while (query.MoveNext(out var uid, out var deflect, out var reflect))
        {
            UpdateDeflect((uid, deflect), reflect);
        }
    }

    private void UpdateDeflect(Entity<InstituteSuitDeflectComponent> ent, ReflectComponent reflect)
    {
        var on = TryComp<SealableClothingControlComponent>(ent, out var control)
            && control.IsCurrentlySealed
            && TryComp<SealableClothingRequiresPowerComponent>(ent, out var power)
            && power.IsPowered;

        if (on == ent.Comp.Active && reflect.Reflects == (on ? ent.Comp.ActiveReflects : ReflectType.None))
            return;

        ent.Comp.Active = on;
        reflect.Reflects = on ? ent.Comp.ActiveReflects : ReflectType.None;
        Dirty(ent.Owner, reflect);
    }
}
