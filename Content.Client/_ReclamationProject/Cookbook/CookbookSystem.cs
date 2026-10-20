using System.Linq;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Controls;
using Content.Shared._ReclamationProject.Cookbook;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.Cookbook;

public sealed class CookbookSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    public GuidebookWindow? Window { get; private set; }

    public override void Initialize()
    {
        SubscribeLocalEvent<CookbookComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<CookbookComponent, UseInHandEvent>(OnUse);
        SubscribeLocalEvent<CookbookComponent, GetVerbsEvent<ExamineVerb>>(OnVerbs);
    }

    private void OnActivate(EntityUid uid, CookbookComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled || !_timing.IsFirstTimePredicted) return;
        OpenBook(component);
        args.Handled = true;
    }

    private void OnUse(EntityUid uid, CookbookComponent component, UseInHandEvent args)
    {
        if (args.Handled || !_timing.IsFirstTimePredicted) return;
        OpenBook(component);
        args.Handled = true;
    }

    private void OnVerbs(EntityUid uid, CookbookComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract) return;
        args.Verbs.Add(new ExamineVerb
        {
            Text = Loc.GetString("cookbook-read"),
            Act = () => OpenBook(component),
            ClientExclusive = true,
            CloseMenu = true,
        });
    }

    public void OpenBook(CookbookComponent component)
    {
        if (component.Chapters.Count == 0) return;
        var entries = component.Chapters.ToDictionary(pair => pair.Key, pair => new GuideEntry
        {
            Id = pair.Key,
            Name = pair.Value.Name,
            Text = pair.Value.Text,
            Priority = pair.Value.Order,
            FilterEnabled = pair.Value.Order != 0,
        });
        Window ??= new GuidebookWindow { Title = Loc.GetString("cookbook-title") };
        Window.UpdateGuides(entries, selected: entries.OrderBy(pair => pair.Value.Priority).First().Key);
        Window.OpenCentered();
    }

    public override void Shutdown()
    {
        Window?.Dispose();
        Window = null;
        base.Shutdown();
    }
}
