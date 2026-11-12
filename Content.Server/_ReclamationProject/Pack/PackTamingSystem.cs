using Robust.Shared.Prototypes;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._ReclamationProject.Pack;
using Content.Shared.Actions;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;

namespace Content.Server._ReclamationProject.Pack;

public sealed class PackTamingSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private static readonly Dictionary<string, string> Animals = new()
    {
        ["N14MobMolerat"] = "PackTamedMolerat",
        ["PackMenagerieMolerat"] = "PackTamedMolerat",
        ["PackMenagerieNightstalker"] = "PackTamedNightstalker",
        ["N14MobMirelurk"] = "PackTamedMirelurk",
        ["N14MobRadMirelurk"] = "PackTamedRadMirelurk",
        ["N14MobNightstalker"] = "PackTamedNightstalker",
        ["N14MobNightstalkerCub"] = "PackTamedNightstalkerCub",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PackMemberComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PackMemberComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PackMemberComponent, PackTameActionEvent>(OnTame);
    }

    private void OnStartup(Entity<PackMemberComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.CanTame)
            _actions.AddAction(ent, ref ent.Comp.TameAction, "ActionPackTameAnimal");
        foreach (var animal in ent.Comp.StartingAnimals)
            SpawnFollower(ent, animal);
        for (var i = 0; i < ent.Comp.StartingArrows; i++)
            Spawn("PackHuntingArrow", Transform(ent).Coordinates);
    }

    public bool TrySpawnHound(Entity<PackMemberComponent> owner)
    {
        if (_npc.GetFollowerCountForCommander(owner) >= _npc.GetNeutralFollowerCapacity(owner))
            return false;
        SpawnFollower(owner, "PackHound");
        return true;
    }

    private void SpawnFollower(EntityUid owner, EntProtoId prototype)
    {
        var animal = Spawn(prototype, Transform(owner).Coordinates);
        AddComp(animal, new FollowerAutoRecruitComponent { Commander = owner });
    }

    private void OnShutdown(Entity<PackMemberComponent> ent, ref ComponentShutdown args) =>
        _actions.RemoveAction(ent.Owner, ent.Comp.TameAction);

    private void OnTame(Entity<PackMemberComponent> ent, ref PackTameActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner)
            return;
        if (TryTame(ent, args.Target))
            args.Handled = true;
    }

    public bool TryTame(Entity<PackMemberComponent> tamer, EntityUid target)
    {
        if (!tamer.Comp.CanTame || TerminatingOrDeleted(target) ||
            !_mob.IsAlive(tamer) || !_mob.IsAlive(target) || !_blocker.CanInteract(tamer, target) ||
            !_interaction.InRangeUnobstructed(tamer.Owner, target, range: 2f, popup: true))
            return false;
        if (HasComp<PackAnimalComponent>(target) || HasComp<RecruitedFollowerComponent>(target) ||
            _mind.TryGetMind(target, out _, out _) ||
            HasComp<Content.Shared._Misfits.TribalHunt.LegendaryCreatureComponent>(target) ||
            HasComp<Content.Server._Misfits.TribalHunt.MinorHuntCreatureComponent>(target))
            return false;
        var id = MetaData(target).EntityPrototype?.ID;
        if (id == null || !Animals.TryGetValue(id, out var variant) ||
            (id is not ("PackMenagerieMolerat" or "PackMenagerieNightstalker") &&
             !_factions.IsEntityHostile((target, null, null), (tamer.Owner, null))))
        {
            _popup.PopupEntity(Loc.GetString("pack-tame-invalid"), tamer, tamer);
            return false;
        }

        // Mark before replacement so another activation cannot copy the same queued-for-deletion animal.
        EnsureComp<PackAnimalComponent>(target);
        var pet = Spawn(variant, Transform(target).Coordinates);
        if (TryComp<DamageableComponent>(target, out var oldDamage) && TryComp<DamageableComponent>(pet, out var newDamage))
            _damage.SetDamage(pet, newDamage, new DamageSpecifier(oldDamage.Damage));
        QueueDel(target);
        _popup.PopupEntity(Loc.GetString("pack-tame-success"), tamer, tamer);
        return true;
    }
}
