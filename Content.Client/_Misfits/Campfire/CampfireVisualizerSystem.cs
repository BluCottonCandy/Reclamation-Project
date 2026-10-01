// #Misfits Add - Client-side campfire visualizer that drives the burning sprite
// layer on/off based on the GenericVisualizer enum.CampfireVisuals.Lit data.
using Content.Shared._Misfits.Campfire;
using Robust.Client.GameObjects;

namespace Content.Client._Misfits.Campfire;

/// <summary>
/// Drives the campfire sprite appearance on the client via the appearance component.
/// The actual layer visibility toggle is handled by GenericVisualizer in YAML.
/// This system registers the CampfireComponent for client-side appearance updates.
/// </summary>
public sealed class CampfireVisualizerSystem : VisualizerSystem<CampfireComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, CampfireComponent component,
        ref AppearanceChangeEvent args)
    {
        // GenericVisualizer in the YAML handles the burning layer visibility.
        // No additional client logic needed beyond what GenericVisualizer provides.
    }
}
