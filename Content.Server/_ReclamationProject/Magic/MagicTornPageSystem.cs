using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - tearing pages out of a grimoire, casting from a torn page, and putting it back.
/// </summary>
public sealed class MagicTornPageSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly MagicGrimoireSystem _grimoire = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;

    private static readonly SoundSpecifier TearSound = new SoundPathSpecifier("/Audio/Effects/packetrip.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicGrimoireComponent, MagicTearOutMessage>(OnTearOut);
        SubscribeLocalEvent<MagicGrimoireComponent, InteractUsingEvent>(OnPutBack);
        SubscribeLocalEvent<MagicTornPageComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<MagicTornPageComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MagicTornPageComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnTearOut(Entity<MagicGrimoireComponent> ent, ref MagicTearOutMessage args)
    {
        TearOut(ent, args.Actor, args.Page);
    }

    /// <summary>Tears a written page out of the owner's book into a loose torn page.</summary>
    public EntityUid? TearOut(Entity<MagicGrimoireComponent> ent, EntityUid user, int index)
    {
        if (ent.Comp.MagicOwner != user || index < 0 || index >= ent.Comp.MagicPages.Count)
            return null;

        var page = ent.Comp.MagicPages[index];
        if (string.IsNullOrWhiteSpace(page.Text))
            return null;

        if (page.Readied)
            _grimoire.SetReadied(ent, index, false);

        var torn = Spawn("MagicTornPage", Transform(user).Coordinates);
        var comp = EnsureComp<MagicTornPageComponent>(torn);
        comp.MagicOwner = user;
        comp.Page = new MagicPage
        {
            Text = page.Text,
            Note = page.Note,
            Spell = page.Spell,
            Empowered = page.Empowered,
            Glow = page.Glow,
        };
        Dirty(torn, comp);

        ent.Comp.MagicPages[index] = new MagicPage();
        Dirty(ent);
        _grimoire.UpdateUi(ent, user);

        _audio.PlayPvs(TearSound, ent);
        _hands.TryPickupAnyHand(user, torn);
        _magic.Notify(user, Loc.GetString("magic-torn-tear"));
        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):player} tore page {index + 1} (spell: {page.Spell?.Id ?? "none"}) out of {ToPrettyString(ent.Owner):entity}");
        return torn;
    }

    private void OnPutBack(Entity<MagicGrimoireComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<MagicTornPageComponent>(args.Used, out var torn))
            return;
        args.Handled = true;
        PutBack(ent, (args.Used, torn), args.User);
    }

    public bool PutBack(Entity<MagicGrimoireComponent> book, Entity<MagicTornPageComponent> torn, EntityUid user)
    {
        if (book.Comp.MagicOwner != user || torn.Comp.MagicOwner != user)
        {
            _popup.PopupEntity(Loc.GetString("magic-torn-wrong-book"), book, user);
            return false;
        }

        var free = book.Comp.MagicPages.FindIndex(p => string.IsNullOrWhiteSpace(p.Text));
        if (free < 0)
        {
            book.Comp.MagicPages.Add(new MagicPage());
            free = book.Comp.MagicPages.Count - 1;
        }

        book.Comp.MagicPages[free] = torn.Comp.Page;
        torn.Comp.Page.Readied = false;
        Dirty(book);
        QueueDel(torn);
        _grimoire.UpdateUi(book, user);
        _magic.Notify(user, Loc.GetString("magic-torn-put-back"));
        return true;
    }

    private void OnGetActions(Entity<MagicTornPageComponent> ent, ref GetItemActionsEvent args)
    {
        if (ent.Comp.MagicOwner != args.User || ent.Comp.Page.Spell is not { } spellId || !_proto.TryIndex(spellId, out var spell))
            return;

        // A held torn page counts as one of the 8 active spells.
        var hasAction = Exists(ent.Comp.Action);
        if (!hasAction && _magic.CountActive(args.User) >= MagicCasterComponent.ActiveLimit
            && !(TryComp<MagicCasterComponent>(args.User, out var caster) && caster.MagicIgnoreActiveLimit))
        {
            _magic.Notify(args.User, Loc.GetString("magic-limit-reached"));
            return;
        }

        var action = ent.Comp.Action;
        if (!_actionContainer.EnsureAction(ent, ref action, spell.Action))
            return;
        ent.Comp.Action = action;

        var spellAction = EnsureComp<MagicSpellActionComponent>(action.Value);
        spellAction.Spell = spellId;
        spellAction.Empowered = ent.Comp.Page.Empowered;
        Dirty(action.Value, spellAction);
        // Your own note wins; a page that glows (you know the spell) shows its real name and what it does.
        var name = !string.IsNullOrWhiteSpace(ent.Comp.Page.Note) ? ent.Comp.Page.Note
            : ent.Comp.Page.Glow != null ? Loc.GetString(spell.Name) : "· · ·";
        _meta.SetEntityName(action.Value, name);
        if (ent.Comp.Page.Glow != null)
            _meta.SetEntityDescription(action.Value, spell.Summary ?? string.Empty);
        args.AddAction(action);
    }

    private void OnExamined(Entity<MagicTornPageComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.MagicOwner == args.Examiner && ent.Comp.Page.Glow != null
            ? "magic-torn-examine-owner"
            : "magic-torn-examine-other"));
    }

    private void OnShutdown(Entity<MagicTornPageComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Action is { } action && !TerminatingOrDeleted(action))
        {
            _actions.RemoveAction(action);
            QueueDel(action);
        }
    }
}
