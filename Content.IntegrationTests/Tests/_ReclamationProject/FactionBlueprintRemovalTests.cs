using System;
using System.Numerics;
using Content.Server._ReclamationProject.Supplies;
using Content.Shared._Misfits.Crafting;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Containers;
using System.Threading.Tasks;
namespace Content.IntegrationTests.Tests._ReclamationProject;
[TestFixture]
public sealed class FactionBlueprintRemovalTests
{
    [Test]
    public async Task NewAndStoredBlueprintsAreRemovedWithoutDeletingTheirHolders()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() => Verify(pair.Server.ResolveDependency<IEntityManager>()));
        await pair.CleanReturnAsync();
    }
    public static void Verify(IEntityManager em)
    {
        var maps = em.System<SharedMapSystem>();
        var containers = em.System<SharedContainerSystem>();
        var cleanup = em.System<FactionBlueprintRemovalSystem>();
        var map = maps.CreateMap();
        var coords = new EntityCoordinates(map, Vector2.Zero);
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        try
        {
            var loose = em.SpawnEntity("N14BlueprintBoSWeaponsT1", coords);
            Check(em.IsQueuedForDeletion(loose), "New blueprint spawns are not retired.");
            var chest = em.SpawnEntity("CrateGenericSteel", coords);
            var bag = em.SpawnEntity("N14ClothingBackpackMilitary", coords);
            Check(containers.Insert(bag, containers.EnsureContainer<Container>(chest, "blueprint-test-chest")), "Cannot nest backpack.");
            var paper = em.SpawnEntity("Paper", coords);
            Check(containers.Insert(paper, containers.EnsureContainer<Container>(bag, "blueprint-test-bag")), "Cannot nest paper.");
            em.AddComponent<BlueprintComponent>(paper);
            var person = em.SpawnEntity("MobHuman", coords);
            var inventoryPaper = em.SpawnEntity("Paper", coords);
            Check(containers.Insert(inventoryPaper, containers.EnsureContainer<Container>(person, "blueprint-test-inventory")), "Cannot prepare carried blueprint.");
            em.AddComponent<BlueprintComponent>(inventoryPaper);
            var table = em.SpawnEntity("Table", coords);
            var tablePaper = em.SpawnEntity("Paper", coords);
            em.System<SharedTransformSystem>().SetParent(tablePaper, table);
            em.AddComponent<BlueprintComponent>(tablePaper);
            cleanup.PurgeTree(map);
            Check(!em.EntityExists(loose) && !em.EntityExists(paper) && !em.EntityExists(inventoryPaper) && !em.EntityExists(tablePaper), "Blueprint survived loose, nested, table, or living-inventory cleanup.");
            Check(em.EntityExists(chest) && em.EntityExists(bag) && em.EntityExists(person) && em.EntityExists(table), "Blueprint cleanup deleted a holder.");
        }
        finally { em.DeleteEntity(map); }
    }
}
