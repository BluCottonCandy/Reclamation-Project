// #Reclamation Project - medieval weapons: in-hand and worn sprites drawn from the full-detail art.
using System.Numerics;
using Content.Client.Clothing;
using Content.Client.Items.Systems;
using Content.Shared._ReclamationProject.Medieval;
using Content.Shared.Clothing;
using Content.Shared.Hands;
using Robust.Shared.GameObjects;

namespace Content.Client._ReclamationProject.Medieval;

public sealed class MedievalHiResSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedievalHiResComponent, GetInhandVisualsEvent>(OnInhand, after: new[] { typeof(ItemSystem) });
        SubscribeLocalEvent<MedievalHiResComponent, GetEquipmentVisualsEvent>(OnEquipped, after: new[] { typeof(ClientClothingSystem) });
    }

    private void OnInhand(EntityUid uid, MedievalHiResComponent comp, GetInhandVisualsEvent args) => Shrink(args.Layers, comp.Scale);

    private void OnEquipped(EntityUid uid, MedievalHiResComponent comp, GetEquipmentVisualsEvent args) => Shrink(args.Layers, comp.Scale);

    private static void Shrink(List<(string, PrototypeLayerData)> layers, float scale)
    {
        foreach (var (_, layer) in layers)
            layer.Scale = new Vector2(scale, scale);
    }
}
