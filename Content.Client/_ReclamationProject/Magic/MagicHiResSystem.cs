using System.Numerics;
using Content.Client.Clothing;
using Content.Client.Items.Systems;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Clothing;
using Content.Shared.Hands;
using Robust.Shared.GameObjects;

namespace Content.Client._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - magic weapons whose held and worn sprites are drawn at double resolution (so every pixel of
/// the art survives) are shown at half scale here, the same size as normal sprites.
/// </summary>
public sealed class MagicHiResSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicHiResComponent, GetInhandVisualsEvent>(OnInhand, after: new[] { typeof(ItemSystem) });
        SubscribeLocalEvent<MagicHiResComponent, GetEquipmentVisualsEvent>(OnEquipped, after: new[] { typeof(ClientClothingSystem) });
    }

    private void OnInhand(EntityUid uid, MagicHiResComponent comp, GetInhandVisualsEvent args) => Shrink(args.Layers, comp.Scale);

    private void OnEquipped(EntityUid uid, MagicHiResComponent comp, GetEquipmentVisualsEvent args) => Shrink(args.Layers, comp.Scale);

    private static void Shrink(List<(string, PrototypeLayerData)> layers, float scale)
    {
        foreach (var (_, layer) in layers)
            layer.Scale = new Vector2(scale, scale);
    }
}
