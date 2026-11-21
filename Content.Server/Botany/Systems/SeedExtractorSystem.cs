using Content.Server.Botany.Components;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server.Botany.Systems;

public sealed class SeedExtractorSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly BotanySystem _botanySystem = default!;
    [Dependency] private readonly ConstructionSystem _construction = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SeedExtractorComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<SeedExtractorComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<SeedExtractorComponent, UpgradeExamineEvent>(OnUpgradeExamine);
    }

    private void OnInteractUsing(EntityUid uid, SeedExtractorComponent seedExtractor, InteractUsingEvent args)
    {
        if (!this.IsPowered(uid, EntityManager))
            return;

        if (!TryComp(args.Used, out ProduceComponent? produce)) return;
        if (!_botanySystem.TryGetSeed(produce, out var seed) || seed.Seedless)
        {
            _popupSystem.PopupCursor(Loc.GetString("seed-extractor-component-no-seeds",("name", args.Used)),
                args.User, PopupType.MediumCaution);
            return;
        }

        _popupSystem.PopupCursor(Loc.GetString("seed-extractor-component-interact-message",("name", args.Used)),
            args.User, PopupType.Medium);

        // #Misfits Fix - seed extractor now respects stack count; stacking 5 produce
        // yields 5x the seeds instead of treating the whole stack as a single item
        var stackMultiplier = 1;
        if (TryComp<StackComponent>(args.Used, out var stack))
            stackMultiplier = stack.Count;

        QueueDel(args.Used);

        var multiplier = GetSeedAmountMultiplier(uid, seedExtractor);
        var amount = (int) (_random.NextFloat(seedExtractor.BaseMinSeeds, seedExtractor.BaseMaxSeeds + 1) * multiplier) * stackMultiplier;
        var coords = Transform(uid).Coordinates;

        var packetSeed = seed;
        if (amount > 1)
            packetSeed.Unique = false;

        for (var i = 0; i < amount; i++)
        {
            _botanySystem.SpawnSeedPacket(packetSeed, coords, args.User);
        }
    }

    private void OnRefreshParts(EntityUid uid, SeedExtractorComponent seedExtractor, RefreshPartsEvent args)
    {
        var manipulatorQuality = args.PartRatings[seedExtractor.MachinePartSeedAmount];
        seedExtractor.SeedAmountMultiplier = MathF.Pow(seedExtractor.PartRatingSeedAmountMultiplier, manipulatorQuality - 1);
    }

    private float GetSeedAmountMultiplier(EntityUid uid, SeedExtractorComponent extractor)
    {
        // Initialized map saves keep the installed parts but skip the initial parts refresh.
        if (extractor.SeedAmountMultiplier == null && TryComp<MachineComponent>(uid, out var machine))
            _construction.RefreshParts(uid, machine);

        return extractor.SeedAmountMultiplier ?? 1f;
    }

    private void OnUpgradeExamine(EntityUid uid, SeedExtractorComponent seedExtractor, UpgradeExamineEvent args)
    {
        args.AddPercentageUpgrade("seed-extractor-component-upgrade-seed-yield", GetSeedAmountMultiplier(uid, seedExtractor));
    }
}
