using Robust.Shared.Map;
using Content.Server._ReclamationProject.Medical;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

internal static class TraumaTestPatients
{
    // Explicit opt-in for test dummies; production NPCs must never opt in automatically.
    public static EntityUid SpawnTraumaPatient(this IEntityManager entities, EntityCoordinates coordinates)
    {
        var patient = entities.SpawnEntity("MobHuman", coordinates);
        entities.System<TraumaSystem>().InitializeBody(patient);
        return patient;
    }
}
