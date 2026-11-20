using System.Numerics;
using Content.Shared.Friction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Dynamics.Joints;

namespace Content.Shared._ReclamationProject.PowerArmor;

public sealed class ArmorTowSystem : VirtualController
{
    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SharedMoverController));
        UpdatesAfter.Add(typeof(TileFrictionController));
        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        var query = EntityQueryEnumerator<ArmorTowComponent, PhysicsComponent, InputMoverComponent>();
        while (query.MoveNext(out var wearer, out var tow, out var body, out var mover))
        {
            var load = tow.Target;
            if (mover.WishDir == Vector2.Zero || !TryComp<PhysicsComponent>(load, out var loadBody) ||
                prediction && (!body.Predict || !loadBody.Predict) ||
                !TryComp<PullableComponent>(load, out var pulled) || pulled.Puller != wearer || pulled.PullJointId is not {} id ||
                !TryComp<JointComponent>(load, out var joints) || !joints.GetJoints.TryGetValue(id, out var joint) ||
                joint is not DistanceJoint cable)
                continue;

            var offset = TransformSystem.GetWorldPosition(wearer) - TransformSystem.GetWorldPosition(load);
            var distance = offset.Length();
            if (distance < cable.Length || distance <= 0f)
                continue;

            // The winch drives the load along the cable. The normal joint still handles slack, turns and obstacles.
            var direction = offset / distance;
            var speed = MathF.Min(Vector2.Dot(mover.WishDir, direction), Vector2.Dot(body.LinearVelocity, direction));
            var gain = speed - Vector2.Dot(loadBody.LinearVelocity, direction);
            if (speed > 0f && gain > 0f)
                PhysicsSystem.SetLinearVelocity(load, loadBody.LinearVelocity + direction * gain, body: loadBody);
        }
    }
}
