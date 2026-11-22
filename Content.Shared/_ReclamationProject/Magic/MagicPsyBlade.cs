using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Wieldable;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Magic;

// #Reclamation Project - the psy katana (summoned by Psy-Blade): sheathe / draw, the sword guard, and the
// shared half of Bullet Catch.

/// <summary>A blade that can be sheathed and drawn (use it in hand). Sheathed it can go on your belt or back.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MagicPsyBladeComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Drawn;

    /// <summary>Hits with the blade out.</summary>
    [DataField(required: true)]
    public DamageSpecifier DrawnDamage = default!;

    /// <summary>Hits with the scabbard still on.</summary>
    [DataField(required: true)]
    public DamageSpecifier SheathedDamage = default!;

    /// <summary>In-hand look while sheathed ("sheathed-inhand-left" ...).</summary>
    [DataField]
    public string SheathedPrefix = "sheathed";
}

/// <summary>
/// Lasts while it is held, worn or carried in a bag. Left loose (dropped, thrown, or stuck in someone) for
/// <see cref="Delay"/> seconds, it crumbles to ash: only the item itself goes, a pile of ash is left on the floor.
/// </summary>
[RegisterComponent]
public sealed partial class MagicAshWhenLooseComponent : Component
{
    [DataField]
    public float Delay = 10f;

    [DataField]
    public string Ash = "Ash";

    /// <summary>Who summoned it. Anyone else holding or carrying it makes it crumble at once (null = anyone may).</summary>
    [ViewVariables]
    public EntityUid? Owner;

    /// <summary>When it was last seen loose (null while it's held, worn or in a bag).</summary>
    [ViewVariables]
    public TimeSpan? LooseSince;
}

/// <summary>Held and worn sprites drawn at double resolution: shown at this scale so they match normal sprites.</summary>
[RegisterComponent]
public sealed partial class MagicHiResComponent : Component
{
    [DataField]
    public float Scale = 0.5f;
}

[Serializable, NetSerializable]
public enum MagicPsyBladeVisuals : byte
{
    Drawn,
}

[Serializable, NetSerializable]
public enum MagicPsyBladeLayers : byte
{
    Base,
}

/// <summary>
/// The sword guard: a hotbar button on the blade. Only with the blade drawn and held in both hands: bullets and
/// lasers from the front are sent straight back at the shooter. Costs stamina every second while up and more for
/// each block; too tired and the guard drops. You walk slower and can't swing while guarding.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MagicSwordGuardComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField]
    public float StaminaPerSecond = 1f;

    [DataField]
    public float StaminaPerBlock = 5f;

    /// <summary>Walk and run speed while guarding.</summary>
    [DataField]
    public float SpeedModifier = 0.6f;

    /// <summary>Bullets within this many degrees of where you face are blocked (90 = the whole front half).</summary>
    [DataField]
    public float FrontArc = 75f;

    [DataField]
    public string Action = "ActionMagicSwordGuard";

    [ViewVariables]
    public EntityUid? ActionEntity;

    [ViewVariables]
    public float Accumulator;
}

/// <summary>On whoever is holding a guard up (walk speed and no swinging, predicted on their client too).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MagicGuardingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Sword;

    [DataField, AutoNetworkedField]
    public float SpeedModifier = 0.6f;
}

public sealed partial class MagicSwordGuardEvent : InstantActionEvent;

/// <summary>Bullet Catch (King): toggled. Bullets that would hit you stop and hover around you, any number of them.</summary>
public sealed partial class MagicBulletCatchEvent : InstantActionEvent
{
    [DataField]
    public float DrainPerSecond = 1f;
}

/// <summary>Someone with Bullet Catch up. Count goes to their own client so a left click can throw the bullets.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MagicBulletCatcherComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>How many bullets are hovering (sent to the owner's client).</summary>
    [AutoNetworkedField]
    public int Count;

    /// <summary>The spell is up (still catching). Caught bullets stay until thrown even after it ends.</summary>
    [AutoNetworkedField]
    public bool Catching;

    [ViewVariables] public float DrainPerSecond = 1f;
    [ViewVariables] public float Accumulator;
    [ViewVariables] public float Angle;
    [ViewVariables] public List<EntityUid> Caught = new();
}

/// <summary>The shared half of the guard: slower walking and no swinging while it is up (predicted).</summary>
public sealed class MagicSwordGuardSharedSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicGuardingComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<MagicGuardingComponent, AttackAttemptEvent>(OnAttack);
        SubscribeLocalEvent<MagicPsyBladeComponent, BeingEquippedAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<MagicPsyBladeComponent, BeforeWieldEvent>(OnBeforeWield);
    }

    private void OnSpeed(Entity<MagicGuardingComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.SpeedModifier, ent.Comp.SpeedModifier);
    }

    private void OnAttack(Entity<MagicGuardingComponent> ent, ref AttackAttemptEvent args)
    {
        args.Cancel();
    }

    /// <summary>A drawn blade can't go on the belt or back: sheathe it first.</summary>
    private void OnEquipAttempt(Entity<MagicPsyBladeComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (!ent.Comp.Drawn)
            return;
        args.Cancel();
        _popup.PopupClient(Loc.GetString("magic-psyblade-sheathe-first"), args.Equipee, args.Equipee);
    }

    /// <summary>Two hands on the hilt only with the blade out.</summary>
    private void OnBeforeWield(Entity<MagicPsyBladeComponent> ent, ref BeforeWieldEvent args)
    {
        if (!ent.Comp.Drawn)
            args.Cancel();
    }
}
