using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Actions;
using Content.Shared.Clothing;
using Content.Shared.DoAfter;
using Content.Shared.Inventory;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._ReclamationProject.PowerArmor;

public sealed class RustbucketEngineSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    private static readonly SoundPathSpecifier RevSound = new("/Audio/Weapons/chainsawwield.ogg");

    public override void Initialize()
    {
        SubscribeLocalEvent<RustbucketEngineComponent, GetItemActionsEvent>((uid, engine, ev) =>
        {
            if (ev.SlotFlags == SlotFlags.OUTERCLOTHING)
                ev.AddAction(ref engine.Action, "ActionAtomCatsRevEngine");
        });
        SubscribeLocalEvent<RustbucketEngineComponent, RevRustbucketEvent>(OnRev);
        SubscribeLocalEvent<RustbucketEngineComponent, RevEngineDoAfterEvent>(OnFinished);
        SubscribeLocalEvent<RustbucketEngineComponent, ClothingGotUnequippedEvent>((_, engine, _) => Stop(engine));
        SubscribeLocalEvent<RustbucketEngineComponent, EntityTerminatingEvent>((_, engine, _) => Stop(engine));
    }

    private void OnRev(Entity<RustbucketEngineComponent> ent, ref RevRustbucketEvent ev)
    {
        if (ev.Handled || ent.Comp.Rev != null ||
            !_inventory.TryGetSlotEntity(ev.Performer, "outerClothing", out var worn) || worn != ent.Owner)
            return;
        var args = new DoAfterArgs(EntityManager, ev.Performer, 20f, new RevEngineDoAfterEvent(), ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            MultiplyDelay = false,
        };
        if (!_doAfter.TryStartDoAfter(args, out var id))
            return;
        ent.Comp.Rev = id;
        ent.Comp.Audio = _audio.PlayPvs(RevSound, ev.Performer, AudioParams.Default.WithLoop(true).WithPitchScale(.72f).WithVolume(-4))?.Entity;
        ev.Handled = true;
    }

    private void OnFinished(Entity<RustbucketEngineComponent> ent, ref RevEngineDoAfterEvent ev)
    {
        if (ent.Comp.Rev != ev.DoAfter.Id)
            return;
        ent.Comp.Rev = null;
        ent.Comp.Audio = _audio.Stop(ent.Comp.Audio);
        ev.Handled = true;
    }

    private void Stop(RustbucketEngineComponent engine)
    {
        _doAfter.Cancel(engine.Rev);
        engine.Rev = null;
        engine.Audio = _audio.Stop(engine.Audio);
    }
}
