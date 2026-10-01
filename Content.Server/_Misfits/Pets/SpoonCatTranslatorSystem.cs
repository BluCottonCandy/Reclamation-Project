// #Misfits Add - Spoon cat action for teaching targets to understand Cat.

using Content.Server.Language;
using Content.Shared._Misfits.Pets;
using Content.Shared.Actions;
using Content.Shared.Language;
using Content.Shared.Language.Components;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Pets;

public sealed class SpoonCatTranslatorSystem : EntitySystem
{
    private static readonly ProtoId<LanguagePrototype> CatLanguage = "Cat";

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SpoonCatTranslatorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SpoonCatTranslatorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SpoonCatTranslatorComponent, SpoonCatTranslatorActionEvent>(OnTranslate);
    }

    private void OnMapInit(Entity<SpoonCatTranslatorComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.ActionId);
    }

    private void OnShutdown(Entity<SpoonCatTranslatorComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent, ent.Comp.ActionEntity);
    }

    private void OnTranslate(Entity<SpoonCatTranslatorComponent> ent, ref SpoonCatTranslatorActionEvent args)
    {
        if (args.Handled)
            return;

        if (!HasComp<LanguageSpeakerComponent>(args.Target))
        {
            _popup.PopupEntity(Loc.GetString("spoon-cat-translator-invalid"), args.Performer, args.Performer);
            return;
        }

        if (_language.CanUnderstand(args.Target, CatLanguage))
        {
            _popup.PopupEntity(Loc.GetString("spoon-cat-translator-already"), args.Performer, args.Performer);
            return;
        }

        _language.AddLanguage(args.Target, CatLanguage, addSpoken: false);
        _popup.PopupEntity(Loc.GetString("spoon-cat-translator-success"), args.Target, args.Target);
        _popup.PopupEntity(Loc.GetString("spoon-cat-translator-success-user", ("target", args.Target)), args.Target, args.Performer);
        args.Handled = true;
    }
}
