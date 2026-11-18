// #Reclamation Project - medieval weapons: blacksmith-only recipes on the forge.
using Content.Server.Popups;
using Content.Shared._ReclamationProject.Medieval;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Medieval;

public sealed class BlacksmithLatheSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BlacksmithLatheComponent, LatheGetRecipesEvent>(OnGetRecipes);
    }

    private void OnGetRecipes(EntityUid uid, BlacksmithLatheComponent comp, LatheGetRecipesEvent args)
    {
        foreach (var recipe in _proto.EnumeratePrototypes<LatheRecipePrototype>())
        {
            if (recipe.Category is not { } cat || !comp.Categories.Contains(cat))
                continue;
            if (!args.Recipes.Contains(recipe.ID))
                args.Recipes.Add(recipe.ID);
        }
    }

    /// <summary>
    /// False (with a popup) when <paramref name="user"/> tries to queue a blacksmith recipe without the trait.
    /// </summary>
    public bool CanQueue(EntityUid lathe, EntityUid user, LatheRecipePrototype recipe)
    {
        if (!TryComp<BlacksmithLatheComponent>(lathe, out var comp))
            return true;
        if (recipe.Category is not { } cat || !comp.Categories.Contains(cat))
            return true;
        if (HasComp<BlacksmithComponent>(user))
            return true;

        _popup.PopupEntity(Loc.GetString(comp.DeniedPopup), lathe, user);
        return false;
    }
}
