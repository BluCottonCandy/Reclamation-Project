// #Reclamation Project - a powered stasis bed keeps its patient alive through organ loss, so the
// Autodoc (or a surgeon) can take every organ out without the patient dying on the bed.
using Content.Server.Bed.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Body.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server._ReclamationProject.Medical;

public sealed class StasisLifeSupportSystem : EntitySystem
{
    [Dependency] private readonly PowerReceiverSystem _power = default!;

    /// <summary>
    /// What organ loss kills you with: suffocation (no lungs), blood loss (no heart, no brain, bleeding out)
    /// and poison (no liver or kidneys).
    /// </summary>
    private static readonly string[] BlockedTypes = { "Asphyxiation", "Bloodloss", "Poison" };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BuckleComponent, BeforeDamageChangedEvent>(OnBeforeDamage);
    }

    public bool IsOnLifeSupport(EntityUid uid, BuckleComponent? buckle = null)
    {
        if (!Resolve(uid, ref buckle, false) || buckle.BuckledTo is not { } bed || !HasComp<BodyComponent>(uid))
            return false;

        // An emagged bed speeds metabolism up instead (multiplier below 1), so it gives no life support.
        return TryComp<StasisBedComponent>(bed, out var stasis) && stasis.Multiplier > 1f && _power.IsPowered(bed);
    }

    private void OnBeforeDamage(Entity<BuckleComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (!IsOnLifeSupport(ent, ent.Comp))
            return;

        DamageSpecifier? filtered = null;
        foreach (var type in BlockedTypes)
        {
            if (!args.Damage.DamageDict.TryGetValue(type, out var amount) || amount <= FixedPoint2.Zero)
                continue;

            filtered ??= new DamageSpecifier(args.Damage);
            filtered.DamageDict.Remove(type);
        }

        if (filtered == null)
            return;

        if (filtered.Empty)
            args.Cancelled = true;
        else
            args.Damage = filtered;
    }
}
