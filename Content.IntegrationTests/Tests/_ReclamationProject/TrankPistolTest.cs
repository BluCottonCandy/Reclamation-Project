using System.Numerics;
using Content.Server.Body.Components;
using Content.Shared._Misfits.CCVar;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - the trank pistol fires its one dart, the dart sticks in the target and
/// injects its whole 20u at once.
/// </summary>
[TestFixture]
public sealed class TrankPistolTest
{
    private const string Gun = "RPWeaponPistolTrank";
    private const string Dart = "RPTrankDart";
    private const string Reagent = "ChloralHydrate";

    [Test]
    public async Task TrankDartInjectsTwentyUnitsOnHit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var originalPrediction = cfg.GetCVar(PerformanceCVars.GunPrediction);
        var solutions = entities.System<SharedSolutionContainerSystem>();

        EntityUid user = default, target = default, gun = default, dart = default;

        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(PerformanceCVars.GunPrediction, false);
            user = entities.SpawnEntity("MobHuman", map.GridCoords);
            target = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(3, 0)));
            gun = entities.SpawnEntity(Gun, map.GridCoords);

            // Bought from the uplink with one dart already in the tray.
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(gun, "trank_dart", out var slot), Is.True);
            Assert.That(slot!.Item, Is.Not.Null, "the trank pistol must come with one dart loaded");
            dart = slot.Item!.Value;
            Assert.That(entities.GetComponent<MetaDataComponent>(dart).EntityPrototype?.ID, Is.EqualTo(Dart));

            var ammo = new GetAmmoCountEvent();
            entities.EventBus.RaiseLocalEvent(gun, ref ammo);
            // ContainerAmmoProvider always reports int.MaxValue capacity; the one-item slot is what limits it.
            Assert.That(ammo.Count, Is.EqualTo(1));

            // The dart holds 20u, filled like any container.
            Assert.That(solutions.TryGetSolution(dart, "dart", out var dartSoln, out var solution), Is.True);
            Assert.That(solution!.MaxVolume, Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(solutions.TryAddReagent(dartSoln!.Value, Reagent, FixedPoint2.New(20), out _), Is.True);
            Assert.That(solution.Volume, Is.EqualTo(FixedPoint2.New(20)));

            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, gun), Is.True);
        });

        // Picking up a gun delays its next shot, so let that pass first.
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var gunComp = entities.GetComponent<GunComponent>(gun);
            entities.System<SharedGunSystem>().AttemptShoot(user, gun, gunComp,
                new EntityCoordinates(target, Vector2.Zero));
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var ammo = new GetAmmoCountEvent();
            entities.EventBus.RaiseLocalEvent(gun, ref ammo);
            Assert.That(ammo.Count, Is.EqualTo(0), "after the shot the tray is empty");

            Assert.That(entities.TryGetComponent<EmbeddableProjectileComponent>(dart, out var embed), Is.True);
            Assert.That(embed!.Target, Is.EqualTo(target), "the dart must stick in whoever it hits");

            Assert.That(solutions.TryGetSolution(dart, "dart", out _, out var dartSolution), Is.True);
            Assert.That(dartSolution!.Volume, Is.EqualTo(FixedPoint2.Zero), "the whole dose goes in at once");

            var bloodstream = entities.GetComponent<BloodstreamComponent>(target);
            Assert.That(solutions.TryGetSolution(target, bloodstream.ChemicalSolutionName, out _, out var chems), Is.True);
            // Metabolism may already have used a sliver of it.
            Assert.That(chems!.GetTotalPrototypeQuantity(Reagent), Is.GreaterThan(FixedPoint2.New(15)));
        });

        await server.WaitPost(() => cfg.SetCVar(PerformanceCVars.GunPrediction, originalPrediction));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyTrankDartsFitTheTray()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var gun = entities.SpawnEntity(Gun, map.GridCoords);
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.TryEject(gun, "trank_dart", null, out var ejected), Is.True);

            var arrow = entities.SpawnEntity("ArrowRegular", map.GridCoords);
            Assert.That(slots.TryInsert(gun, "trank_dart", arrow, null), Is.False, "arrows must not load into the trank pistol");
            Assert.That(slots.TryInsert(gun, "trank_dart", ejected!.Value, null), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
