using System.Numerics;
using Content.Server._ReclamationProject.Institute.Relay;
using Content.Shared._ReclamationProject.Institute.Relay;
using Content.Shared.Damage;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - Institute relay pads: Gen 3 synths and Coursers get the action, see every pad,
/// relay onto one with whoever they're dragging, and relaying again inside 30 s hurts.
/// </summary>
[TestFixture]
public sealed class InstituteRelayPadTest
{
    [Test]
    public async Task SynthsRelayOntoPads()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var padSys = entities.System<InstituteRelayPadSystem>();
            var xformSys = entities.System<SharedTransformSystem>();

            var courser = entities.SpawnEntity("MobCourser", map.GridCoords);
            var synth = entities.SpawnEntity("MobGen3Synth", map.GridCoords);
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(entities.GetComponent<InstitutePadTravelerComponent>(courser).ActionEntity, Is.Not.Null, "Courser has no Relay to pad action");
            Assert.That(entities.GetComponent<InstitutePadTravelerComponent>(synth).ActionEntity, Is.Not.Null, "Gen 3 synth has no Relay to pad action");
            Assert.That(entities.HasComponent<InstitutePadTravelerComponent>(human), Is.False, "humans shouldn't get the pad action");

            // Folded kit -> pad spawns anchored.
            var padCoords = new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f));
            var pad = entities.SpawnEntity("InstituteRelayPad", padCoords);
            Assert.That(entities.GetComponent<TransformComponent>(pad).Anchored, "pad isn't anchored");
            Assert.That(entities.GetComponent<MetaDataComponent>(pad).EntityName, Does.Contain("IRP-"), "pad has no IRP number");

            var pads = padSys.GetPads(courser);
            Assert.That(pads, Has.Count.EqualTo(1));
            Assert.That(pads[0].Pad, Is.EqualTo(entities.GetNetEntity(pad)));

            // Move the Courser and the human away from the pad, and have the Courser drag the human.
            var away = new EntityCoordinates(map.Grid, new Vector2(0.5f, 6.5f));
            xformSys.SetCoordinates(courser, away);
            xformSys.SetCoordinates(human, away);
            Assert.That(entities.System<PullingSystem>().TryStartPull(courser, human), "couldn't start the pull");

            var traveler = entities.GetComponent<InstitutePadTravelerComponent>(courser);
            Assert.That(padSys.TryRelayToPad((courser, traveler), pad), "relay to pad failed");

            var padPos = xformSys.GetWorldPosition(pad);
            Assert.That((xformSys.GetWorldPosition(courser) - padPos).Length(), Is.LessThan(0.6f), "Courser didn't land on the pad");
            Assert.That((xformSys.GetWorldPosition(human) - padPos).Length(), Is.LessThan(0.6f), "the dragged human didn't come along");
            Assert.That(traveler.CoolingDown, "no cooldown started");
            Assert.That(traveler.Cooldown, Is.EqualTo(TimeSpan.FromSeconds(30)));

            // Relaying again straight away hurts.
            var before = entities.GetComponent<DamageableComponent>(courser).TotalDamage;
            Assert.That(padSys.TryRelayToPad((courser, traveler), pad));
            Assert.That(entities.GetComponent<DamageableComponent>(courser).TotalDamage, Is.GreaterThan(before),
                "relaying again inside the cooldown didn't hurt");
        });

        await pair.CleanReturnAsync();
    }
}
