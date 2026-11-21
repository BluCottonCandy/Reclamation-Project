using Content.Shared._ReclamationProject.Operators;

namespace Content.Client._ReclamationProject.Operators;

public sealed class OperatorTrackerSystem : EntitySystem
{
    private readonly List<EntityUid> _markers = [];

    public override void Initialize()
    {
        SubscribeNetworkEvent<OperatorTrailEvent>(OnTrail);
    }

    private void OnTrail(OperatorTrailEvent args)
    {
        foreach (var marker in _markers)
            QueueDel(marker);
        _markers.Clear();

        foreach (var step in args.Steps)
        {
            if (TryGetEntity(step.NetEntity, out var anchor) && HasComp<TransformComponent>(anchor.Value))
                _markers.Add(Spawn("OperatorTrailFootprint", GetCoordinates(step)));
        }
    }
}
