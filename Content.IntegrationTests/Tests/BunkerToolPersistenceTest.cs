using System.Collections.Generic;
using System.IO;
using Content.Server._Reclamation.Bunkers;
using Content.Server.Kitchen.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Shared._Misfits.Surgery.Contamination;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Storage;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class BunkerToolPersistenceTest
{
    [Test]
    public async Task NestedToolsAndLegacyDisplaysSurviveRoomReload()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var bunker = em.System<PersonalBunkerSystem>();
            var displays = em.System<BunkerDisplayWeaponSystem>();
            var containers = em.System<SharedContainerSystem>();
            var solutions = em.System<SharedSolutionContainerSystem>();
            var cells = em.System<PowerCellSystem>();
            var loader = em.System<MapLoaderSystem>();
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 1; x++)
            for (var y = -3; y <= 0; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), new Tile(1));

            EntityUid Spawn(string id) => em.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            EntityUid InStorage(EntityUid owner, string id) =>
                em.GetComponent<StorageComponent>(owner).Container.ContainedEntities.Single(item =>
                    em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == id);

            var belt = Spawn("ReclamationClothingBeltSurgicalFilled");
            var scalpel = InStorage(belt, "Scalpel");
            em.GetComponent<SurgeryDirtinessComponent>(scalpel).Dirtiness = 37;
            var scalpelDamage = new DamageSpecifier(em.GetComponent<MeleeWeaponComponent>(scalpel).Damage);
            var toolbox = Spawn("ToolboxMechanical");
            var storage = em.GetComponent<StorageComponent>(toolbox).Container;
            var driver = Spawn("N14Screwdriver");
            var welder = Spawn("N14WelderIndustrial");
            Assert.That(containers.Insert(driver, storage));
            Assert.That(containers.Insert(welder, storage));
            Assert.That(solutions.TryGetSolution(welder, "Welder", out var tank));
            Assert.That(solutions.RemoveReagent(tank!.Value, "WeldingFuel", 19));
            var remainingFuel = tank.Value.Comp.Solution.Volume;
            var light = Spawn("N14Flashlight");
            Assert.That(cells.TryGetBatteryFromSlot(light, out var cell, out _));
            em.System<BatterySystem>().SetCharge(cell!.Value, 73);
            var knife = Spawn("N14CombatKnife");
            bunker.PrepareRoomContentsForSave(map.Grid, occupied: false);
            Assert.That(em.HasComponent<BunkerDisplayWeaponComponent>(knife));
            Assert.That(em.HasComponent<BunkerDisplayWeaponComponent>(welder), Is.False);

            // Run the old conversion by withholding the new exemption until after conversion.
            var legacy = Spawn("N14Scalpel");
            var melee = em.GetComponent<MeleeWeaponComponent>(legacy);
            var legacyDamage = new DamageSpecifier(melee.Damage);
            var legacyHeavyModifier = melee.HeavyDamageBaseModifier;
            var cuttingSpeed = em.GetComponent<ScalpelComponent>(legacy).Speed;
            melee.AttackRate = 0.63f;
            em.RemoveComponent<BunkerPreservedToolComponent>(legacy);
            displays.Decommission(legacy);
            Assert.That(em.HasComponent<BunkerDisplayWeaponComponent>(legacy));
            Assert.That(em.HasComponent<SharpComponent>(legacy), Is.False);
            em.EnsureComponent<BunkerPreservedToolComponent>(legacy);

            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid.Owner, writer));
            Assert.That(writer.ToString(), Does.Contain("ReclamationClothingBeltSurgical"));
            using var reader = new StringReader(writer.ToString());
            Assert.That(loader.TryLoadGrid(reader, "bunker-tool-test", out var loadedMap, out var grid,
                new DeserializationOptions { InitializeMaps = true }));
            try
            {
                var floor = new Dictionary<string, EntityUid>();
                var children = em.GetComponent<TransformComponent>(grid!.Value.Owner).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    if (em.GetComponent<MetaDataComponent>(child).EntityPrototype is { } proto)
                        floor[proto.ID] = child;
                }

                Assert.That(floor.Keys, Does.Contain("ReclamationClothingBeltSurgicalFilled"),
                    $"Loaded floor items: {string.Join(", ", floor.Keys)}");
                bunker.RestoreRoomContents(grid.Value.Owner, loadedMap!.Value.Owner);
                var savedScalpel = InStorage(floor["ReclamationClothingBeltSurgicalFilled"], "Scalpel");
                Assert.That(em.HasComponent<ScalpelComponent>(savedScalpel));
                Assert.That(em.HasComponent<SharpComponent>(savedScalpel));
                Assert.That(em.GetComponent<SurgeryDirtinessComponent>(savedScalpel).Dirtiness, Is.EqualTo((FixedPoint2) 37));
                Assert.That(em.GetComponent<MeleeWeaponComponent>(savedScalpel).Damage.DamageDict, Is.EqualTo(scalpelDamage.DamageDict));
                var savedDriver = InStorage(floor["ToolboxMechanical"], "N14Screwdriver");
                Assert.That(em.System<SharedToolSystem>().HasQuality(savedDriver, "Screwing"));
                Assert.That(em.GetComponent<MeleeWeaponComponent>(savedDriver).Damage.DamageDict["Piercing"], Is.EqualTo((FixedPoint2) 6));
                var savedWelder = InStorage(floor["ToolboxMechanical"], "N14WelderIndustrial");
                Assert.That(solutions.TryGetSolution(savedWelder, "Welder", out var savedTank));
                Assert.That(savedTank!.Value.Comp.Solution.Volume, Is.EqualTo(remainingFuel));
                Assert.That(em.System<ItemToggleSystem>().TryActivate(savedWelder));
                Assert.That(cells.TryGetBatteryFromSlot(floor["N14Flashlight"], out var savedBattery));
                Assert.That(savedBattery!.CurrentCharge, Is.EqualTo(73f));

                var restored = floor["N14Scalpel"];
                displays.Decommission(restored);
                Assert.That(em.HasComponent<BunkerDisplayWeaponComponent>(restored), Is.False);
                Assert.That(em.HasComponent<SharpComponent>(restored));
                Assert.That(em.GetComponent<ScalpelComponent>(restored).Speed, Is.EqualTo(cuttingSpeed));
                var restoredMelee = em.GetComponent<MeleeWeaponComponent>(restored);
                Assert.That(restoredMelee.Damage.DamageDict, Is.EqualTo(legacyDamage.DamageDict));
                Assert.That(restoredMelee.DisableHeavy, Is.False);
                Assert.That(restoredMelee.HeavyDamageBaseModifier, Is.EqualTo(legacyHeavyModifier));
                Assert.That(restoredMelee.AttackRate, Is.EqualTo(0.63f));
                var savedKnife = floor["N14CombatKnife"];
                Assert.That(em.HasComponent<BunkerDisplayWeaponComponent>(savedKnife));
                Assert.That(em.GetComponent<MeleeWeaponComponent>(savedKnife).Damage.DamageDict, Is.EqualTo(
                    new Dictionary<string, FixedPoint2> { ["Blunt"] = 1 }));

                // The first load removes the legacy marker. Later saves must retain the repaired stats on their own.
                bunker.PrepareRoomContentsForSave(grid.Value.Owner, occupied: false);
                using var repairedSave = new StringWriter();
                Assert.That(loader.TrySaveGrid(grid.Value.Owner, repairedSave));
                using var repairedReader = new StringReader(repairedSave.ToString());
                Assert.That(loader.TryLoadGrid(repairedReader, "repaired-tool-test", out var nextMap, out var nextGrid,
                    new DeserializationOptions { InitializeMaps = true }));
                try
                {
                    var query = em.AllEntityQueryEnumerator<ScalpelComponent, TransformComponent>();
                    var recovered = false;
                    while (query.MoveNext(out var item, out _, out var xform))
                    {
                        if (xform.ParentUid != nextGrid!.Value.Owner || em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID != "N14Scalpel")
                            continue;
                        Assert.That(em.GetComponent<MeleeWeaponComponent>(item).Damage.DamageDict, Is.EqualTo(legacyDamage.DamageDict));
                        recovered = true;
                    }
                    Assert.That(recovered);
                }
                finally
                {
                    em.DeleteEntity(nextMap?.Owner);
                }
            }
            finally
            {
                em.DeleteEntity(loadedMap?.Owner);
            }
        });
        await pair.CleanReturnAsync();
    }
}
