using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Inventory;

namespace Content.Shared._ReclamationProject.Blindness;

/// <summary>Evaluates worn blindfolds on both peers without changing eyesight or concentration.</summary>
public sealed class BlindAwarenessSystem : EntitySystem
{
    private static readonly string[] BlindfoldSlots = { "eyes", "head", "mask" };
    [Dependency] private readonly InventorySystem _inventory = default!;

    public float GetRadiusMultiplier(EntityUid uid, TimeSpan now)
    {
        if (!TryComp<BlindConcentrationComponent>(uid, out var focus))
            return 1f;
        var radius = 1f + (focus.MaximumRadiusMultiplier - 1f) * focus.Progress(now);
        foreach (var slot in BlindfoldSlots)
        {
            if (_inventory.TryGetSlotEntity(uid, slot, out var item) && HasComp<BlindfoldComponent>(item.Value))
                return Math.Max(radius, 3f); // Matches the ordinary fully focused radius.
        }
        return radius;
    }

    public float GetVisualProgress(EntityUid uid, BlindConcentrationComponent focus, TimeSpan now)
        => (GetRadiusMultiplier(uid, now) - 1f) / (focus.MaximumRadiusMultiplier - 1f);
}
