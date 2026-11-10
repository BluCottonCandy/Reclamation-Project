using System.Linq;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Server.Inventory;
using Content.Shared.Lock;
using Content.Shared.Mind.Components;
using Content.Shared.Roles.Jobs;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.GameTicking;
using Content.Server.Storage.Components;
using Content.Shared._ReclamationProject.Supplies;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
namespace Content.Server._ReclamationProject.Supplies;
/// <summary>One round-local budget per faction, independent of vendor/player count.</summary>
public sealed class FactionSupplySystem : EntitySystem
{
    public const int Capacity = 200;
    public const int IncomePerMinute = 3;
    public static readonly string[] CratePrototypes = { "ReclamationSupplyCrate", "ReclamationSupplyCrateBlue", "ReclamationSupplyCrateRed" };
    public static bool IsCrateProduct(string product) => CratePrototypes.Contains(product);
    public int GetCrateReward(EntityUid crate) => TryComp<FactionSupplyCrateComponent>(crate, out var supply) ? supply.Reward : 0;
    private bool MatchesCrateProduct(EntityUid crate, string? product) => product == null || MetaData(crate).EntityPrototype?.ID == product;
    public const string CurrencyPrefix = "Supply points: ";
    public static readonly string[] Factions = { "Brotherhood", "Town", "Minutemen", "Forged", "Railroad", "Institute", "Triggermen", "Pack" };
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    private sealed class Budget { public int Points = 50; public long Minute; }
    private readonly Dictionary<string, Budget> _budgets = new();
    private TimeSpan? _roundStart;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartedEvent>(_ => BeginRound());
        SubscribeLocalEvent<Content.Shared.GameTicking.RoundRestartCleanupEvent>(_ => Reset());
        SubscribeLocalEvent<FactionSupplyVendorComponent, InteractUsingEvent>(OnUseWithItem);
        SubscribeLocalEvent<FactionSupplyCrateComponent, ContainerGettingInsertedAttemptEvent>(OnInsert);
        SubscribeLocalEvent<FactionSupplyCrateComponent, LockToggleAttemptEvent>(OnLockToggle);
        SubscribeLocalEvent<FactionSupplyCrateComponent, PriceCalculationEvent>(OnCratePrice);
        SubscribeLocalEvent<FactionSupplyPurchaseComponent, PriceCalculationEvent>(OnPurchasePrice);
        SubscribeLocalEvent<FactionSupplyPurchaseComponent, StackSplitEvent>(OnSplit);
    }
    private void OnUseWithItem(EntityUid uid, FactionSupplyVendorComponent comp, InteractUsingEvent args)
    {
        if (args.Handled) return;
        // Use the normal activation path, including its UI/access/interaction checks,
        // when the selected hand contains an item instead of being empty.
        args.Handled = EntityManager.System<SharedInteractionSystem>().InteractionActivate(args.User, uid);
    }
    public void BeginRound() { Reset(); _roundStart = _timing.CurTime; }
    private void Reset() { _budgets.Clear(); _roundStart = null; }
    public static bool IsCurrency(string currency) => currency.StartsWith(CurrencyPrefix, StringComparison.Ordinal)
        && Factions.Contains(currency[CurrencyPrefix.Length..]);
    private Budget Account(string currency)
    {
        if (!_budgets.TryGetValue(currency, out var budget)) _budgets[currency] = budget = new();
        var minute = _roundStart is {} start ? Math.Max(0, (long) ((_timing.CurTime - start).TotalMinutes)) : 0;
        if (minute > budget.Minute)
        {
            budget.Points = (int) Math.Min(Capacity, budget.Points + (minute - budget.Minute) * IncomePerMinute);
            budget.Minute = minute;
        }
        return budget;
    }
    public bool CanUseVendor(EntityUid machine, EntityUid user)
    {
        if (!TryComp<FactionSupplyVendorComponent>(machine, out var vendor) || !vendor.RequireFactionJob) return true;
        return TryComp<MindContainerComponent>(user, out var mind)
            && EntityManager.System<SharedJobSystem>().MindTryGetJob(mind.Mind, out _, out var job)
            && vendor.JobRanks.ContainsKey(job.ID);
    }
    public bool CanPurchase(EntityUid machine, EntityUid user, string product)
    {
        if (!CanUseVendor(machine, user)) return false;
        if (!TryComp<FactionSupplyVendorComponent>(machine, out var vendor)
            || !vendor.EquipmentRanks.TryGetValue(product, out var required)) return true;
        return TryComp<MindContainerComponent>(user, out var mind)
            && EntityManager.System<SharedJobSystem>().MindTryGetJob(mind.Mind, out _, out var job)
            && vendor.JobRanks.TryGetValue(job.ID, out var rank) && rank >= required;
    }
    public int Balance(string currency) => IsCurrency(currency) ? Account(currency).Points : 0;
    public bool TrySpend(string currency, int amount)
    {
        if (!IsCurrency(currency) || amount < 0) return false;
        var account = Account(currency);
        if (account.Points < amount) return false;
        account.Points -= amount; return true;
    }
    public bool Credit(string currency, int amount)
    {
        if (!IsCurrency(currency) || amount < 0) return false;
        var account = Account(currency); account.Points = (int) Math.Min(Capacity, (long) account.Points + amount); return true;
    }
    public bool TryGetPulledCrate(EntityUid user, out EntityUid crate)
    {
        crate = default;
        if (!TryComp<PullerComponent>(user, out var puller) || puller.Pulling is not {} pulled
            || !Exists(pulled) || !HasComp<FactionSupplyCrateComponent>(pulled)
            || !TryComp<PullableComponent>(pulled, out var pullable) || pullable.Puller != user) return false;
        crate = pulled;
        return true;
    }
    private bool InSaleRange(EntityUid user, EntityUid machine, EntityUid crate)
    {
        var xform = EntityManager.System<SharedTransformSystem>();
        return xform.InRange(Transform(user).Coordinates, Transform(machine).Coordinates, 2.5f)
            && xform.InRange(Transform(crate).Coordinates, Transform(machine).Coordinates, 4f)
            && xform.InRange(Transform(user).Coordinates, Transform(crate).Coordinates, 2.5f);
    }
    public EntityUid? GetRedeemableCrate(EntityUid user, EntityUid machine, string? product = null)
    {
        foreach (var held in _hands.EnumerateHeld(user))
            if (HasComp<FactionSupplyCrateComponent>(held) && MatchesCrateProduct(held, product) && InSaleRange(user, machine, held)) return held;
        return TryGetPulledCrate(user, out var pulled) && MatchesCrateProduct(pulled, product) && InSaleRange(user, machine, pulled) ? pulled : null;
    }
    public bool RedeemCrate(EntityUid user, EntityUid machine, string currency, EntityUid? requestedCrate = null, string? product = null)
    {
        if (!CanUseVendor(machine, user) || !IsCurrency(currency) || Balance(currency) >= Capacity) return false;
        var candidate = requestedCrate ?? GetRedeemableCrate(user, machine, product);
        if (candidate is not {} crate || !Exists(crate) || !HasComp<FactionSupplyCrateComponent>(crate)
            || !MatchesCrateProduct(crate, product) || !InSaleRange(user, machine, crate)) return false;
        if (!_hands.EnumerateHeld(user).Contains(crate) && !(TryGetPulledCrate(user, out var pulled) && pulled == crate)) return false;
        // Immediate deletion prevents repeat requests from redeeming the same crate twice.
        var reward = GetCrateReward(crate);
        if (reward <= 0) return false;
        // Stop dragging while the crate still exists so the normal hand/pull cleanup
        // can run before its backing entity is deleted and replicated to clients.
        if (TryComp<PullableComponent>(crate, out var pullable) && pullable.Puller != null
            && !EntityManager.System<PullingSystem>().TryStopPull(crate, pullable, user)) return false;
        EntityManager.System<VirtualItemSystem>().DeleteInHandsMatching(user, crate);
        Del(crate); Credit(currency, reward); return true;
    }
    public void MarkPurchase(EntityUid uid)
    {
        if (!Exists(uid)) return;
        EnsureComp<FactionSupplyPurchaseComponent>(uid);
        EnsureComp<CargoSellBlacklistComponent>(uid);
        var children = Transform(uid).ChildEnumerator;
        while (children.MoveNext(out var child)) MarkPurchase(child);
    }
    private void OnLockToggle(EntityUid uid, FactionSupplyCrateComponent comp, ref LockToggleAttemptEvent args) => args.Cancelled = true;
    private void OnInsert(EntityUid uid, FactionSupplyCrateComponent comp, ContainerGettingInsertedAttemptEvent args)
    {
        if (HasComp<StorageComponent>(args.Container.Owner) || HasComp<EntityStorageComponent>(args.Container.Owner)) args.Cancel();
    }
    private void OnSplit(EntityUid uid, FactionSupplyPurchaseComponent comp, ref StackSplitEvent args) => MarkPurchase(args.NewId);
    private void OnCratePrice(EntityUid uid, FactionSupplyCrateComponent comp, ref PriceCalculationEvent args) { args.Price = 0; args.Handled = true; }
    private void OnPurchasePrice(EntityUid uid, FactionSupplyPurchaseComponent comp, ref PriceCalculationEvent args) { args.Price = 0; args.Handled = true; }
}
