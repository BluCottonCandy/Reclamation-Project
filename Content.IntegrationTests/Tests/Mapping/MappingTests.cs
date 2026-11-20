using System.Linq;
using Content.Server.Administration.Managers;
using Content.Server.Mapping;
using Content.Shared.CCVar;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Mapping;

[TestFixture]
public sealed class MappingTests
{
    [Test]
    public async Task TimedAutosaveCanReopenAPausedMap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var maps = server.System<MapSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var autosaveEnabled = config.GetCVar(CCVars.AutosaveEnabled);
        var files = server.ResolveDependency<IResourceManager>().UserData;
        const string name = nameof(TimedAutosaveCanReopenAPausedMap);
        var directory = new ResPath("/Autosaves") / name;
        MapId mapId = default;

        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out mapId, runMapInit: false);
            var interval = config.GetCVar(CCVars.AutosaveInterval);
            config.SetCVar(CCVars.AutosaveEnabled, true);
            config.SetCVar(CCVars.AutosaveInterval, 0f);
            server.System<MappingSystem>().ToggleAutosave(mapId, name);
            config.SetCVar(CCVars.AutosaveInterval, interval);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var filename = files.DirectoryEntries(directory).Single();
            Assert.That(filename, Does.EndWith("-AUTO.yml"));
            Assert.That(server.System<MapLoaderSystem>().TryLoadMap(directory / filename, out var map, out _));
            Assert.That(maps.IsInitialized(map!.Value.Owner), Is.False);
            server.System<MappingSystem>().ToggleAutosave(mapId);
            config.SetCVar(CCVars.AutosaveEnabled, autosaveEnabled);
            maps.DeleteMap(mapId);
            server.EntMan.DeleteEntity(map.Value.Owner);
            files.Delete(directory / filename);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Checks that the mapping command creates paused & uninitialized maps.
    /// </summary>
    [Test]
    public async Task MappingTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, Connected = true, DummyTicker = false });

        var server = pair.Server;
        var entMan = server.EntMan;
        var mapSys = server.System<MapSystem>();

        var config = server.ResolveDependency<IConfigurationManager>();
        var admins = server.ResolveDependency<IAdminManager>();
        var hostName = config.GetCVar(CCVars.ConsoleLoginHostUser);
        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.ConsoleLoginHostUser, pair.Player!.Name);
            admins.ReloadAdmin(pair.Player);
        });
        await pair.RunTicksSync(5);
        var mapId = 1;
        while (mapSys.MapExists(new(mapId)))
        {
            mapId++;
        }

        await pair.WaitClientCommand($"mapping {mapId}");
        var map = mapSys.GetMap(new MapId(mapId));

        var mapXform = server.Transform(map);
        Assert.That(mapXform.MapUid, Is.EqualTo(map));
        Assert.That(mapXform.MapID, Is.EqualTo(new MapId(mapId)));

        var xform = server.Transform(pair.Player!.AttachedEntity!.Value);

        Assert.That(xform.MapUid, Is.EqualTo(map));
        Assert.That(mapSys.IsInitialized(map), Is.False);
        Assert.That(mapSys.IsPaused(map), Is.True);
        Assert.That(server.MetaData(map).EntityLifeStage, Is.EqualTo(EntityLifeStage.Initialized));
        Assert.That(server.MetaData(map).EntityPaused, Is.True);

        // Spawn a new entity
        EntityUid ent = default;
        await server.WaitPost(() =>
        {
            ent = entMan.Spawn(null, new MapCoordinates(default, new(mapId)));
        });
        await pair.RunTicksSync(5);
        Assert.That(server.MetaData(ent).EntityLifeStage, Is.EqualTo(EntityLifeStage.Initialized));
        Assert.That(server.MetaData(ent).EntityPaused, Is.True);

        // Save the map
        var file = $"{nameof(MappingTest)}.yml";
        await pair.WaitClientCommand($"savemap {mapId} {file}");

        // Mapinitialize it
        await pair.WaitClientCommand($"mapinit {mapId}");
        Assert.That(mapSys.IsInitialized(map), Is.True);
        Assert.That(mapSys.IsPaused(map), Is.False);
        Assert.That(server.MetaData(map).EntityLifeStage, Is.EqualTo(EntityLifeStage.MapInitialized));
        Assert.That(server.MetaData(map).EntityPaused, Is.False);
        Assert.That(server.MetaData(ent).EntityLifeStage, Is.EqualTo(EntityLifeStage.MapInitialized));
        Assert.That(server.MetaData(ent).EntityPaused, Is.False);

        await server.WaitPost(() => entMan.DeleteEntity(map));

        // Load the saved map
        mapId++;
        while (mapSys.MapExists(new(mapId)))
        {
            mapId++;
        }

        await pair.WaitClientCommand($"mapping {mapId} {file}");
        map = mapSys.GetMap(new MapId(mapId));

        // And it should all be paused and un-initialized
        xform = server.Transform(pair.Player!.AttachedEntity!.Value);
        Assert.That(xform.MapUid, Is.EqualTo(map));
        Assert.That(mapSys.IsInitialized(map), Is.False);
        Assert.That(mapSys.IsPaused(map), Is.True);
        Assert.That(server.MetaData(map).EntityLifeStage, Is.EqualTo(EntityLifeStage.Initialized));
        Assert.That(server.MetaData(map).EntityPaused, Is.True);

        mapXform = server.Transform(map);
        Assert.That(mapXform.MapUid, Is.EqualTo(map));
        Assert.That(mapXform.MapID, Is.EqualTo(new MapId(mapId)));
        Assert.That(mapXform.ChildCount, Is.EqualTo(2));

        mapXform.ChildEnumerator.MoveNext(out ent);
        if (ent == pair.Player.AttachedEntity)
            mapXform.ChildEnumerator.MoveNext(out ent);

        Assert.That(server.MetaData(ent).EntityLifeStage, Is.EqualTo(EntityLifeStage.Initialized));
        Assert.That(server.MetaData(ent).EntityPaused, Is.True);

        await server.WaitPost(() => entMan.DeleteEntity(map));
        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            config.SetCVar(CCVars.ConsoleLoginHostUser, hostName);
            admins.ReloadAdmin(pair.Player!);
        });
        await pair.CleanReturnAsync();
    }
}
