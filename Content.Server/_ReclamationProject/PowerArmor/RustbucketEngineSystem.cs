using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Actions;
using Content.Shared.Clothing;
using Content.Shared.DoAfter;
using Content.Shared.Inventory;
using Robust.Shared.Audio.Systems;

namespace Content.Server._ReclamationProject.PowerArmor;

public sealed class RustbucketEngineSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

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
        SubscribeLocalEvent<RustbucketIdleSoundComponent, EntityTerminatingEvent>(OnIdleEnded);
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
        _audio.Stop(ent.Comp.Audio);
        ent.Comp.Audio = _audio.PlayPvs(ent.Comp.RevSound, ev.Performer)?.Entity;
        ev.Handled = true;
    }

    private void OnFinished(Entity<RustbucketEngineComponent> ent, ref RevEngineDoAfterEvent ev)
    {
        if (ent.Comp.Rev != ev.DoAfter.Id)
            return;
        ent.Comp.Rev = null;
        ent.Comp.Audio = _audio.Stop(ent.Comp.Audio);
        ev.Handled = true;
        if (!TerminatingOrDeleted(ev.User) && _inventory.TryGetSlotEntity(ev.User, "outerClothing", out var worn) &&
            worn == ent.Owner && _audio.PlayPvs(ent.Comp.IdleSound, ev.User) is {} idle)
        {
            ent.Comp.Audio = idle.Entity;
            EnsureComp<RustbucketIdleSoundComponent>(idle.Entity).Frame = ent.Owner;
        }
    }

    private void OnIdleEnded(Entity<RustbucketIdleSoundComponent> sound, ref EntityTerminatingEvent ev)
    {
        if (TryComp<RustbucketEngineComponent>(sound.Comp.Frame, out var engine) && engine.Audio == sound.Owner)
            engine.Audio = null;
    }

    private void Stop(RustbucketEngineComponent engine)
    {
        var rev = engine.Rev;
        engine.Rev = null;
        _doAfter.Cancel(rev);
        engine.Audio = _audio.Stop(engine.Audio);
    }
}

[RegisterComponent]
public sealed partial class RustbucketIdleSoundComponent : Component
{
    [DataField] public EntityUid Frame;
}
