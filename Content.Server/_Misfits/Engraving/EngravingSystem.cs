// #Misfits Add: Lets players permanently engrave items with sharp objects.
using Content.Server.Administration;
using Content.Server.Kitchen.Components;
using Content.Shared._Misfits.Engraving;
using Content.Shared.Administration;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.Engraving;

public sealed class EngravingSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly QuickDialogSystem _quickDialog = default!;

    public override void Initialize()
    {
        base.Initialize();

        // #Misfits Add: engraving is only exposed through the right-click utility menu.
        SubscribeLocalEvent<SharpComponent, GetVerbsEvent<UtilityVerb>>(OnGetEngraveVerb);
        SubscribeLocalEvent<EngravedComponent, ExaminedEvent>(OnExamined);
    }

    private void OnGetEngraveVerb(EntityUid uid, SharpComponent _, GetVerbsEvent<UtilityVerb> args)
    {
        if (args.Hands == null || args.Using != uid || !args.CanAccess || !args.CanInteract)
            return;

        var target = args.Target;

        if (!HasComp<EngravableComponent>(target) || HasComp<EngravedComponent>(target))
            return;

        var verb = new UtilityVerb
        {
            Act = () => OpenEngravingDialog(args.User, target, uid),
            DoContactInteraction = false,
            Impact = LogImpact.Low,
            Text = Loc.GetString("engraving-verb-text"),
            Message = Loc.GetString("engraving-verb-message"),
        };

        args.Verbs.Add(verb);
    }

    private void OpenEngravingDialog(EntityUid user, EntityUid target, EntityUid sharp)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        _quickDialog.OpenDialog<string, LongString>(
            actor.PlayerSession,
            Loc.GetString("engraving-dialog-title"),
            Loc.GetString("engraving-dialog-name"),
            Loc.GetString("engraving-dialog-description"),
            (name, description) =>
            {
                if (!Exists(target) || !Exists(user) || !Exists(sharp) || !HasComp<SharpComponent>(sharp))
                    return;

                if (HasComp<EngravedComponent>(target))
                {
                    _popup.PopupClient(Loc.GetString("engraving-popup-already"), user, user);
                    return;
                }

                if (!TryApplyEngraving(user, target, name, description.String))
                {
                    _popup.PopupClient(Loc.GetString("engraving-popup-invalid-name"), user, user);
                    return;
                }

                _popup.PopupClient(Loc.GetString("engraving-popup-success"), user, user);
            });
    }

    public bool TryApplyEngraving(EntityUid user, EntityUid target, string name, string description,
        EngravableComponent? engravable = null)
    {
        if (!Resolve(target, ref engravable, false) || HasComp<EngravedComponent>(target))
            return false;

        var cleanName = Clean(name, engravable.MaxNameLength);
        if (string.IsNullOrWhiteSpace(cleanName))
            return false;

        var cleanDescription = Clean(description, engravable.MaxDescriptionLength);
        var ownerName = Clean(MetaData(user).EntityName, 100);
        var metadata = MetaData(target);

        _meta.SetEntityName(target, cleanName, metadata);
        if (!string.IsNullOrWhiteSpace(cleanDescription))
            _meta.SetEntityDescription(target, cleanDescription, metadata);

        var engraved = EnsureComp<EngravedComponent>(target);
        engraved.OwnerName = ownerName;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):user} engraved {ToPrettyString(target):target}");

        return true;
    }

    private void OnExamined(Entity<EngravedComponent> ent, ref ExaminedEvent args)
    {
        if (string.IsNullOrWhiteSpace(ent.Comp.OwnerName))
            return;

        args.PushMarkup(Loc.GetString("engraving-examine-owner",
            ("owner", FormattedMessage.EscapeText(ent.Comp.OwnerName))));
    }

    private static string Clean(string value, int maxLength)
    {
        var clean = FormattedMessage.RemoveMarkupPermissive(value).Trim();
        return clean[..Math.Min(clean.Length, maxLength)];
    }
}
