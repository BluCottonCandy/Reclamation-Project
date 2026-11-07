using Content.Server._NC.Trade;
namespace Content.Server._ReclamationProject.Supplies;
/// <summary>Use the existing town store's currency interface with a shared faction budget.</summary>
public sealed class FactionSupplyCurrencyHandler(FactionSupplySystem supplies) : ICurrencyHandler
{
    public bool CanHandle(string currencyId) => FactionSupplySystem.IsCurrency(currencyId);
    public bool TryGetBalance(in NcInventorySnapshot snapshot, string currencyId, out int balance)
    { balance = supplies.Balance(currencyId); return CanHandle(currencyId); }
    public bool TryTake(EntityUid user, string currencyId, int amount) => supplies.TrySpend(currencyId, amount);
    public bool TryGiveCurrency(EntityUid user, string currencyId, int amount) => supplies.Credit(currencyId, amount);
}
