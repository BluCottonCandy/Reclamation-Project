using System.Collections.Generic;
using System.Numerics;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Movement.Systems;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests;

[TestFixture, NonParallelizable]
public sealed class IdleMobWorkTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: IdleWorkTestBody
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      flammable:
        shape: !type:PhysShapeCircle
          radius: 0.35
        density: 100
        hard: false
        mask: [MobLayer]
        layer: [MobLayer]
  - type: MovementSpeedModifier
  - type: IdleWorkProbe

- type: entity
  id: IdleWorkTestMob
  parent: IdleWorkTestBody
  components:
  - type: MobCollision
  - type: Damageable
  - type: MobState
    allowedStates: [Alive, Critical, Dead]
  - type: PassiveDamage
    allowedStates: [Alive, Critical]
    damage:
      types:
        Blunt: -1
""";

    [Test]
    public async Task PassiveRecoverySkipsHealthyMobsAndPreservesTimingCapsAndStates()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var map, runMapInit: true);
            try
            {
                var mob = em.SpawnEntity("IdleWorkTestMob", new MapCoordinates(0, 0, map));
                var passive = em.GetComponent<PassiveDamageComponent>(mob);
                var probe = em.GetComponent<IdleWorkProbeComponent>(mob);
                var damage = em.GetComponent<DamageableComponent>(mob);
                var system = em.System<PassiveDamageSystem>();
                passive.NextDamage = TimeSpan.Zero;
                system.Update(0);
                Assert.That(probe.DamageAttempts, Is.Zero, "healthy mobs skip the damage pipeline");
                Assert.That(passive.NextDamage, Is.GreaterThan(server.ResolveDependency<IGameTiming>().CurTime));

                em.System<DamageableSystem>().SetDamage(mob, damage, Damage("Blunt", 5));
                passive.NextDamage = TimeSpan.Zero;
                system.Update(0);
                Assert.That(damage.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(4));
                Assert.That(probe.DamageAttempts, Is.EqualTo(1));
                system.Update(0);
                Assert.That(probe.DamageAttempts, Is.EqualTo(1), "no extra recovery between intervals");

                passive.DamageCap = 4;
                passive.NextDamage = TimeSpan.Zero;
                system.Update(0);
                Assert.That(damage.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(4), "respect existing caps");
                passive.DamageCap = 0;
                em.System<MobStateSystem>().ChangeMobState(mob, MobState.Dead);
                system.Update(0);
                Assert.That(damage.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(4), "dead is not an allowed state");
                em.System<MobStateSystem>().ChangeMobState(mob, MobState.Critical);
                passive.NextDamage = TimeSpan.Zero;
                system.Update(0);
                Assert.That(damage.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(3), "critical recovery still runs");
            }
            finally { maps.DeleteMap(map); }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task MixedDamageStillRunsEvenWithZeroOrNegativeTotal(int heat)
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            maps.CreateMap(out var map, runMapInit: true);
            try
            {
                var mob = em.SpawnEntity("IdleWorkTestMob", new MapCoordinates(0, 0, map));
                var passive = em.GetComponent<PassiveDamageComponent>(mob);
                passive.Damage = Damage("Blunt", -2);
                passive.Damage.DamageDict["Heat"] = heat;
                passive.NextDamage = TimeSpan.Zero;
                em.System<PassiveDamageSystem>().Update(0);
                Assert.That(em.GetComponent<DamageableComponent>(mob).Damage.DamageDict["Heat"].Float(), Is.EqualTo(heat));
                Assert.That(em.GetComponent<IdleWorkProbeComponent>(mob).DamageAttempts, Is.EqualTo(1));
            }
            finally { maps.DeleteMap(map); }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RecoveryKeepsLimbOnlyHealingAndDamageCancellation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            maps.CreateMap(out var map, runMapInit: true);
            try
            {
                var mob = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map));
                var damageSystem = em.System<DamageableSystem>();
                var bodyDamage = em.GetComponent<DamageableComponent>(mob);
                var arm = em.System<SharedBodySystem>().GetBodyChildrenOfType(mob, BodyPartType.Arm).First().Id;
                var armDamage = em.GetComponent<DamageableComponent>(arm);
                damageSystem.SetDamage(arm, armDamage, Damage("Slash", 5));
                Assert.That(bodyDamage.TotalDamage.Float(), Is.Zero, "fixture has only a limb injury");
                var passive = em.EnsureComponent<PassiveDamageComponent>(mob);
                passive.AllowedStates = new List<MobState> { MobState.Alive };
                passive.Damage = Damage("Slash", -1);
                passive.NextDamage = TimeSpan.Zero;
                em.System<PassiveDamageSystem>().Update(0);
                Assert.That(armDamage.Damage.DamageDict["Slash"].Float(), Is.LessThan(5), "full overall health must not suppress limb recovery");

                damageSystem.SetDamage(mob, bodyDamage, Damage("Slash", 5));
                em.AddComponent<GodmodeComponent>(mob);
                passive.NextDamage = TimeSpan.Zero;
                em.System<PassiveDamageSystem>().Update(0);
                Assert.That(bodyDamage.Damage.DamageDict["Slash"].Float(), Is.EqualTo(5), "normal cancellation hooks still apply");
            }
            finally { maps.DeleteMap(map); }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    // Drive one recovery interval without advancing unrelated biology systems.
#pragma warning disable RA0002
    public async Task BloodRecoverySkipsHealthyButHealsExistingBloodloss()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            maps.CreateMap(out var map, runMapInit: true);
            try
            {
                var mob = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map));
                var probe = em.AddComponent<IdleWorkProbeComponent>(mob);
                var blood = em.GetComponent<BloodstreamComponent>(mob);
                var damage = em.GetComponent<DamageableComponent>(mob);
                blood.NextUpdate = TimeSpan.Zero;
                em.System<BloodstreamSystem>().Update(0);
                Assert.That(probe.DamageAttempts, Is.Zero, "full blood with no bloodloss needs no healing call");
                em.System<DamageableSystem>().SetDamage(mob, damage, Damage("Bloodloss", 5));
                blood.NextUpdate = TimeSpan.Zero;
                em.System<BloodstreamSystem>().Update(0);
                Assert.That(damage.Damage.DamageDict["Bloodloss"].Float(), Is.LessThan(5));
                Assert.That(probe.DamageAttempts, Is.EqualTo(1));
            }
            finally { maps.DeleteMap(map); }
        });
        await pair.CleanReturnAsync();
    }
#pragma warning restore RA0002

    [Test]
    public async Task CollisionCandidatesFollowContactsComponentsAndPause()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var collision = em.System<MobCollisionSystem>();
        MapId map = default;
        EntityUid a = default, b = default, c = default;
        var wasPushing = false;
        await server.WaitAssertion(() =>
        {
            wasPushing = server.CfgMan.GetCVar(CCVars.MovementMobPushing);
            server.CfgMan.SetCVar(CCVars.MovementMobPushing, true);
            maps.CreateMap(out map, runMapInit: true);
            a = em.SpawnEntity("IdleWorkTestMob", new MapCoordinates(0, 0, map));
            b = em.SpawnEntity("IdleWorkTestBody", new MapCoordinates(0.4f, 0, map));
            em.GetComponent<IdleWorkProbeComponent>(a).BlockCollision = true;
            em.GetComponent<IdleWorkProbeComponent>(b).BlockCollision = true;
        });
        try
        {
            await server.WaitRunTicks(3);
            await server.WaitAssertion(() =>
            {
                Assert.That(em.GetComponent<PhysicsComponent>(a).ContactCount, Is.GreaterThan(0));
                Assert.That(em.GetComponent<IdleWorkProbeComponent>(a).CollisionAttempts, Is.Zero,
                    "contacts with non-mobs should not enter mob-pushing processing");
                em.AddComponent<MobCollisionComponent>(b);
            });
            await server.WaitRunTicks(3);
            await server.WaitAssertion(() =>
            {
                var probe = em.GetComponent<IdleWorkProbeComponent>(a);
                Assert.That(probe.CollisionAttempts, Is.GreaterThan(0), "adding MobCollision to an existing contact activates both sides");
                Assert.That(em.GetComponent<IdleWorkProbeComponent>(b).CollisionAttempts, Is.GreaterThan(0));
                maps.SetPaused(map, true);
                probe.CollisionAttempts = 0;
                collision.Update(1f / 30);
                Assert.That(probe.CollisionAttempts, Is.Zero, "paused maps retain their pause semantics");
                maps.SetPaused(map, false);
                collision.Update(1f / 30);
                Assert.That(probe.CollisionAttempts, Is.GreaterThan(0), "contacts resume after unpausing");
                em.RemoveComponent<MobCollisionComponent>(b);
                probe.CollisionAttempts = 0;
            });
            await server.WaitRunTicks(3);
            await server.WaitAssertion(() =>
            {
                Assert.That(em.GetComponent<IdleWorkProbeComponent>(a).CollisionAttempts, Is.Zero, "removing MobCollision clears the other side's candidate");
                em.AddComponent<MobCollisionComponent>(b);
                c = em.SpawnEntity("IdleWorkTestMob", new MapCoordinates(-0.4f, 0, map));
                em.GetComponent<IdleWorkProbeComponent>(c).BlockCollision = true;
            });
            await server.WaitRunTicks(3);
            await server.WaitAssertion(() =>
            {
                em.DeleteEntity(b);
                em.GetComponent<IdleWorkProbeComponent>(a).CollisionAttempts = 0;
            });
            await server.WaitRunTicks(3);
            await server.WaitAssertion(() =>
            {
                Assert.That(em.GetComponent<IdleWorkProbeComponent>(a).CollisionAttempts, Is.GreaterThan(0), "ending one contact must keep another active");
                em.System<SharedTransformSystem>().SetWorldPosition(c, new Vector2(10, 0));
            });
            await server.WaitRunTicks(3);
            await server.WaitAssertion(() => em.GetComponent<IdleWorkProbeComponent>(a).CollisionAttempts = 0);
            await server.WaitRunTicks(3);
            await server.WaitAssertion(() => Assert.That(em.GetComponent<IdleWorkProbeComponent>(a).CollisionAttempts,
                Is.Zero, "separated idle mobs leave the candidate set"));
        }
        finally
        {
            await server.WaitPost(() =>
            {
                maps.DeleteMap(map);
                server.CfgMan.SetCVar(CCVars.MovementMobPushing, wasPushing);
            });
        }
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StationaryMobsStillPushAndCollisionBufferExpires()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        MapId map = default;
        EntityUid a = default, b = default;
        var wasPushing = false;
        await server.WaitAssertion(() =>
        {
            wasPushing = server.CfgMan.GetCVar(CCVars.MovementMobPushing);
            server.CfgMan.SetCVar(CCVars.MovementMobPushing, true);
            maps.CreateMap(out map, runMapInit: true);
            a = em.SpawnEntity("IdleWorkTestMob", new MapCoordinates(0, 0, map));
            b = em.SpawnEntity("IdleWorkTestMob", new MapCoordinates(0.4f, 0, map));
            Assert.That(em.HasComponent<InputMoverComponent>(a), Is.False, "test mob has no movement input, like a sleeping NPC");
        });
        try
        {
            await server.WaitRunTicks(3);
            await server.WaitAssertion(() =>
            {
                Assert.That(em.GetComponent<IdleWorkProbeComponent>(a).CollisionAttempts, Is.GreaterThan(0));
                Assert.That(em.GetComponent<PhysicsComponent>(a).LinearVelocity.LengthSquared(), Is.GreaterThan(0));
                Assert.That(em.GetComponent<MobCollisionComponent>(a).Colliding, Is.True);
                em.System<SharedTransformSystem>().SetWorldPosition(b, new Vector2(10, 0));
            });
            await server.WaitRunTicks(20);
            await server.WaitAssertion(() =>
            {
                var collision = em.GetComponent<MobCollisionComponent>(a);
                Assert.That(collision.Colliding, Is.False);
                Assert.That(collision.SpeedModifier, Is.EqualTo(1f), "no lingering collision slowdown after separation");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                maps.DeleteMap(map);
                server.CfgMan.SetCVar(CCVars.MovementMobPushing, wasPushing);
            });
        }
        await pair.CleanReturnAsync();
    }

    private static DamageSpecifier Damage(string type, int value)
        => new() { DamageDict = new() { [type] = value } };
}

[RegisterComponent]
public sealed partial class IdleWorkProbeComponent : Component
{
    public int DamageAttempts;
    public int CollisionAttempts;
    public bool BlockCollision;
}

public sealed class IdleWorkProbeSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<IdleWorkProbeComponent, BeforeDamageChangedEvent>(OnDamage);
        SubscribeLocalEvent<IdleWorkProbeComponent, AttemptMobCollideEvent>(OnCollision);
    }

    private void OnDamage(Entity<IdleWorkProbeComponent> ent, ref BeforeDamageChangedEvent args)
        => ent.Comp.DamageAttempts++;

    private void OnCollision(Entity<IdleWorkProbeComponent> ent, ref AttemptMobCollideEvent args)
    {
        ent.Comp.CollisionAttempts++;
        if (ent.Comp.BlockCollision)
            args.Cancelled = true;
    }
}
