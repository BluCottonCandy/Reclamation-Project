using Content.Shared.Inventory;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared._ReclamationProject.Blindness;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class BlindConcentrationTests
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestBlindListener
  parent: MobHuman
  components:
  - type: PermanentBlindness
  - type: BlindConcentration

- type: entity
  id: TestBlindWithoutPerk
  parent: MobHuman
  components:
  - type: PermanentBlindness
";

    [Test]
    public async Task SneakGunfireRecordsPositionWithoutFollowingShooter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        await pair.Server.WaitAssertion(() =>
        {
            var shooter = entities.SpawnEntity("TestBlindWithoutPerk", map.GridCoords);
            var quiet = entities.AddComponent<QuietAwarenessComponent>(shooter);
            var gun = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(gun);
            var shot = new Content.Shared.Weapons.Ranged.Systems.GunShotEvent(shooter, new());
            entities.EventBus.RaiseLocalEvent(gun, ref shot);
            Assert.That(quiet.LastShotAt, Is.EqualTo(timing.CurTime));
            var origin = quiet.LastShotCoordinates;
            entities.System<SharedTransformSystem>().SetLocalPosition(shooter,
                entities.GetComponent<TransformComponent>(shooter).LocalPosition + new System.Numerics.Vector2(5, 0));
            Assert.That(quiet.LastShotCoordinates, Is.EqualTo(origin));
            Assert.That(quiet.LastShotCoordinates, Is.Not.EqualTo(entities.GetComponent<TransformComponent>(shooter).Coordinates));
            entities.DeleteEntity(shooter);
            entities.DeleteEntity(gun);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BlindnessTraitPlacesOneBlindfoldInBackpack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid wearer = default, backpack = default;
        await pair.Server.WaitAssertion(() =>
        {
            wearer = entities.SpawnEntity("TestBlindWithoutPerk", map.GridCoords);
            backpack = entities.SpawnEntity("ClothingBackpack", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(wearer, backpack, "back", force: true), Is.True);
            entities.AddComponent<BlindConcentrationComponent>(wearer);
        });
        await pair.RunTicksSync(3);
        await pair.Server.WaitAssertion(() =>
        {
            var contents = entities.GetComponent<Content.Shared.Storage.StorageComponent>(backpack).Container.ContainedEntities;
            Assert.That(contents.Count, Is.EqualTo(1));
            Assert.That(entities.GetComponent<MetaDataComponent>(contents[0]).EntityPrototype!.ID,
                Is.EqualTo("ClothingEyesBlindfold"));
            entities.DeleteEntity(wearer);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WornBlindfoldProvidesPassivePerceptionOnlyWithPerk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        await pair.Server.WaitAssertion(() =>
        {
            var wearer = entities.SpawnEntity("TestBlindListener", map.GridCoords);
            var normal = entities.SpawnEntity("TestBlindWithoutPerk", map.GridCoords.Offset(new System.Numerics.Vector2(3, 0)));
            var fold = entities.SpawnEntity("ClothingEyesBlindfold", map.GridCoords);
            var inventory = entities.System<InventorySystem>();
            var awareness = entities.System<BlindAwarenessSystem>();
            Assert.That(awareness.GetRadiusMultiplier(wearer, timing.CurTime), Is.EqualTo(1f));
            Assert.That(inventory.TryEquip(wearer, fold, "eyes", force: true), Is.True);
            Assert.That(awareness.GetRadiusMultiplier(wearer, timing.CurTime), Is.EqualTo(3f));
            Assert.That(entities.GetComponent<BlindConcentrationComponent>(wearer).Concentrating, Is.False);
            Assert.That(inventory.TryUnequip(wearer, "eyes", force: true), Is.True);
            Assert.That(awareness.GetRadiusMultiplier(wearer, timing.CurTime), Is.EqualTo(1f));
            Assert.That(inventory.TryEquip(normal, fold, "eyes", force: true), Is.True);
            Assert.That(awareness.GetRadiusMultiplier(normal, timing.CurTime), Is.EqualTo(1f));
            entities.DeleteEntity(wearer);
            entities.DeleteEntity(normal);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ConcentrationBuildsHoldsAndFadesOnMovement(bool eyesClosed)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        EntityUid listener = default;
        await pair.Server.WaitAssertion(() =>
        {
            listener = entities.SpawnEntity("TestBlindListener", map.GridCoords);
            var normal = entities.SpawnEntity("TestBlindWithoutPerk", map.GridCoords.Offset(new System.Numerics.Vector2(3, 0)));
            Assert.That(entities.HasComponent<BlindConcentrationComponent>(normal), Is.False);
            var focus = entities.GetComponent<BlindConcentrationComponent>(listener);
            Assert.That(focus.Action, Is.Not.Null);
            if (eyesClosed)
                entities.System<EyeClosingSystem>().SetEyelids((listener, null), true);
            var ev = new BlindConcentrateActionEvent { Performer = listener };
            entities.EventBus.RaiseLocalEvent(listener, ev);
            Assert.That(ev.Handled, Is.True);
            Assert.That(focus.Concentrating, Is.True);
            Assert.That(focus.DoAfter, Is.Not.Null);
            Assert.That(focus.ClosedEyeBonus, Is.EqualTo(eyesClosed));
            Assert.That(focus.MaximumRadiusMultiplier, Is.EqualTo(eyesClosed ? 6f : 3f));
            entities.DeleteEntity(normal);
        });
        await pair.RunTicksSync((int) (timing.TickRate * (eyesClosed ? 3.5f : 4f)));
        await pair.Server.WaitAssertion(() =>
        {
            var focus = entities.GetComponent<BlindConcentrationComponent>(listener);
            Assert.That(focus.Progress(timing.CurTime), Is.InRange(0.4f, 0.6f));
            var before = focus.Progress(timing.CurTime);
            entities.EventBus.RaiseLocalEvent(listener, new ToggleEyesActionEvent { Performer = listener });
            Assert.That(focus.ClosedEyeBonus, Is.EqualTo(!eyesClosed));
            Assert.That(focus.Progress(timing.CurTime), Is.EqualTo(before).Within(0.001f));
            entities.EventBus.RaiseLocalEvent(listener, new ToggleEyesActionEvent { Performer = listener });
            Assert.That(focus.ClosedEyeBonus, Is.EqualTo(eyesClosed));
            Assert.That(focus.DoAfter, Is.Not.Null);

        });
        await pair.RunTicksSync(timing.TickRate * 5);
        await pair.Server.WaitAssertion(() =>
        {
            var focus = entities.GetComponent<BlindConcentrationComponent>(listener);
            Assert.That(focus.Progress(timing.CurTime), Is.EqualTo(1f));
            Assert.That(focus.Concentrating, Is.True);
            Assert.That(focus.DoAfter, Is.Null);
            entities.System<SharedTransformSystem>().SetCoordinates(listener, map.GridCoords.Offset(new System.Numerics.Vector2(1, 0)));
            Assert.That(focus.Concentrating, Is.False);
            Assert.That(focus.Progress(timing.CurTime), Is.EqualTo(1f));
        });
        await pair.RunTicksSync(timing.TickRate * (eyesClosed ? 3 : 2));
        await pair.Server.WaitAssertion(() =>
        {
            var focus = entities.GetComponent<BlindConcentrationComponent>(listener);
            Assert.That(focus.Progress(timing.CurTime), Is.InRange(0.4f, 0.6f));
        });
        await pair.RunTicksSync(timing.TickRate * 4);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<BlindConcentrationComponent>(listener).Progress(timing.CurTime), Is.Zero);
            entities.DeleteEntity(listener);
        });
        await pair.CleanReturnAsync();
    }
}
