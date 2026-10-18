using System.Linq;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;

namespace Content.Server._ReclamationProject.Medical;

public sealed partial class TraumaSystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    private void InitializeSplints()
    {
        SubscribeLocalEvent<TraumaSplintComponent, AfterInteractEvent>(OnSplintInteract);
        SubscribeLocalEvent<TraumaSplintComponent, TraumaSplintDoAfterEvent>(OnSplintFinished);
    }

    private void OnSplintInteract(EntityUid uid, TraumaSplintComponent comp, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target ||
            !TryComp<BodyComponent>(target, out var body) || body.RootContainer.ContainedEntity is not { } root) return;
        var broken = _body.GetBodyPartChildren(root).FirstOrDefault(p =>
            TryComp<FractureComponent>(p.Id, out var fracture) && !fracture.Splinted);
        if (broken == default) return;
        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(8),
            new TraumaSplintDoAfterEvent(), uid, broken.Id, uid)
        {
            NeedHand = true, BreakOnMove = true, BreakOnDamage = true, BreakOnHandChange = true,
        });
    }

    private void OnSplintFinished(EntityUid uid, TraumaSplintComponent comp, TraumaSplintDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } part ||
            !TryComp<FractureComponent>(part, out var fracture) || fracture.Splinted ||
            !TryComp<BodyPartComponent>(part, out var bodyPart) || bodyPart.Body is not { } body) return;
        fracture.Splinted = true;
        Dirty(part, fracture);
        _movement.RefreshMovementSpeedModifiers(body);
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("trauma-splint-applied"), body, args.User);
        QueueDel(uid);
    }
}
