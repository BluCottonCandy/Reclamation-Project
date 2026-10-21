using System;
using System.Diagnostics.CodeAnalysis;
using Content.Client.Guidebook.Controls;
using Content.Client.Guidebook.Richtext;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._ReclamationProject.Cookbook;

/// <summary>A standalone recipe card searchable by its dish name and ingredients.</summary>
[UsedImplicitly]
public sealed class CookbookRecipe : BoxContainer, IDocumentTag, ISearchableControl
{
    private string _searchText = string.Empty;

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        control = this;
        HorizontalExpand = true;
        Orientation = LayoutOrientation.Vertical;
        return args.TryGetValue("Search", out _searchText!);
    }

    public bool CheckMatchesSearch(string query) =>
        _searchText.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);

    public void SetHiddenState(bool state, string query) =>
        Visible = CheckMatchesSearch(query) ? state : !state;
}
