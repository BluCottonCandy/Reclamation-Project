using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - Phantom: a toggled magic cloak. Cast once to vanish (25 mana), cast again to come back.
/// While it is up it drains 1 mana a second; at 0 mana the normal overcast damage starts until you drop it.
/// NPCs ignore you (NpcFactionSystem skips MagicPhantomComponent). It is its own cloak, not a Stealth Boy, so
/// there is no Stealth Boy addiction or hallucinations. Attacking or shooting breaks it; touching, carrying
/// and opening things does not.
/// </summary>
public sealed class MagicPhantomSystem : EntitySystem
{
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly SharedStealthSystem _stealth = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicPhantomEvent>(OnPhantom);
        SubscribeLocalEvent<MagicPhantomComponent, MeleeAttackEvent>(OnMelee);
        SubscribeLocalEvent<MagicPhantomComponent, ShotAttemptedEvent>(OnShot);
    }

    private void OnPhantom(MagicPhantomEvent args)
    {
        if (args.Handled)
            return;
        var user = args.Performer;

        // Casting it again drops the cloak (free).
        if (HasComp<MagicPhantomComponent>(user))
        {
            args.Handled = true;
            End(user, "magic-phantom-end");
            return;
        }

        // Already cloaked some other way (Stealth Boy, Nightkin): don't fight over the same cloak.
        if (HasComp<StealthComponent>(user))
        {
            _magic.Notify(user, Loc.GetString("magic-phantom-already-cloaked"));
            return;
        }

        if (!_magic.TryPayForSpell(args.Action, user))
            return;
        args.Handled = true;

        var phantom = EnsureComp<MagicPhantomComponent>(user);
        phantom.DrainPerSecond = args.DrainPerSecond;
        var stealth = EnsureComp<StealthComponent>(user); // MinVisibility defaults to -1 (fully invisible)
        _stealth.SetEnabled(user, true, stealth);
        _stealth.SetVisibility(user, -1f, stealth);
        _magic.Notify(user, Loc.GetString("magic-phantom-start"));
    }

    private void OnMelee(Entity<MagicPhantomComponent> ent, ref MeleeAttackEvent args)
    {
        End(ent, "magic-phantom-broken");
    }

    private void OnShot(Entity<MagicPhantomComponent> ent, ref ShotAttemptedEvent args)
    {
        End(ent, "magic-phantom-broken");
    }

    private void End(EntityUid user, string message)
    {
        if (!RemComp<MagicPhantomComponent>(user))
            return;
        RemComp<StealthComponent>(user);
        _magic.Notify(user, Loc.GetString(message));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<MagicPhantomComponent, MagicCasterComponent>();
        while (query.MoveNext(out var uid, out var phantom, out var caster))
        {
            // Knocked out, dead, or the cloak was removed some other way: drop it.
            if (!HasComp<StealthComponent>(uid) || _mobState.IsIncapacitated(uid))
            {
                End(uid, "magic-phantom-end");
                continue;
            }

            phantom.Accumulator += frameTime;
            if (phantom.Accumulator < 1f)
                continue;
            phantom.Accumulator -= 1f;
            // Pays from mana; once mana is empty this is overcasting, which hurts (the usual mana debuff).
            _magic.PayMana((uid, caster), phantom.DrainPerSecond);
        }
    }
}
