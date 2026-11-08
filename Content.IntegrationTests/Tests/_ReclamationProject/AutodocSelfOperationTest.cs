using System.Collections.Generic;
using Content.Shared._Shitmed.Autodoc;
using Content.Shared._Shitmed.Autodoc.Components;
using Content.Shared._Shitmed.Autodoc.Systems;
using Content.Shared.Body.Part;
using Content.Shared.Buckle;
using Content.Shared.DeviceLinking;
using Content.Shared.Hands.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - with the safety on, the Autodoc still refuses to cut into an awake patient,
/// unless that patient started the program on themselves.
/// </summary>
[TestFixture]
public sealed class AutodocSelfOperationTest
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task AwakePatientCanOnlyOperateOnThemselves(bool selfStarted)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var autodocSys = entities.System<SharedAutodocSystem>();
            var table = entities.SpawnEntity("OperatingTable", map.GridCoords);
            var autodoc = entities.SpawnEntity("Autodoc", map.GridCoords.Offset(new System.Numerics.Vector2(1, 0))); // next to the bed, like in game
            var patient = entities.SpawnEntity("MobHuman", map.GridCoords);
            var doctor = entities.SpawnEntity("MobHuman", map.GridCoords);

            entities.System<SharedDeviceLinkSystem>().SaveLinks(null, table, autodoc,
                new List<(string, string)> { ("OperatingTable", "OperatingTableReceiver") });
            Assert.That(entities.System<SharedBuckleSystem>().TryBuckle(patient, patient, table, popup: false), "patient couldn't lie on the table");

            var comp = entities.GetComponent<AutodocComponent>(autodoc);
            Assert.That(comp.RequireSleeping, Is.True, "safety should still be on by default");
            Assert.That(autodocSys.GetPatient((autodoc, comp)), Is.EqualTo(patient), "autodoc doesn't see the patient");
            Assert.That(autodocSys.IsAwake(patient), "patient should be awake for this test");

            var program = autodocSys.CreateProgram((autodoc, comp), "incision")!.Value;
            var step = new SurgeryAutodocStep { Part = BodyPartType.Torso, Surgery = "SurgeryOpenIncision" };
            var starter = selfStarted ? patient : doctor;
            Assert.That(autodocSys.AddStep((autodoc, comp), program, step, 0, starter), "couldn't add the surgery step");
            Assert.That(autodocSys.StartProgram((autodoc, comp), program, starter), "program didn't start");

            var active = entities.GetComponent<ActiveAutodocComponent>(autodoc);
            var stopped = autodocSys.Proceed((autodoc, comp, active));
            if (selfStarted)
            {
                Assert.That(stopped, Is.False, "autodoc refused to operate on an awake patient who started it themselves");
                Assert.That(active.Waiting || active.CurrentSurgery != null, "surgery didn't begin");
            }
            else
            {
                Assert.That(stopped, Is.True, "autodoc operated on an awake patient someone else strapped in");
            }
        });

        await pair.CleanReturnAsync();
    }
}
