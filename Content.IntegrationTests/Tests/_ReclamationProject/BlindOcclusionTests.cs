using Content.Client.Eye.Blinding;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class BlindOcclusionTests
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestBlindOcclusionListener
  parent: MobHuman
  components:
  - type: PermanentBlindness
  - type: BlindConcentration
";

    [Test]
    public async Task HearingKeepsWallOcclusionAndRestoresLightingOnDetach()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid listener = default;
        NetEntity netListener = default;
        await pair.Server.WaitPost(() =>
        {
            listener = server.SpawnEntity("TestBlindOcclusionListener", map.GridCoords);
            var blindfold = server.SpawnEntity("ClothingEyesBlindfold", map.GridCoords);
            Assert.That(server.System<Content.Shared.Inventory.InventorySystem>().TryEquip(listener, blindfold, "eyes", force: true), Is.True);
            netListener = server.GetNetEntity(listener);
            pair.Server.PlayerMan.SetAttachedEntity(session, listener);
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var uid = client.GetEntity(netListener);
            Assert.That(client.GetComponent<Content.Shared.Eye.Blinding.Components.BlindableComponent>(uid).IsBlind, Is.True);
            client.System<BlindingSystem>().FrameUpdate(0f);
            var lighting = pair.Client.ResolveDependency<ILightManager>();
            // These are the renderer's prerequisites for its wall-occlusion pass.
            Assert.That(lighting.Enabled, Is.True);
            Assert.That(lighting.DrawHardFov, Is.True);
            Assert.That(lighting.DrawLighting, Is.False);
            var eye = client.GetComponent<EyeComponent>(uid).Eye;
            Assert.That(eye.DrawFov, Is.True);
            Assert.That(eye.DrawLight, Is.True);
        });
        await pair.Server.WaitPost(() => pair.Server.PlayerMan.SetAttachedEntity(session, null));
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            client.System<BlindingSystem>().FrameUpdate(0f);
            var lighting = pair.Client.ResolveDependency<ILightManager>();
            Assert.That(lighting.Enabled, Is.True);
            Assert.That(lighting.DrawLighting, Is.True);
        });
        await pair.Server.WaitPost(() => server.DeleteEntity(listener));
        await pair.CleanReturnAsync();
    }
}
