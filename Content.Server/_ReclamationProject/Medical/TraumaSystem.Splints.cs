using System.Linq;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared.Body.Part;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Verbs;

namespace Content.Server._ReclamationProject.Medical;

public sealed partial class TraumaSystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    private void InitializeSplints()
    {
        SubscribeLocalEvent<TraumaSplintComponent, AfterInteractEvent>(OnSplintInteract);
        SubscribeLocalEvent<TraumaBodyComponent, GetVerbsEvent<Verb>>(AddSplintVerb);
        SubscribeLocalEvent<TraumaSplintComponent, TraumaSplintDoAfterEvent>(OnSplintFinished);
    }

    private void OnSplintInteract(EntityUid uid, TraumaSplintComponent comp, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target ||
            !HasComp<TraumaBodyComponent>(target))
            return;

        args.Handled = true;
        TryApplySplint(uid, args.User, target);
    }

    private void AddSplintVerb(EntityUid patient, TraumaBodyComponent component, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } splint ||
            !HasComp<TraumaSplintComponent>(splint))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("trauma-splint-apply-verb"),
            Act = () => TryApplySplint(splint, args.User, patient),
        });
    }

    private void TryApplySplint(EntityUid splint, EntityUid user, EntityUid patient)
    {
        var parts = _body.GetBodyChildren(patient);
        var broken = parts.FirstOrDefault(part => CanSplintDisabledLeg(part.Item1, part.Item2));
        if (broken == default)
            broken = parts.FirstOrDefault(part => HasComp<FractureComponent>(part.Item1));
        if (broken == default)
        {
            _popup.PopupEntity(Loc.GetString("trauma-splint-no-fracture"), patient, user);
            return;
        }

        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(8),
            new TraumaSplintDoAfterEvent { Part = GetNetEntity(broken.Item1) }, splint, patient, splint)
        {
            NeedHand = true, BreakOnMove = true, BreakOnDamage = true, BreakOnHandChange = true,
        }))
            _popup.PopupEntity(Loc.GetString("trauma-splint-cannot-start"), patient, user);
    }

    private bool CanSplintDisabledLeg(EntityUid part, BodyPartComponent bodyPart)
        => bodyPart.PartType == BodyPartType.Leg &&
           TryComp<TraumaLimbComponent>(part, out var health) &&
           health.OriginalFailureThreshold > 0f &&
           GetLimbHealth(part, health) <= 0f;

    private void OnSplintFinished(EntityUid uid, TraumaSplintComponent comp, TraumaSplintDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } patient ||
            !TryGetEntity(args.Part, out var part) ||
            !TryComp<BodyPartComponent>(part.Value, out var bodyPart) || bodyPart.Body != patient ||
            (!HasComp<FractureComponent>(part.Value) && !CanSplintDisabledLeg(part.Value, bodyPart)))
            return;

        Repair(patient, part.Value, TraumaKind.Fracture, splint: true);
        if (HasComp<FractureComponent>(part.Value) || CanSplintDisabledLeg(part.Value, bodyPart))
        {
            _popup.PopupEntity(Loc.GetString("trauma-splint-treatment-blocked"), patient, args.User);
            return;
        }

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("trauma-splint-applied"), patient, args.User);
        QueueDel(uid);
    }
}
