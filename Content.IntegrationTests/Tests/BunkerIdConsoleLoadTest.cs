using System.IO;
using Content.Server._Reclamation.Bunkers;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests;

/// <summary>
/// #Reclamation Project - a bunker save turns IDs into decorations (IdCard/Access stripped).
/// An access terminal holding one of those must still load instead of breaking the whole room.
/// </summary>
[TestFixture]
public sealed class BunkerIdConsoleLoadTest
{
    [Test]
    public async Task TerminalWithDecorativeIdLoads()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var maps = entMan.System<SharedMapSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var slots = entMan.System<ItemSlotsSystem>();
        var displays = entMan.System<BunkerDisplayWeaponSystem>();

        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var grid = maps.CreateGridEntity(mapId);
            maps.SetTile(grid, Vector2i.Zero, new Tile(1));
            var coords = new EntityCoordinates(grid.Owner, 0.5f, 0.5f);

            var terminal = entMan.SpawnEntity("N14ComputerIDAccess", coords);
            var card = entMan.SpawnEntity("PassengerIDCard", coords);
            var console = entMan.GetComponent<IdCardConsoleComponent>(terminal);
            Assert.That(slots.TryInsert(terminal, console.TargetIdSlot, card, null), "card should go into the target slot");

            // Same call the bunker makes when it saves a vacant room.
            displays.Decommission(card);
            Assert.That(entMan.HasComponent<IdCardComponent>(card), Is.False, "card should now be decorative");

            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(grid.Owner, writer), "grid should save");

            using var reader = new StringReader(writer.ToString());
            Entity<MapComponent>? loadedMap = null;
            Entity<MapGridComponent>? loadedGrid = null;
            Assert.DoesNotThrow(() => loader.TryLoadGrid(reader, "bunker-id-console-test", out loadedMap, out loadedGrid,
                new DeserializationOptions { InitializeMaps = true }));
            Assert.That(loadedGrid, Is.Not.Null, "grid should load");

            entMan.DeleteEntity(loadedMap?.Owner);
            maps.DeleteMap(mapId);
        });

        await pair.CleanReturnAsync();
    }
}
