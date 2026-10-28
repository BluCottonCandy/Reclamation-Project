using System.Linq;
using Content.Shared._ReclamationProject.Endurance;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Body.Part;
using Robust.Shared.Timing;

namespace Content.Shared._ReclamationProject.Medical;

public sealed partial class TraumaSurgerySystem
{
    [Dependency] private readonly IGameTiming _limbProtectionTiming = default!;
    private readonly HashSet<EntityUid> _enragedLimbBodies = new();

    public bool HasLimbDisableProtection(EntityUid part)
    {
        if (!TryComp<TraumaLimbComponent>(part, out var health) ||
            !TryComp<BodyPartComponent>(part, out var limb) ||
            limb.PartType is not (BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot) ||
            limb.Body is not { } body || !_body.BodyHasChild(body, part)) return false;
        return health.TemporaryHealth > 0f || IsEnragedLimbBody(body);
    }

    private bool IsEnragedLimbBody(EntityUid body)
        => TryComp<EnduranceEnragedComponent>(body, out var rage) &&
            rage.Active && _limbProtectionTiming.CurTime < rage.EndsAt;

    public override void Update(float frameTime)
    {
        // Refresh existing disabled limbs at activation; disable events are also
        // intercepted above so later injuries cannot remove their function.
        var query = EntityQueryEnumerator<EnduranceEnragedComponent>();
        while (query.MoveNext(out var body, out var rage))
        {
            if (!HasComp<TraumaBodyComponent>(body) || !rage.Active ||
                _limbProtectionTiming.CurTime >= rage.EndsAt || !_enragedLimbBodies.Add(body)) continue;
            RefreshEnragedLimbs(body, true);
        }
        foreach (var body in _enragedLimbBodies.ToArray())
        {
            if (IsEnragedLimbBody(body)) continue;
            _enragedLimbBodies.Remove(body);
            if (!TerminatingOrDeleted(body)) RefreshEnragedLimbs(body, false);
        }
    }

    private void RefreshEnragedLimbs(EntityUid body, bool active)
    {
        if (!_net.IsServer) return;
        foreach (var (part, limb) in _body.GetBodyChildren(body).ToArray())
        {
            if (!HasComp<TraumaLimbComponent>(part) ||
                limb.PartType is not (BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot)) continue;
            if (active)
            {
                var enabled = new BodyPartEnableChangedEvent(true);
                RaiseLocalEvent(part, ref enabled);
            }
            else
            {
                _body.CheckBodyPart((part, limb), _body.GetTargetBodyPart(limb), false);
                var health = Comp<TraumaLimbComponent>(part);
                if (!HasLimbDisableProtection(part) &&
                    (health.MaximumHealth <= 0f || HasComp<FractureComponent>(part)))
                {
                    var disabled = new BodyPartEnableChangedEvent(false);
                    RaiseLocalEvent(part, ref disabled);
                }
            }
        }
    }
}
