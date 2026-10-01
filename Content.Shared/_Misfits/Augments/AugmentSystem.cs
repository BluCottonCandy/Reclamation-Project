// #Misfits Add - Core augment system: tracks organ install/remove and exposes
// an event relay for other augment subsystems.
// Ported and adapted from Goob-Station (Content.Goobstation.Shared.Augments.AugmentSystem).
// Grants body entities passive AccessibleOverride so they can self-examine installed augments.

using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Interaction;

namespace Content.Shared._Misfits.Augments;

public sealed class AugmentSystem : EntitySystem
{
    private EntityQuery<InstalledAugmentsComponent> _installedQuery;
    private EntityQuery<OrganComponent> _organQuery;

    public override void Initialize()
    {
        base.Initialize();

        _installedQuery = GetEntityQuery<InstalledAugmentsComponent>();
        _organQuery = GetEntityQuery<OrganComponent>();

        SubscribeLocalEvent<AugmentComponent, OrganAddedToBodyEvent>(OnOrganAdded);
        SubscribeLocalEvent<AugmentComponent, OrganRemovedFromBodyEvent>(OnOrganRemoved);

        // Allow the body to interact with its own installed augments (e.g. toggle them).
        SubscribeLocalEvent<InstalledAugmentsComponent, AccessibleOverrideEvent>(OnAccessibleOverride);
    }

    private void OnOrganAdded(Entity<AugmentComponent> augment, ref OrganAddedToBodyEvent args)
    {
        var installed = EnsureComp<InstalledAugmentsComponent>(args.Body);
        installed.InstalledAugments.Add(GetNetEntity(augment));
    }

    private void OnOrganRemoved(Entity<AugmentComponent> augment, ref OrganRemovedFromBodyEvent args)
    {
        if (!TryComp<InstalledAugmentsComponent>(args.OldBody, out var installed))
            return;

        installed.InstalledAugments.Remove(GetNetEntity(augment));

        // Clean up the tracker component once all augments are gone.
        if (installed.InstalledAugments.Count == 0)
            RemComp<InstalledAugmentsComponent>(args.OldBody);
    }

    private void OnAccessibleOverride(Entity<InstalledAugmentsComponent> ent, ref AccessibleOverrideEvent args)
    {
        // Bodies may always reach their own augments.
        if (GetBody(args.Target) is not { } body || body != args.User)
            return;

        args.Handled = true;
        args.Accessible = true;
    }

    #region Public API

    /// <summary>
    /// Returns the body entity an augment organ is installed in, or null if not installed.
    /// </summary>
    public EntityUid? GetBody(EntityUid augment) => _organQuery.CompOrNull(augment)?.Body;

    /// <summary>
    /// Relays an event to all augment organs installed in a body.
    /// Call this from specialised relay systems to let augments react to body events.
    /// </summary>
    public void RelayEvent<T>(EntityUid body, ref T ev) where T : notnull
    {
        if (_installedQuery.TryComp(body, out var comp))
            RelayEvent((body, comp), ref ev);
    }

    /// <inheritdoc cref="RelayEvent{T}(EntityUid,ref T)"/>
    public void RelayEvent<T>(Entity<InstalledAugmentsComponent> ent, ref T ev) where T : notnull
    {
        foreach (var netEnt in ent.Comp.InstalledAugments)
        {
            var aug = GetEntity(netEnt);
            RaiseLocalEvent(aug, ref ev);
        }
    }

    #endregion
}
