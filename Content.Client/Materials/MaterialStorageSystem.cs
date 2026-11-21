using Content.Shared.Materials;
using Robust.Client.GameObjects;
using Content.Shared.Stacks;

namespace Content.Client.Materials;

public sealed class MaterialStorageSystem : SharedMaterialStorageSystem
{
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MaterialStorageComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(EntityUid uid, MaterialStorageComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!args.Sprite.LayerMapTryGet(MaterialStorageVisualLayers.Inserting, out var layer))
            return;

        if (!_appearance.TryGetData<bool>(uid, MaterialStorageVisuals.Inserting, out var inserting, args.Component))
            return;

        if (inserting && TryComp<InsertingMaterialStorageComponent>(uid, out var insertingComp))
        {
            args.Sprite.LayerSetAnimationTime(layer, 0f);

            args.Sprite.LayerSetVisible(layer, true);
            if (insertingComp.MaterialColor != null)
                args.Sprite.LayerSetColor(layer, insertingComp.MaterialColor.Value);
        }
        else
        {
            args.Sprite.LayerSetVisible(layer, false);
        }
    }

    public override bool TryInsertMaterialEntity(EntityUid user,
        EntityUid toInsert,
        EntityUid receiver,
        MaterialStorageComponent? storage = null,
        MaterialSiloUtilizerComponent? utilizer = null,
        MaterialComponent? material = null,
        PhysicalCompositionComponent? composition = null)
    {
        if (!TryStoreMaterialUnits(toInsert, receiver, out var inserted, storage, utilizer, material, composition))
            return false;
        if (TryComp<StackComponent>(toInsert, out var stack) && inserted < stack.Count)
            _stacks.SetCount(toInsert, stack.Count - inserted, stack);
        else
            _transform.DetachParentToNull(toInsert, Transform(toInsert));
        return true;
    }
}

public enum MaterialStorageVisualLayers : byte
{
    Inserting
}
