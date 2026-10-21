using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.Server._ReclamationProject.Roofs;
using Content.Server.Construction;
using Content.Shared._ReclamationProject.Roofs;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class WoodenRoofTests
{
    private static EntityCoordinates At(TestMapData map, int x, int y = 0) => new(map.Grid, new Vector2(x + .5f, y + .5f));

    private static void Floor(IEntityManager em, TestMapData map, int x, int y = 0, string id = "FloorWoodOak")
    {
        var tiles = IoCManager.Resolve<ITileDefinitionManager>();
        em.System<SharedMapSystem>().SetTile(map.Grid, At(map, x, y), new Tile(tiles[id].TileId));
    }

    private static int Wood(IEntityManager em)
    {
        var count = 0;
        var query = em.EntityQueryEnumerator<StackComponent>();
        while (query.MoveNext(out _, out var stack))
            if (stack.StackTypeId.Id == "WoodPlank") count += stack.Count;
        return count;
    }

    [Test]
    public async Task ActualConstructionConsumesExactlyTwoWood()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        Task<bool> building = null!;
        await pair.Server.WaitAssertion(() =>
        {
            Floor(em, map, 0);
            Floor(em, map, 1);
            var user = em.SpawnEntity("MobHuman", At(map, 0));
            em.AddComponent<SpecialComponent>(user);
            var wood = em.SpawnEntity("MaterialWoodPlank10", At(map, 0));
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(user, wood), Is.True);
            em.SpawnEntity("WoodenSupportBeam", At(map, 0));
            building = em.System<ConstructionSystem>().TryStartStructureConstruction(user, "ReclamationWoodenRoof", At(map, 1), Angle.Zero);
        });
        await pair.RunSeconds(6);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(building.IsCompletedSuccessfully, Is.True);
            Assert.That(building.Result, Is.True);
            Assert.That(Wood(em), Is.EqualTo(8));
            var count = 0;
            var query = em.EntityQueryEnumerator<ConstructedRoofComponent>();
            while (query.MoveNext(out _, out _)) count++;
            Assert.That(count, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GMenuRecipeCostsTwoWoodRequiresFloorAndPreventsDuplicateRoofs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var recipe = prototypes.Index<ConstructionPrototype>("ReclamationWoodenRoof");
            Assert.That(recipe.Hide, Is.False);
            Assert.That(recipe.PlacementMode, Is.EqualTo("SnapgridCenter"));
            var step = prototypes.Index(recipe.Graph).Nodes["start"].Edges.Single().Steps.Single();
            Assert.That(step, Is.TypeOf<MaterialConstructionGraphStep>());
            Assert.That(((MaterialConstructionGraphStep) step).MaterialPrototypeId.Id, Is.EqualTo("WoodPlank"));
            Assert.That(((MaterialConstructionGraphStep) step).Amount, Is.EqualTo(2));
            var condition = recipe.Conditions.OfType<RoofFloorCondition>().Single();
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(condition.Condition(user, At(map, 3), Direction.South), Is.False);
            Floor(em, map, 0, id: "FloorWasteland");
            Assert.That(condition.Condition(user, At(map, 0), Direction.South), Is.False);
            Floor(em, map, 0);
            Assert.That(condition.Condition(user, At(map, 0), Direction.South), Is.True);
            var roof = em.SpawnEntity("ReclamationWoodenRoof", At(map, 0));
            Assert.That(condition.Condition(user, At(map, 0), Direction.South), Is.False);
            Assert.That(em.System<SharedRoofSystem>().IsRooved((map.Grid.Owner, map.Grid.Comp, em.GetComponent<RoofComponent>(map.Grid)), new Vector2i(0, 0)), Is.True);
            Assert.That(em.GetComponent<ConstructedRoofComponent>(roof).SupportGrace, Is.EqualTo(TimeSpan.FromSeconds(30)));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OneBeamHoldsWholeSectionButIsolatedAndDiagonalTilesCollapseAfterThirtySeconds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid[] linked = [];
        EntityUid isolated = default, diagonal = default;
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = -1; x <= 5; x++) Floor(em, map, x);
            Floor(em, map, 3, 1);
            em.SpawnEntity("WoodenSupportBeam", At(map, -1));
            linked = Enumerable.Range(0, 3).Select(x => em.SpawnEntity("ReclamationWoodenRoof", At(map, x))).ToArray();
            isolated = em.SpawnEntity("ReclamationWoodenRoof", At(map, 5));
            diagonal = em.SpawnEntity("ReclamationWoodenRoof", At(map, 3, 1));
            em.System<ConstructedRoofSystem>().CheckSupport();
            Assert.That(linked.All(uid => em.GetComponent<ConstructedRoofComponent>(uid).CollapseAt == null), Is.True);
        });
        await pair.RunSeconds(29);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(isolated) && em.EntityExists(diagonal), Is.True);
            Assert.That(Wood(em), Is.Zero);
        });
        await pair.RunSeconds(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(isolated) || em.EntityExists(diagonal), Is.False);
            Assert.That(linked.All(em.EntityExists), Is.True);
            Assert.That(Wood(em), Is.EqualTo(4));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LosingAndRestoringSupportResetsTheGracePeriod()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            Floor(em, map, 0);
            Floor(em, map, -1);
            var roof = em.SpawnEntity("ReclamationWoodenRoof", At(map, 0));
            var beam = em.SpawnEntity("WoodenSupport", At(map, -1));
            var system = em.System<ConstructedRoofSystem>();
            var component = em.GetComponent<ConstructedRoofComponent>(roof);
            system.CheckSupport();
            Assert.That(component.CollapseAt, Is.Null);
            em.DeleteEntity(beam);
            system.CheckSupport();
            var now = pair.Server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(component.CollapseAt, Is.EqualTo(now + TimeSpan.FromSeconds(30)));
            system.CheckSupport();
            Assert.That(component.CollapseAt, Is.EqualTo(now + TimeSpan.FromSeconds(30)), "Checks do not postpone an unsupported roof indefinitely.");
            beam = em.SpawnEntity("WoodenSupportBeam", At(map, 0));
            system.CheckSupport();
            Assert.That(component.CollapseAt, Is.Null, "Same-tile support also holds a roof.");
            em.System<SharedTransformSystem>().Unanchor(beam);
            system.CheckSupport();
            Assert.That(component.CollapseAt, Is.EqualTo(now + TimeSpan.FromSeconds(30)), "Unanchored beams do not support a roof.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PryingTheFloorRemovesOnlyItsRoofAndRefundsWoodOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid roof = default, neighbor = default;
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = -1; x <= 1; x++) Floor(em, map, x);
            em.SpawnEntity("WoodenSupportBeam", At(map, -1));
            roof = em.SpawnEntity("ReclamationWoodenRoof", At(map, 0));
            neighbor = em.SpawnEntity("ReclamationWoodenRoof", At(map, 1));
            var system = em.System<ConstructedRoofSystem>();
            system.CheckSupport();
            Assert.That(em.System<TileSystem>().PryTile(new Vector2i(0, 0), map.Grid), Is.True);
            Assert.That(em.GetComponent<ConstructedRoofComponent>(roof).Removing, Is.True);
            Assert.That(em.GetComponent<IsRoofComponent>(roof).Enabled, Is.False);
            system.CheckSupport();
            Assert.That(Wood(em), Is.EqualTo(2));
            Assert.That(em.GetComponent<ConstructedRoofComponent>(neighbor).CollapseAt, Is.Not.Null,
                "Removing the bridge leaves the remaining section unsupported.");
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(roof), Is.False);
            Assert.That(em.EntityExists(neighbor), Is.True);
            Assert.That(Wood(em), Is.EqualTo(2));
        });
        await pair.CleanReturnAsync();
    }
}
