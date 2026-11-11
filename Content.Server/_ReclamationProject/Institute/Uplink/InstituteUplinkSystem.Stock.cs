using System.Linq;
using System.Text.Json;
using Content.Shared._ReclamationProject.Institute.Uplink;
using Content.Shared.FixedPoint;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._ReclamationProject.Institute.Uplink;

// #Reclamation Project - Director's stock: items the Director adds to every Institute uplink.
// Saved to the server's data folder, so the list carries over from round to round and across restarts.
public sealed partial class InstituteUplinkSystem
{
    [Dependency] private readonly IResourceManager _resources = default!;

    public static readonly ResPath StockPath = new("/institute_director_stock.json");
    public static readonly ProtoId<StoreCategoryPrototype> StockCategory = "InstituteDirectorStock";
    private const string StockListingPrefix = "InstituteDirectorStock_";

    /// <summary>Prototype id -> price in Research Points.</summary>
    private Dictionary<string, int>? _stock;

    private void InitializeStock()
    {
        SubscribeNetworkEvent<InstituteStockEditEvent>(OnStockEdit);
    }

    private Dictionary<string, int> Stock()
    {
        if (_stock != null)
            return _stock;

        _stock = new Dictionary<string, int>();
        try
        {
            if (_resources.UserData.Exists(StockPath))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, int>>(_resources.UserData.ReadAllText(StockPath));
                foreach (var (id, price) in saved ?? new())
                {
                    // Items removed from the game since the list was saved are skipped (and dropped on the next save).
                    if (_proto.TryIndex<EntityPrototype>(id, out var proto) && InstituteDirectorStock.IsStockable(proto))
                        _stock[id] = Math.Clamp(price, InstituteDirectorStock.MinPrice, InstituteDirectorStock.MaxPrice);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Couldn't read the Institute Director's stock from {StockPath}: {e}");
        }

        return _stock;
    }

    private void SaveStock()
    {
        try
        {
            _resources.UserData.WriteAllText(StockPath, JsonSerializer.Serialize(Stock()));
        }
        catch (Exception e)
        {
            Log.Error($"Couldn't save the Institute Director's stock to {StockPath}: {e}");
        }
    }

    public IReadOnlyDictionary<string, int> DirectorStock => Stock();

    /// <summary>Adds or reprices an item for every Institute uplink. Returns false if the item can't be stocked.</summary>
    public bool SetStock(string prototype, int price)
    {
        if (!_proto.TryIndex<EntityPrototype>(prototype, out var proto) || !InstituteDirectorStock.IsStockable(proto))
            return false;

        Stock()[prototype] = Math.Clamp(price, InstituteDirectorStock.MinPrice, InstituteDirectorStock.MaxPrice);
        SaveStock();
        RefreshStockAll();
        return true;
    }

    public bool RemoveStock(string prototype)
    {
        if (!Stock().Remove(prototype))
            return false;

        SaveStock();
        RefreshStockAll();
        return true;
    }

    private void OnStockEdit(InstituteStockEditEvent msg, EntitySessionEventArgs args)
    {
        if (!TryGetViewer(args.SenderSession, out _, out var viewer) || viewer.Job != Director)
            return;

        var name = _proto.TryIndex<EntityPrototype>(msg.Prototype, out var proto) ? proto.Name : msg.Prototype;
        if (msg.Remove)
        {
            if (RemoveStock(msg.Prototype))
                SendState(args.SenderSession, Loc.GetString("institute-network-stock-removed", ("item", name)));
            return;
        }

        if (msg.Price < InstituteDirectorStock.MinPrice || msg.Price > InstituteDirectorStock.MaxPrice)
        {
            SendState(args.SenderSession, Loc.GetString("institute-network-stock-bad-price",
                ("min", InstituteDirectorStock.MinPrice), ("max", InstituteDirectorStock.MaxPrice)));
            return;
        }

        SendState(args.SenderSession, SetStock(msg.Prototype, msg.Price)
            ? Loc.GetString("institute-network-stock-added", ("item", name), ("price", msg.Price))
            : Loc.GetString("institute-network-stock-bad-item"));
    }

    private void RefreshStockAll()
    {
        var query = EntityQueryEnumerator<InstituteUplinkComponent, StoreComponent>();
        while (query.MoveNext(out var uid, out _, out var store))
        {
            RefreshStock(uid, store);
        }
    }

    /// <summary>Replaces this uplink's Director's stock listings with the current list.</summary>
    private void RefreshStock(EntityUid implant, StoreComponent store)
    {
        store.FullListingsCatalog.RemoveWhere(l => l.ID.StartsWith(StockListingPrefix));
        foreach (var (id, price) in Stock())
        {
            store.FullListingsCatalog.Add(StockListing(id, price));
        }

        _store.UpdateUserInterface(null, implant, store);
    }

    private static ListingDataWithCostModifiers StockListing(string prototype, int price)
    {
        // Name, description and icon come from the item itself.
        var data = new ListingData(
            name: null,
            discountCategory: null,
            description: null,
            conditions: null,
            icon: null,
            priority: 0,
            productEntity: prototype,
            productAction: null,
            productUpgradeId: null,
            productActionEntity: null,
            productEvent: null,
            raiseProductEventOnUser: false,
            purchaseAmount: 0,
            id: StockListingPrefix + prototype,
            categories: new HashSet<ProtoId<StoreCategoryPrototype>> { StockCategory },
            originalCost: new Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2> { { Currency, price } },
            restockTime: TimeSpan.Zero,
            dataDiscountDownTo: new Dictionary<ProtoId<CurrencyPrototype>, FixedPoint2>());
        return new ListingDataWithCostModifiers(data);
    }

    private List<InstituteStockEntry> StockEntries()
    {
        return Stock()
            .Select(pair => new InstituteStockEntry
            {
                Prototype = pair.Key,
                Name = _proto.TryIndex<EntityPrototype>(pair.Key, out var proto) ? proto.Name : pair.Key,
                Price = pair.Value,
            })
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
