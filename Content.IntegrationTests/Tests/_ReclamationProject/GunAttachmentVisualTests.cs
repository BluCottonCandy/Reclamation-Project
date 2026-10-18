using System.Linq;
using System.Numerics;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Maths;
using Content.Client._ReclamationProject.GunAttachments;
using Content.Shared._ReclamationProject.GunAttachments;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands;
using Content.Shared.Verbs;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class GunAttachmentVisualTests
{
    [TestCase("ReclamationGunSuppressor", "gun_muzzle", 0, "N14WeaponSMG10mm")]
    [TestCase("N14CombatKnife", "gun_muzzle", 0, "N14WeaponRifle556R91")]
    [TestCase("ReclamationGunGrip", "gun_grip", 1, "N14WeaponSMG10mm")]
    [TestCase("ReclamationGunBipod", "gun_grip", 1, "N14WeaponRifle556R91")]
    [TestCase("ReclamationGunLight", "gun_light", 2, "N14WeaponSMG10mm")]
    [TestCase("ReclamationGunStock", "gun_stock", 3, "N14WeaponSMG10mm")]
    [TestCase("ReclamationGunLaserSight", "gun_laser", 4, "N14WeaponSMG10mm")]
    [TestCase("ReclamationGunHoloSight", "gun_holo", 5, "N14WeaponSMG10mm")]
    public async Task MountsAndRemovesReplicatedSpritesAndAddsExamineButton(string attachment, string slot, int layer, string rifle)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        EntityUid gun = default;
        EntityUid item = default;
        NetEntity netGun = default;
        await pair.Server.WaitAssertion(() =>
        {
            gun = server.SpawnEntity(rifle, map.GridCoords);
            item = server.SpawnEntity(attachment, map.GridCoords);
            Assert.That(server.System<ItemSlotsSystem>().TryInsert(gun, slot, item, null), Is.True);
            netGun = server.GetNetEntity(gun);
            foreach (var target in new[] { gun, item })
            {
                var ev = new GetVerbsEvent<ExamineVerb>(gun, target, null, null, true, true, true, new());
                server.EventBus.RaiseLocalEvent(target, ev, true);
                Assert.That(ev.Verbs.Any(verb => verb.Text == "Attachments"), Is.True);
            }
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var localGun = client.GetEntity(netGun);
            client.System<GunAttachmentVisualSystem>().FrameUpdate(0.2f);
            var sprite = client.GetComponent<SpriteComponent>(localGun);
            Assert.That(sprite.LayerMapTryGet($"gun-attachment-visual-{layer}", out var index), Is.True);
            Assert.That(sprite[index].Visible, Is.True);
            var held = new GetInhandVisualsEvent(localGun, Content.Shared.Hands.Components.HandLocation.Right);
            client.EventBus.RaiseLocalEvent(localGun, held);
            Assert.That(held.Layers.Any(entry => entry.Item1.StartsWith($"gun-attachment-visual-{layer}")), Is.True);
        });
        await pair.Server.WaitAssertion(() =>
            Assert.That(server.System<ItemSlotsSystem>().TryEject(gun, slot, null, out _), Is.True));
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var localGun = client.GetEntity(netGun);
            client.System<GunAttachmentVisualSystem>().FrameUpdate(0.2f);
            var sprite = client.GetComponent<SpriteComponent>(localGun);
            Assert.That(sprite.LayerMapTryGet($"gun-attachment-visual-{layer}", out var index), Is.True);
            Assert.That(sprite[index].Visible, Is.False);
        });
        await pair.Server.WaitPost(() => { server.DeleteEntity(gun); server.DeleteEntity(item); });
        await pair.CleanReturnAsync();
    }
    [TestCase("N14WeaponRifle556R91")]
    [TestCase("N14WeaponSMG10mm")]
    [TestCase("N14WeaponLaserRifle")]
    [TestCase("N14WeaponLaserRifleAuto")]
    [TestCase("N14WeaponSMG12mm")]
    [TestCase("N14WeaponSMGGreaseGun")]
    [TestCase("N14WeaponSMG45")]
    [TestCase("N14WeaponSMG12mmAdvancedChinese")]
    [TestCase("N14WeaponSMG12mmAdvancedCanadian")]
    [TestCase("N14WeaponSMG12mmPipe")]
    [TestCase("N14WeaponRifle556Pipe")]

    [TestCase("N14WeaponSniperHunting")]
    [TestCase("N14WeaponPistol10mm")]
    [TestCase("N14WeaponShotgun")]
    [TestCase("N14WeaponSMG45", true)]
    [TestCase("N14WeaponSMGGreaseGun", true)]
    [TestCase("N14WeaponSMG12mmAdvancedChinese", true)]
    [TestCase("N14WeaponSMG12mmAdvancedCanadian", true)]
    public async Task HeldLaserStaysFlushWithMuzzleInEveryDirectionAndWieldedPose(string prototype, bool leftHand = false)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        EntityUid user = default, gun = default;
        NetEntity netGun = default, netUser = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = server.SpawnEntity("MobHuman", map.GridCoords);
            gun = server.SpawnEntity(prototype, map.GridCoords);
            var laser = server.SpawnEntity("ReclamationGunLaserSight", map.GridCoords);
            Assert.That(server.System<ItemSlotsSystem>().TryInsert(gun, GunAttachmentsSystem.LaserSlotId, laser, null), Is.True);
            var hands = server.GetComponent<Content.Shared.Hands.Components.HandsComponent>(user);
            var location = leftHand ? Content.Shared.Hands.Components.HandLocation.Left : Content.Shared.Hands.Components.HandLocation.Right;
            var handName = hands.Hands.Values.First(hand => hand.Location == location).Name;
            server.System<SharedHandsSystem>().TrySetActiveHand(user, handName, hands);
            Assert.That(hands.ActiveHand?.Location, Is.EqualTo(location));
            Assert.That(server.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
            netGun = server.GetNetEntity(gun); netUser = server.GetNetEntity(user);
        });
        for (var pose = 0; pose < 2; pose++)
        {
            if (pose == 1)
                await pair.Server.WaitPost(() =>
                {
                    if (server.TryGetComponent<WieldableComponent>(gun, out var wield))
                        server.System<WieldableSystem>().TryWield(gun, wield, user);
                });
            await pair.RunTicksSync(20);
            await pair.Client.WaitAssertion(() =>
            {
                var actor = client.GetEntity(netUser);
                var localGun = client.GetEntity(netGun);
                var system = client.System<GunAttachmentVisualSystem>();
                var sprite = client.GetComponent<SpriteComponent>(actor);
                foreach (var forced in new[] { false, true })
                foreach (var degrees in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
                {
                    var angle = Angle.FromDegrees(degrees);
                    client.System<SharedTransformSystem>().SetLocalRotation(actor, angle);
                    // Humanoid animation normally supplies this override during a rendered frame.
                    sprite.EnableDirectionOverride = forced;
                    sprite.DirectionOverride = Direction.East;
                    system.FrameUpdate(.016f);
                    var main = sprite.AllLayers.OfType<SpriteComponent.Layer>()
                        .First(layer => layer.Visible && layer.ActualRsi?.Path.ToString().Contains("/Guns/") == true &&
                            !layer.ActualRsi.Path.ToString().Contains("GunAttachments"));
                    if (!system.TryGetMount(main, angle, out var mount, sprite.EnableDirectionOverride ? sprite.DirectionOverride : null))
                    {
                        Assert.That(sprite.AllLayers.OfType<SpriteComponent.Layer>()
                            .Any(layer => layer.Visible && layer.ActualRsi?.Path.ToString().EndsWith("/laser.rsi") == true),
                            Is.False, "Do not expose floating attachments when the muzzle is hidden behind the body.");
                        continue;
                    }
                    var laser = sprite.AllLayers.OfType<SpriteComponent.Layer>()
                        .First(layer => layer.Visible && layer.ActualRsi?.Path.ToString().EndsWith("/laser.rsi") == true);
                    main.GetLayerDrawMatrix(SpriteComponent.Layer.GetDirection(main.ActualState!.RsiDirections, angle), out var baseMatrix);
                    var expected = Vector2.Transform(mount.Muzzle + mount.Down * (2f / 32f), baseMatrix);
                    var forward = Vector2.Normalize(Vector2.TransformNormal(mount.Forward, baseMatrix));
                    var laserForward = Vector2.Normalize(Vector2.TransformNormal(Vector2.UnitX, laser.LocalMatrix));
                    Assert.That(Vector2.Dot(forward, laserForward), Is.GreaterThan(.999f),
                        "A mirrored attachment must still point out of the barrel.");
                    if (sprite.NoRotation && Math.Abs(sprite.Rotation.Theta) < .001 &&
                        (forced || degrees is 90 or 270))
                        Assert.That(forward.X * (forced || degrees == 90 ? 1 : -1), Is.GreaterThan(0),
                            "The held barrel and attachments must face the same horizontal direction as the player.");
                    // The laser body's front pivot, excluding its red beam and transparent padding.
                    var actual = Vector2.Transform(new Vector2(1, 7) / 32f, laser.LocalMatrix);
                    Assert.That(Vector2.Distance(expected, actual), Is.LessThan(.001f), $"{prototype}, pose {pose}, facing {degrees}; expected {expected}, actual {actual}; base {main.State}, base matrix {baseMatrix}; laser matrix {laser.LocalMatrix}; eye {pair.Client.ResolveDependency<Robust.Client.Graphics.IEyeManager>().CurrentEye.Rotation}; override {sprite.DirectionOverride}; geometry {mount}");
                }
            });
        }
        await pair.Server.WaitPost(() => server.DeleteEntity(user));
        await pair.CleanReturnAsync();
    }

    [TestCase("N14WeaponSMG10mm")]
    [TestCase("N14WeaponPistol10mm")]
    public async Task OneHandedGroundSpritesPutMuzzleAtRightAndStockAtLeft(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        EntityUid gun = default;
        NetEntity net = default;
        await pair.Server.WaitPost(() => { gun = server.SpawnEntity(prototype, map.GridCoords); net = server.GetNetEntity(gun); });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var sprite = client.GetComponent<SpriteComponent>(client.GetEntity(net));
            var layer = sprite.AllLayers.OfType<SpriteComponent.Layer>().First(layer => layer.Visible);
            Assert.That(client.System<GunAttachmentVisualSystem>().TryGetMount(layer, Angle.Zero, out var mount), Is.True);
            Assert.That(mount.Muzzle.X, Is.GreaterThan(mount.Rear.X));
            Assert.That(mount.Muzzle.Y, Is.EqualTo(mount.Rear.Y));
        });
        await pair.Server.WaitPost(() => server.DeleteEntity(gun));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeployedBipodAimsSidewaysAlongProneBodyAndRestoresFoldedPose()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        EntityUid user = default;
        NetEntity netGun = default, netUser = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = server.SpawnEntity("MobHuman", map.GridCoords);
            var gun = server.SpawnEntity("N14WeaponRifle556R91", map.GridCoords);
            var bipod = server.SpawnEntity("ReclamationGunBipod", map.GridCoords);
            Assert.That(server.System<ItemSlotsSystem>().TryInsert(gun, GunAttachmentsSystem.GripSlotId, bipod, null), Is.True);
            Assert.That(server.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
            netGun = server.GetNetEntity(gun); netUser = server.GetNetEntity(user);
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var actor = client.GetEntity(netUser);
            var gun = client.GetEntity(netGun);
            var sprite = client.GetComponent<SpriteComponent>(actor);
            var component = client.GetComponent<GunAttachmentsComponent>(gun);
            var system = client.System<GunAttachmentVisualSystem>();
            SpriteComponent.Layer Main() => sprite.AllLayers.OfType<SpriteComponent.Layer>()
                .First(layer => layer.Visible && layer.ActualRsi?.Path.ToString().Contains("/Guns/Rifles/") == true);
            system.FrameUpdate(.2f);
            var originalOffset = Main().Offset;
            var originalRotation = Main().Rotation;
            var originalBodyRotation = sprite.Rotation;
            var originalScale = Main().Scale;
            foreach (var degrees in new[] { 90, 270 })
            {
                sprite.Rotation = Angle.FromDegrees(degrees);
                component.BipodDeployed = true;
                system.FrameUpdate(.2f);
                var main = Main();
                Assert.That(system.TryGetMount(main, Angle.Zero, out var mount,
                    sprite.EnableDirectionOverride ? sprite.DirectionOverride : null), Is.True);
                var forward = sprite.Rotation.RotateVec(Vector2.Normalize(Vector2.TransformNormal(mount.Forward, main.LocalMatrix)));
                Assert.That(Math.Abs(forward.Y), Is.LessThan(.0001f));
                Assert.That(forward.X, Is.EqualTo(degrees == 90 ? -1f : 1f).Within(.0001f));
                var down = sprite.Rotation.RotateVec(Vector2.Normalize(Vector2.TransformNormal(mount.Down, main.LocalMatrix)));
                Assert.That(down.Y, Is.LessThan(-.99f), "Magazine and underbarrel must stay below the barrel facing either way.");
                var rear = Vector2.Transform(mount.Rear, main.LocalMatrix);
                Assert.That(Vector2.Distance(rear, new Vector2(degrees == 90 ? -.2f : .2f, 0)), Is.LessThan(.0001f));
                Assert.That(sprite.Rotation.RotateVec(rear).Y, Is.LessThan(0), "The deployed rifle must sit below the player on screen.");
                component.BipodDeployed = false;
                system.FrameUpdate(.2f);
                Assert.That(Main().Offset, Is.EqualTo(originalOffset));
                Assert.That(Main().Rotation, Is.EqualTo(originalRotation));
                Assert.That(Main().Scale, Is.EqualTo(originalScale));
            }
            sprite.Rotation = originalBodyRotation;
        });
        await pair.Server.WaitPost(() => server.DeleteEntity(user));
        await pair.CleanReturnAsync();
    }

}
