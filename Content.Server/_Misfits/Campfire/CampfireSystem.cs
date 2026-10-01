// #Misfits Add - Server-side campfire appearance: toggles point light,
// ambient sound, AlwaysHotComponent, and appearance layer state.
using Content.Server.Atmos.Components;
using Content.Shared._Misfits.Campfire;
using Content.Shared.Audio;
using Content.Shared.Light.Components;
using Content.Shared.Temperature.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Components;

namespace Content.Server._Misfits.Campfire;

public sealed class CampfireSystem : SharedCampfireSystem
{
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    protected override void UpdateAppearance(Entity<CampfireComponent> ent)
    {
        base.UpdateAppearance(ent);

        // Toggle point light
        if (TryComp<PointLightComponent>(ent, out var light))
            _lights.SetEnabled(ent, ent.Comp.Lit, light);

        // Toggle ambient fire crackle sound
        if (TryComp<AmbientSoundComponent>(ent, out var ambientSound))
            _ambient.SetAmbience(ent, ent.Comp.Lit, ambientSound);

        // Make the campfire an ignition source when burning (AlwaysHot = hot for IsHotEvent)
        if (ent.Comp.Lit)
            EnsureComp<AlwaysHotComponent>(ent);
        else
            RemCompDeferred<AlwaysHotComponent>(ent);

        // Drive the appearance system (controls burning layer visibility)
        if (TryComp<AppearanceComponent>(ent, out var appearance))
            _appearance.SetData(ent, CampfireVisuals.Lit, ent.Comp.Lit, appearance);
    }
}
