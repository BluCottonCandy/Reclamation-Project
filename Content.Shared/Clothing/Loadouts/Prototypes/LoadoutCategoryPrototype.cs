using Robust.Shared.Prototypes;

namespace Content.Shared.Clothing.Loadouts.Prototypes;


/// <summary>
///     A prototype defining a valid category for <see cref="LoadoutPrototype"/>s to go into.
/// </summary>
[Prototype]
public sealed partial class LoadoutCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;

    [DataField]
    public bool Root;

    /// <summary>Only one loadout from this category may be selected and spawned.</summary>
    [DataField]
    public bool SingleSelection;

    /// <summary>Entity preview shown on the single-selection dropdown.</summary>
    [DataField]
    public ProtoId<EntityPrototype>? SelectionIcon;

    [DataField]
    public List<ProtoId<LoadoutCategoryPrototype>> SubCategories = new();
}
