// #Misfits Add - Server-side Paracusia perk driver. Tracks time-alive and updates the
// shared HallucinationsComponent through HallucinationsSystem so the perk reuses the
// same chat / audio / phantom infrastructure as Stealth Boy radiation.
using Content.Shared._Misfits.Hallucinations;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Hallucinations;

public sealed class ParacusiaSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly HallucinationsSystem _hallucinations = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MisfitsParacusiaComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<MisfitsParacusiaComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.StartTime = _timing.CurTime;
        // Tier 1 starts immediately so the perk has at least passive flavor popups.
        ent.Comp.CurrentLevel = 1;
        _hallucinations.RefreshIntensity(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MisfitsParacusiaComponent>();
        while (query.MoveNext(out var uid, out var paracusia))
        {
            var aliveSeconds = (float)(now - paracusia.StartTime).TotalSeconds;
            var newLevel = 0;
            for (var i = paracusia.TierThresholds.Length - 1; i >= 1; i--)
            {
                if (aliveSeconds >= paracusia.TierThresholds[i])
                {
                    newLevel = i;
                    break;
                }
            }

            if (newLevel == paracusia.CurrentLevel)
                continue;

            paracusia.CurrentLevel = newLevel;
            _hallucinations.RefreshIntensity(uid);
        }
    }
}
