using Content.Server.Body.Systems;
using Content.Shared._ReclamationProject.Institute.Nanites;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Institute.Nanites;

/// <summary>
/// Keeps hunger and thirst at "Okay" or better. Eating, drinking and meds all still work as normal;
/// nothing (including meds that drain hunger) can make the body hungry or thirsty. Low blood never hurts.
/// </summary>
public sealed class SyntheticSustenanceSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly ThirstSystem _thirst = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SyntheticSustenanceComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<SyntheticSustenanceComponent> ent, ref ComponentStartup args)
    {
        // Same trick zombies use: blood never counts as low, so no blood-loss damage, dizziness or stutter.
        _bloodstream.SetBloodLossThreshold(ent, 0f);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SyntheticSustenanceComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextCheck)
                continue;
            comp.NextCheck = now + CheckInterval;
            TopUp(uid);
        }
    }

    public void TopUp(EntityUid uid)
    {
        if (TryComp<HungerComponent>(uid, out var hunger))
        {
            // Read into a local first; the access analyzer won't allow calls on the component's dictionary.
            var hungerThresholds = hunger.Thresholds;
            if (hungerThresholds.TryGetValue(HungerThreshold.Okay, out var fed) && hunger.CurrentHunger < fed)
                _hunger.SetHunger(uid, fed, hunger);
        }

        if (!TryComp<ThirstComponent>(uid, out var thirst))
            return;
        // Read into a local first; the access analyzer won't allow calls on the component's dictionary.
        var thirstThresholds = thirst.ThirstThresholds;
        if (thirstThresholds.TryGetValue(ThirstThreshold.Okay, out var watered) && thirst.CurrentThirst < watered)
            _thirst.SetThirst(uid, thirst, watered);
    }
}
