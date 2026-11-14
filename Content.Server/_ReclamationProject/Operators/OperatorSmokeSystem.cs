using Content.Shared._ReclamationProject.Operators;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Actions;
using Content.Shared.Mobs.Systems;

namespace Content.Server._ReclamationProject.Operators;

[RegisterComponent]
public sealed partial class OperatorSmokeComponent : Component
{
    public EntityUid? Action;
}

public sealed class OperatorSmokeSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OperatorSmokeComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<OperatorSmokeComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<OperatorSmokeComponent, OperatorSmokeActionEvent>(OnSmoke);
    }

    private void OnStartup(EntityUid uid, OperatorSmokeComponent component, ComponentStartup args)
        => _actions.AddAction(uid, ref component.Action, "ActionOperatorSmoke");

    private void OnShutdown(EntityUid uid, OperatorSmokeComponent component, ComponentShutdown args)
        => _actions.RemoveAction(uid, component.Action);

    private void OnSmoke(Entity<OperatorSmokeComponent> ent, ref OperatorSmokeActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner || !_mob.IsAlive(ent.Owner))
            return;

        var grenade = Spawn("SmokeGrenade", Transform(ent.Owner).Coordinates);
        _trigger.Trigger(grenade, ent.Owner);
        QueueDel(grenade);
        args.Handled = true;
    }
}
