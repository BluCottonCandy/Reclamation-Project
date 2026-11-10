using Content.Shared.Interaction.Events;
using Content.Shared.UserInterface;
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._ReclamationProject.Institute.Uplink;

/// <summary>
/// #Reclamation Project - an Institute tablet only opens for the person who typed its owner's uplink PIN.
/// Dropping it locks it again.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class InstituteTabletLockComponent : Component
{
    /// <summary>False until an Institute member is registered as the owner. Unowned tablets stay open.</summary>
    [DataField, AutoNetworkedField]
    public bool Owned;

    [DataField, AutoNetworkedField]
    public EntityUid? UnlockedFor;

    /// <summary>Server only: the owner's mind and their PIN.</summary>
    [ViewVariables]
    public EntityUid? OwnerMind;

    [ViewVariables]
    public string Pin = string.Empty;

    [ViewVariables]
    public TimeSpan NextAttempt;
}

/// <summary>Raised on the server on a locked tablet someone tried to open.</summary>
public sealed class InstituteTabletLockedEvent(EntityUid user) : EntityEventArgs
{
    public readonly EntityUid User = user;
}

public sealed class InstituteTabletLockSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InstituteTabletLockComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<InstituteTabletLockComponent, DroppedEvent>(OnDropped);
    }

    private void OnOpenAttempt(EntityUid uid, InstituteTabletLockComponent comp, ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled || !comp.Owned || comp.UnlockedFor == args.User)
            return;

        args.Cancel();
        if (_net.IsServer)
            RaiseLocalEvent(uid, new InstituteTabletLockedEvent(args.User));
    }

    private void OnDropped(EntityUid uid, InstituteTabletLockComponent comp, DroppedEvent args)
    {
        Lock((uid, comp));
    }

    public void Lock(Entity<InstituteTabletLockComponent> ent)
    {
        if (ent.Comp.UnlockedFor == null)
            return;

        ent.Comp.UnlockedFor = null;
        Dirty(ent);
    }

    public void Unlock(Entity<InstituteTabletLockComponent> ent, EntityUid user)
    {
        ent.Comp.UnlockedFor = user;
        Dirty(ent);
    }
}
