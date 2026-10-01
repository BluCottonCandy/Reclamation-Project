// #Misfits Add - BarricadeSystem (server): spawns/deletes entities for entrenching tool actions.
// All entity mutation must happen server-side. Inherits shared logic from Content.Shared.
using Content.Shared._Misfits.Entrenching;
using Content.Shared.Stacks;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;

namespace Content.Server._Misfits.Entrenching;

public sealed class BarricadeSystem : SharedBarricadeSystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedStackSystem _stackServer = default!;

    protected override void SpawnDigResult(Entity<EntrenchingToolComponent> tool, EntityUid user, int count)
    {
        _audio.PlayPvs(tool.Comp.DigSound, user);
        var coords = Transform(user).Coordinates;
        for (var i = 0; i < count; i++)
            Spawn("CMSandbagEmpty", coords);
    }

    protected override void SpawnFilledBag(Entity<EmptySandbagComponent> bag, EntityUid user)
    {
        var coords = Transform(bag).Coordinates;
        Spawn(bag.Comp.Filled, coords);
        QueueDel(bag);
    }

    protected override void SpawnBarricade(Entity<FullSandbagComponent> bag, EntityUid user, EntityCoordinates coords)
    {
        // Consume required stack count
        if (TryComp<StackComponent>(bag, out var stack))
        {
            _stackServer.Use(bag, bag.Comp.StackRequired, stack);
            if (stack.Count <= 0)
                QueueDel(bag);
        }
        else
        {
            QueueDel(bag);
        }

        Spawn(bag.Comp.Builds, coords);
    }

    protected override void DismantleBarricade(Entity<BarricadeSandbagComponent> barricade, EntityUid user)
    {
        var coords = Transform(barricade).Coordinates;
        // Return a full sandbag item when dismantled
        Spawn(barricade.Comp.Material, coords);
        QueueDel(barricade);
    }
}
