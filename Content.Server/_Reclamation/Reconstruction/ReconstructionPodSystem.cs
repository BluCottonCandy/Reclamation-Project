using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.EUI;
using Content.Shared._Reclamation.Reconstruction;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Preferences;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Reclamation.Reconstruction;

/// <summary>
/// Runs the reconstruction pod: an operator starts it, the patient inside agrees and picks a new look,
/// then the operator keeps it running until the new look is applied.
/// </summary>
public sealed class ReconstructionPodSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReconstructionPodComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ReconstructionPodComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ReconstructionPodComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<ReconstructionPodComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<ReconstructionPodComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ReconstructionPodComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<ReconstructionPodComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<ReconstructionPodComponent, ReconstructionDoAfterEvent>(OnDoAfter);
    }

    private void OnInit(Entity<ReconstructionPodComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(ent, ent.Comp.ContainerId);
    }

    private void OnShutdown(Entity<ReconstructionPodComponent> ent, ref ComponentShutdown args)
    {
        Reset(ent);
    }

    /// <summary>Drop the patient out instead of deleting them with the pod.</summary>
    private void OnTerminating(Entity<ReconstructionPodComponent> ent, ref EntityTerminatingEvent args)
    {
        Reset(ent);
        if (_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
            _container.EmptyContainer(container, force: true);
    }

    private void OnActivate(Entity<ReconstructionPodComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        TryStart(ent, args.User);
    }

    private void OnGetVerbs(Entity<ReconstructionPodComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || GetPatient(ent) is not { } patient || patient == args.User)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("reconstruction-pod-verb-start"),
            Act = () => TryStart(ent, user),
            Priority = 1,
        });
    }

    /// <summary>Ask the patient right away, so they can pick their look before anyone starts the pod.</summary>
    private void OnInserted(Entity<ReconstructionPodComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ent.Comp.ContainerId)
            return;

        var patient = args.Entity;
        if (!TryComp<HumanoidAppearanceComponent>(patient, out var humanoid)
            || humanoid.LastProfileLoaded == null
            || !_mobState.IsAlive(patient)
            || !TryComp<ActorComponent>(patient, out var actor))
        {
            return;
        }

        Reset(ent);
        ent.Comp.Patient = patient;
        ent.Comp.Eui = new ReconstructionEui(this, ent, BuildState(patient, humanoid));
        _eui.OpenEui(ent.Comp.Eui, actor.PlayerSession);
    }

    private void OnRemoved(Entity<ReconstructionPodComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ent.Comp.ContainerId || ent.Comp.Patient != args.Entity)
            return;

        Cancel(ent, "reconstruction-pod-cancel-left");
    }

    private EntityUid? GetPatient(Entity<ReconstructionPodComponent> ent)
    {
        return _container.TryGetContainer(ent, ent.Comp.ContainerId, out var container)
            ? container.ContainedEntities.FirstOrDefault() is { Valid: true } patient ? patient : null
            : null;
    }

    public void TryStart(Entity<ReconstructionPodComponent> ent, EntityUid user)
    {
        if (ent.Comp.Operator != null)
        {
            _popup.PopupEntity(Loc.GetString("reconstruction-pod-busy"), ent, user);
            return;
        }

        if (GetPatient(ent) is not { } patient)
        {
            _popup.PopupEntity(Loc.GetString("reconstruction-pod-empty"), ent, user);
            return;
        }

        if (patient == user)
        {
            _popup.PopupEntity(Loc.GetString("reconstruction-pod-inside"), ent, user);
            return;
        }

        if (!_access.IsAllowed(user, ent))
        {
            _popup.PopupEntity(Loc.GetString("reconstruction-pod-access-denied"), ent, user);
            return;
        }

        if (!HasComp<HumanoidAppearanceComponent>(patient) || !_mobState.IsAlive(patient))
        {
            _popup.PopupEntity(Loc.GetString("reconstruction-pod-invalid-patient"), ent, user);
            return;
        }

        if (ent.Comp.Patient != patient || ent.Comp.Pending == null)
        {
            _popup.PopupEntity(Loc.GetString("reconstruction-pod-not-ready"), ent, user);
            return;
        }

        var args = new DoAfterArgs(EntityManager, user, ent.Comp.Duration, new ReconstructionDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            DistanceThreshold = 1.5f,
        };

        if (!_doAfter.TryStartDoAfter(args, out var doAfterId))
            return;

        ent.Comp.Operator = user;
        ent.Comp.DoAfter = doAfterId;
        _appearance.SetData(ent, ReconstructionPodVisuals.Running, true);
        _popup.PopupEntity(Loc.GetString("reconstruction-pod-started"), ent, user);
        _popup.PopupEntity(Loc.GetString("reconstruction-pod-started-patient"), ent, patient);
    }

    private ReconstructionEuiState BuildState(EntityUid patient, HumanoidAppearanceComponent humanoid)
    {
        var hair = humanoid.MarkingSet.TryGetCategory(MarkingCategories.Hair, out var hairs) && hairs.Count > 0
            ? hairs[0]
            : null;
        var facialHair = humanoid.MarkingSet.TryGetCategory(MarkingCategories.FacialHair, out var facialHairs) && facialHairs.Count > 0
            ? facialHairs[0]
            : null;

        var markings = new List<Marking>();
        foreach (var marking in humanoid.MarkingSet.GetForwardEnumerator())
        {
            if (marking != hair && marking != facialHair)
                markings.Add(new Marking(marking));
        }

        return new ReconstructionEuiState
        {
            Species = humanoid.Species,
            Name = Name(patient),
            Sex = humanoid.Sex,
            Height = humanoid.Height,
            Width = humanoid.Width,
            Appearance = new HumanoidCharacterAppearance(
                hair?.MarkingId ?? HairStyles.DefaultHairStyle,
                hair?.MarkingColors.FirstOrDefault() ?? Color.Black,
                facialHair?.MarkingId ?? HairStyles.DefaultFacialHairStyle,
                facialHair?.MarkingColors.FirstOrDefault() ?? Color.Black,
                humanoid.EyeColor,
                humanoid.SkinColor,
                markings),
        };
    }

    public void OnPatientDeclined(EntityUid pod)
    {
        if (!TryComp<ReconstructionPodComponent>(pod, out var comp))
            return;

        comp.Eui = null;
        if (comp.Patient is { } patient)
            _popup.PopupEntity(Loc.GetString("reconstruction-pod-declined-patient"), pod, patient);

        Reset((pod, comp));
    }

    public void OnPatientSubmitted(EntityUid pod, ReconstructionSubmitMessage submit)
    {
        if (!TryComp<ReconstructionPodComponent>(pod, out var comp)
            || comp.Patient is not { } patient
            || GetPatient((pod, comp)) != patient
            || !TryComp<HumanoidAppearanceComponent>(patient, out var humanoid))
        {
            return;
        }

        comp.Eui = null;
        comp.Pending = Validate(patient, humanoid, submit);
        _popup.PopupEntity(Loc.GetString("reconstruction-pod-ready-patient"), pod, patient);
    }

    /// <summary>Never trust the client: clamp everything to what this species allows.</summary>
    private ReconstructionSubmitMessage Validate(EntityUid patient, HumanoidAppearanceComponent humanoid, ReconstructionSubmitMessage submit)
    {
        var species = _proto.Index<SpeciesPrototype>(humanoid.Species);

        var name = new string(submit.Name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0)
            name = Name(patient);
        if (name.Length > HumanoidCharacterProfile.MaxNameLength)
            name = name[..HumanoidCharacterProfile.MaxNameLength];

        var sex = species.Sexes.Contains(submit.Sex) ? submit.Sex : humanoid.Sex;
        var valid = HumanoidCharacterAppearance.EnsureValid(submit.Appearance, humanoid.Species, sex);

        // Body-part replacements (prosthetics) are not something this pod can change.
        var appearance = new HumanoidCharacterAppearance(
            valid.HairStyleId,
            valid.HairColor,
            valid.FacialHairStyleId,
            valid.FacialHairColor,
            valid.EyeColor,
            valid.SkinColor,
            valid.Markings,
            humanoid.CustomBaseLayers);

        return new ReconstructionSubmitMessage(
            name,
            sex,
            Math.Clamp(submit.Height, species.MinHeight, species.MaxHeight),
            Math.Clamp(submit.Width, species.MinWidth, species.MaxWidth),
            appearance);
    }

    private void OnDoAfter(Entity<ReconstructionPodComponent> ent, ref ReconstructionDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ent.Comp.DoAfter = null;

        if (args.Cancelled)
        {
            // Keep the patient's chosen look so the operator can start again.
            _popup.PopupEntity(Loc.GetString("reconstruction-pod-cancel-operator"), ent, args.User);
            ent.Comp.Operator = null;
            _appearance.SetData(ent, ReconstructionPodVisuals.Running, false);
            return;
        }

        if (ent.Comp.Pending is not { } pending
            || ent.Comp.Patient is not { } patient
            || GetPatient(ent) != patient
            || !TryComp<HumanoidAppearanceComponent>(patient, out var humanoid)
            || humanoid.LastProfileLoaded is not { } profile)
        {
            Cancel(ent, "reconstruction-pod-cancel-left");
            return;
        }

        var oldName = Name(patient);
        var newProfile = profile
            .WithName(pending.Name)
            .WithSex(pending.Sex)
            .WithHeight(pending.Height)
            .WithWidth(pending.Width)
            .WithCharacterAppearance(pending.Appearance);

        // Only the body changes. The saved character and the mind's CharacterName are left alone on purpose:
        // the persistent wallet and player data are keyed on that name, so money keeps saving to the real
        // character, and the next round spawns with the original look.
        _humanoid.LoadProfile(patient, newProfile, humanoid);
        _meta.SetEntityName(patient, pending.Name);

        _adminLog.Add(LogType.Identity, LogImpact.Medium,
            $"{ToPrettyString(args.User):operator} reconstructed {ToPrettyString(patient):patient} (was {oldName}) in {ToPrettyString(ent):pod}");

        _audio.PlayPvs(ent.Comp.FinishSound, ent);
        _popup.PopupEntity(Loc.GetString("reconstruction-pod-finished"), ent, args.User);
        _popup.PopupEntity(Loc.GetString("reconstruction-pod-finished-patient"), ent, patient);

        // Clear state before ejecting so the removal isn't treated as the patient walking out.
        Reset(ent);
        if (_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
            _container.Remove(patient, container);
    }

    private void Cancel(Entity<ReconstructionPodComponent> ent, string reason)
    {
        if (ent.Comp.Operator is { } user)
            _popup.PopupEntity(Loc.GetString(reason), ent, user);

        Reset(ent);
    }

    private void Reset(Entity<ReconstructionPodComponent> ent)
    {
        // Clear everything first: closing the window or cancelling the do-after calls back into this system.
        var eui = ent.Comp.Eui;
        var doAfter = ent.Comp.DoAfter;
        ent.Comp.Eui = null;
        ent.Comp.DoAfter = null;
        ent.Comp.Operator = null;
        ent.Comp.Patient = null;
        ent.Comp.Pending = null;
        _appearance.SetData(ent, ReconstructionPodVisuals.Running, false);

        if (eui != null)
        {
            eui.Finished = true;
            if (!eui.IsShutDown)
                eui.Close();
        }

        if (doAfter != null)
            _doAfter.Cancel(doAfter);
    }
}
