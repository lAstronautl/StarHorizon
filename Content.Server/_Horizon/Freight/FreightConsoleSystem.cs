using System.Text;
using Content.Server._NF.Bank;
using Content.Server._NF.Trade;
using Content.Server.Cargo.Components;
using Content.Server.Hands.Systems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._Horizon.Freight;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Coordinates;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Horizon.Freight;

/// <summary>
/// Freight console. Flow: pick an offered order -> a manifest is printed -> the manifest is handed in to a
/// freight console at its loading point -> crates are released -> each crate is delivered to its own address
/// and sold on sell pallets (credits, trade tokens and a share into the ATM account of the delivery grid).
/// </summary>
public sealed class FreightConsoleSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly HashSet<EntityUid> _setEnts = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FreightConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<FreightConsoleComponent, EntInsertedIntoContainerMessage>(OnSlotInserted);
        SubscribeLocalEvent<FreightConsoleComponent, EntRemovedFromContainerMessage>(OnSlotRemoved);
        SubscribeLocalEvent<FreightConsoleComponent, FreightTakeOfferMessage>(OnTakeOffer);
        SubscribeLocalEvent<FreightConsoleComponent, FreightSpawnMessage>(OnSpawn);
        SubscribeLocalEvent<FreightConsoleComponent, FreightSellMessage>(OnSell);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FreightConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextRefresh)
                continue;

            RefreshOffers((uid, comp));
        }
    }

    private void OnUiOpened(Entity<FreightConsoleComponent> ent, ref BoundUIOpenedEvent args) => UpdateUi(ent);

    private void OnSlotInserted(Entity<FreightConsoleComponent> ent, ref EntInsertedIntoContainerMessage args) => UpdateUi(ent);

    private void OnSlotRemoved(Entity<FreightConsoleComponent> ent, ref EntRemovedFromContainerMessage args) => UpdateUi(ent);

    #region Addresses

    /// <summary>
    /// Freight address of the grid this console stands on: grid/station FreightDestination, then the NF trade
    /// destination of the station (depot letters A/B/C/D are assigned there after spawn), then the console's own.
    /// </summary>
    private ProtoId<FreightDestinationPrototype>? GetDestination(Entity<FreightConsoleComponent> console)
    {
        var grid = Transform(console).GridUid;
        if (grid != null)
        {
            if (TryComp<FreightDestinationComponent>(grid, out var gridDest))
                return gridDest.Id;

            var station = _station.GetOwningStation(grid.Value);
            if (station != null)
            {
                if (TryComp<FreightDestinationComponent>(station, out var stationDest))
                    return stationDest.Id;

                if (TryComp<TradeCrateDestinationComponent>(station, out var trade)
                    && _proto.HasIndex<FreightDestinationPrototype>(trade.DestinationProto.Id))
                {
                    return new ProtoId<FreightDestinationPrototype>(trade.DestinationProto.Id);
                }
            }
        }

        return console.Comp.Destination;
    }

    /// <summary>All freight addresses that currently exist in the sector.</summary>
    private HashSet<ProtoId<FreightDestinationPrototype>> GetAvailableDestinations()
    {
        var result = new HashSet<ProtoId<FreightDestinationPrototype>>();

        var freight = EntityQueryEnumerator<FreightDestinationComponent>();
        while (freight.MoveNext(out _, out var dest))
        {
            result.Add(dest.Id);
        }

        var trade = EntityQueryEnumerator<TradeCrateDestinationComponent>();
        while (trade.MoveNext(out _, out var dest))
        {
            if (_proto.HasIndex<FreightDestinationPrototype>(dest.DestinationProto.Id))
                result.Add(new ProtoId<FreightDestinationPrototype>(dest.DestinationProto.Id));
        }

        return result;
    }

    private string DestinationName(ProtoId<FreightDestinationPrototype>? id)
    {
        return id is { } d && _proto.TryIndex(d, out var proto)
            ? Loc.GetString(proto.Name)
            : Loc.GetString("freight-no-destination");
    }

    #endregion

    #region Offers

    private void RefreshOffers(Entity<FreightConsoleComponent> ent)
    {
        var offers = GenerateOffers(ent);
        ent.Comp.Offers = offers;

        // No orders possible yet (e.g. other POIs have not spawned) - retry soon.
        ent.Comp.NextRefresh = _timing.CurTime + (offers.Count == 0 ? RetryDelay : ent.Comp.RefreshInterval);
        UpdateUi(ent);
    }

    private List<FreightOffer> GenerateOffers(Entity<FreightConsoleComponent> console)
    {
        var result = new List<FreightOffer>();
        var here = GetDestination(console);
        var available = GetAvailableDestinations();

        var orders = new List<FreightOrderPrototype>(_proto.EnumeratePrototypes<FreightOrderPrototype>());
        if (orders.Count == 0)
            return result;

        for (var i = 0; i < console.Comp.OfferCount; i++)
        {
            // A few attempts per slot: prefer orders not offered yet.
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var order = PickOrder(orders);
                if (attempt < 8 && result.Exists(o => o.OrderId == order.ID))
                    continue;

                if (!TryBuildOffer(order, here, available, out var offer))
                    continue;

                result.Add(offer);
                break;
            }
        }

        return result;
    }

    private FreightOrderPrototype PickOrder(List<FreightOrderPrototype> orders)
    {
        var total = 0f;
        foreach (var o in orders)
            total += Math.Max(o.Weight, 0f);

        if (total <= 0f)
            return _random.Pick(orders);

        var roll = _random.NextFloat() * total;
        foreach (var o in orders)
        {
            roll -= Math.Max(o.Weight, 0f);
            if (roll <= 0f)
                return o;
        }

        return orders[^1];
    }

    private bool TryBuildOffer(FreightOrderPrototype order, ProtoId<FreightDestinationPrototype>? here,
        HashSet<ProtoId<FreightDestinationPrototype>> available, out FreightOffer offer)
    {
        offer = default!;

        // Loading point: another existing address (never this console's own).
        var sources = new List<ProtoId<FreightDestinationPrototype>>();
        foreach (var d in available)
        {
            if (d == here)
                continue;

            if (order.Sources.Count > 0 && !order.Sources.Contains(d))
                continue;

            sources.Add(d);
        }

        if (sources.Count == 0)
            return false;

        var source = _random.Pick(sources);
        var entries = new List<FreightResolvedEntry>();

        foreach (var entry in order.Entries)
        {
            if (!_proto.HasIndex(entry.Crate) || !_proto.TryIndex(entry.Pool, out var pool))
            {
                Log.Warning($"Freight order {order.ID} has an invalid entry ({entry.Crate}/{entry.Pool})");
                return false;
            }

            var recipients = pool.Recipients.FindAll(r => available.Contains(r.Destination) && r.Destination != source);
            if (recipients.Count == 0)
                return false;

            // Group crates of the same kind going to the same recipient.
            var groups = new Dictionary<FreightRecipient, int>();
            for (var i = 0; i < entry.Count; i++)
            {
                var r = PickRecipient(recipients);
                groups[r] = groups.GetValueOrDefault(r) + 1;
            }

            foreach (var (r, count) in groups)
            {
                entries.Add(new FreightResolvedEntry
                {
                    Crate = entry.Crate,
                    Count = count,
                    Destination = r.Destination,
                    Credits = r.Credits,
                    Tokens = r.Tokens,
                    BankPayout = r.BankPayout,
                });
            }
        }

        if (entries.Count == 0)
            return false;

        offer = new FreightOffer
        {
            OrderId = order.ID,
            Name = Loc.GetString(order.Name),
            Description = order.Description is { } desc ? Loc.GetString(desc) : string.Empty,
            Source = source,
            Entries = entries,
        };
        return true;
    }

    private FreightRecipient PickRecipient(List<FreightRecipient> recipients)
    {
        var total = 0f;
        foreach (var r in recipients)
            total += Math.Max(r.Weight, 0f);

        if (total <= 0f)
            return _random.Pick(recipients);

        var roll = _random.NextFloat() * total;
        foreach (var r in recipients)
        {
            roll -= Math.Max(r.Weight, 0f);
            if (roll <= 0f)
                return r;
        }

        return recipients[^1];
    }

    #endregion

    #region Manifest printing

    private void OnTakeOffer(Entity<FreightConsoleComponent> ent, ref FreightTakeOfferMessage args)
    {
        if (args.Index < 0 || args.Index >= ent.Comp.Offers.Count)
        {
            Reject(ent, args.Actor, "freight-console-offer-gone");
            return;
        }

        if (_timing.CurTime < ent.Comp.NextPrintTime)
        {
            Reject(ent, args.Actor, "freight-console-printing");
            return;
        }

        var offer = ent.Comp.Offers[args.Index];
        ent.Comp.Offers.RemoveAt(args.Index);
        ent.Comp.NextPrintTime = _timing.CurTime + ent.Comp.PrintDelay;

        var paper = SpawnAtPosition(ent.Comp.ManifestPrototype, Transform(ent).Coordinates);
        var manifest = EnsureComp<FreightManifestComponent>(paper);
        manifest.Source = offer.Source;
        manifest.Addressee = offer.Name;
        manifest.Entries = offer.Entries;

        _meta.SetEntityName(paper, Loc.GetString("freight-manifest-name", ("order", offer.Name)));
        if (TryComp<PaperComponent>(paper, out var paperComp))
            _paper.SetContent((paper, paperComp), BuildManifestText(offer));

        _hands.TryPickupAnyHand(args.Actor, paper);
        _audio.PlayPvs(ent.Comp.PrintSound, ent);
        UpdateUi(ent);
    }

    private string BuildManifestText(FreightOffer offer)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.GetString("freight-manifest-title", ("order", offer.Name)));
        sb.AppendLine(Loc.GetString("freight-manifest-source", ("source", DestinationName(offer.Source))));
        sb.AppendLine();

        foreach (var e in offer.Entries)
        {
            var crateName = _proto.TryIndex(e.Crate, out var crate) ? crate.Name : e.Crate.Id;
            sb.AppendLine(Loc.GetString("freight-manifest-line",
                ("name", crateName), ("count", e.Count), ("destination", DestinationName(e.Destination)),
                ("credits", e.Credits), ("tokens", e.Tokens)));
        }

        sb.AppendLine();
        sb.AppendLine(Loc.GetString("freight-manifest-footer", ("source", DestinationName(offer.Source))));
        return sb.ToString();
    }

    #endregion

    #region Pallets

    private List<EntityUid> GetPallets(Entity<FreightConsoleComponent> console, BuySellType type)
    {
        var result = new List<EntityUid>();
        var consoleXform = Transform(console);
        if (consoleXform.GridUid is not { } grid)
            return result;

        var query = AllEntityQuery<CargoPalletComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pallet, out var xform))
        {
            if (xform.ParentUid != grid || !xform.Anchored || (type & pallet.PalletType) == 0)
                continue;

            if ((xform.Coordinates.Position - consoleXform.Coordinates.Position).Length() > console.Comp.PalletDistance)
                continue;

            result.Add(uid);
        }

        return result;
    }

    private static bool Matches(FreightCrateComponent crate, ProtoId<FreightDestinationPrototype>? here)
    {
        // A crate without an address is accepted by any freight console.
        return crate.Destination == null || crate.Destination == here;
    }

    #endregion

    #region Spawn

    private void OnSpawn(Entity<FreightConsoleComponent> ent, ref FreightSpawnMessage args)
    {
        var manifestUid = ent.Comp.ManifestSlot.Item;
        if (manifestUid == null || !TryComp<FreightManifestComponent>(manifestUid, out var manifest))
        {
            Reject(ent, args.Actor, "freight-console-no-manifest");
            return;
        }

        // The manifest has its own address: it is only valid at its loading point.
        var here = GetDestination(ent);
        if (manifest.Source != null && manifest.Source != here)
        {
            _audio.PlayPvs(ent.Comp.ErrorSound, ent);
            _popup.PopupEntity(Loc.GetString("freight-console-wrong-source", ("source", DestinationName(manifest.Source))),
                ent, args.Actor);
            return;
        }

        var pallets = GetPallets(ent, BuySellType.Buy);
        var index = 0;
        var consoleCoords = ent.Owner.ToCoordinates();

        foreach (var entry in manifest.Entries)
        {
            if (!_proto.HasIndex(entry.Crate))
            {
                Log.Warning($"Freight manifest {ToPrettyString(manifestUid.Value)} has an invalid crate {entry.Crate}");
                continue;
            }

            var destName = DestinationName(entry.Destination);
            for (var i = 0; i < entry.Count; i++)
            {
                var coords = pallets.Count > 0
                    ? Transform(pallets[index++ % pallets.Count]).Coordinates
                    : consoleCoords;

                var crate = Spawn(entry.Crate, coords);
                var comp = EnsureComp<FreightCrateComponent>(crate);
                comp.Destination = entry.Destination;
                comp.Credits = entry.Credits;
                comp.Tokens = entry.Tokens;
                comp.BankPayout = entry.BankPayout;

                _meta.SetEntityDescription(crate,
                    Loc.GetString("freight-crate-description", ("destination", destName), ("addressee", manifest.Addressee)));
            }
        }

        Del(manifestUid.Value);
        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        _popup.PopupEntity(Loc.GetString("freight-console-spawned"), ent, args.Actor);
        UpdateUi(ent);
    }

    #endregion

    #region Sell

    private List<(EntityUid Uid, FreightCrateComponent Crate)> GetCrates(Entity<FreightConsoleComponent> console)
    {
        var result = new List<(EntityUid, FreightCrateComponent)>();
        var seen = new HashSet<EntityUid>();

        foreach (var pallet in GetPallets(console, BuySellType.Sell))
        {
            _setEnts.Clear();
            _lookup.GetEntitiesIntersecting(pallet, _setEnts, LookupFlags.Dynamic | LookupFlags.Sundries);
            foreach (var ent in _setEnts)
            {
                if (!TryComp<FreightCrateComponent>(ent, out var crate) || Transform(ent).Anchored || !seen.Add(ent))
                    continue;

                result.Add((ent, crate));
            }
        }

        return result;
    }

    private void OnSell(Entity<FreightConsoleComponent> ent, ref FreightSellMessage args)
    {
        var here = GetDestination(ent);
        var toSell = new List<(EntityUid Uid, FreightCrateComponent Crate)>();
        foreach (var c in GetCrates(ent))
        {
            if (Matches(c.Crate, here))
                toSell.Add(c);
        }

        if (toSell.Count == 0)
        {
            Reject(ent, args.Actor, "freight-console-nothing-to-sell");
            return;
        }

        int credits = 0, tokens = 0, bank = 0;
        foreach (var (uid, crate) in toSell)
        {
            credits += crate.Credits;
            tokens += crate.Tokens;
            bank += crate.BankPayout;
            Del(uid);
        }

        Give(args.Actor, ent.Comp.CashType, credits);
        Give(args.Actor, ent.Comp.TokenType, tokens);

        if (bank > 0 && TryGetGridBankAccount(ent) is { } account)
            _bank.TrySectorDeposit(account, bank, LedgerEntryType.CargoTax);

        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        _popup.PopupEntity(Loc.GetString("freight-console-sold",
            ("count", toSell.Count), ("credits", credits), ("tokens", tokens)), ent, args.Actor);
        UpdateUi(ent);
    }

    private void Give(EntityUid actor, string stackProto, int amount)
    {
        if (amount <= 0)
            return;

        var uid = _stack.Spawn(amount, stackProto, actor.ToCoordinates());
        _hands.TryPickupAnyHand(actor, uid);
    }

    /// <summary>The bank account of the first station bank ATM standing on the console's grid.</summary>
    private SectorBankAccount? TryGetGridBankAccount(Entity<FreightConsoleComponent> console)
    {
        var grid = Transform(console).GridUid;
        if (grid == null)
            return null;

        var query = AllEntityQuery<StationBankATMComponent, TransformComponent>();
        while (query.MoveNext(out _, out var atm, out var xform))
        {
            if (xform.GridUid == grid && atm.Account != SectorBankAccount.Invalid)
                return atm.Account;
        }

        return null;
    }

    #endregion

    #region UI

    private void Reject(Entity<FreightConsoleComponent> ent, EntityUid actor, string locId)
    {
        _audio.PlayPvs(ent.Comp.ErrorSound, ent);
        _popup.PopupEntity(Loc.GetString(locId), ent, actor);
    }

    private void UpdateUi(Entity<FreightConsoleComponent> ent)
    {
        var here = GetDestination(ent);

        ProtoId<FreightDestinationPrototype>? manifestSource = null;
        var hasManifest = false;
        var manifestEntries = new List<FreightResolvedEntry>();
        if (ent.Comp.ManifestSlot.Item is { } item && TryComp<FreightManifestComponent>(item, out var manifest))
        {
            hasManifest = true;
            manifestSource = manifest.Source;
            manifestEntries = manifest.Entries;
        }

        int sellable = 0, wrong = 0, value = 0;
        foreach (var (_, crate) in GetCrates(ent))
        {
            if (Matches(crate, here))
            {
                sellable++;
                value += crate.Credits;
            }
            else
            {
                wrong++;
            }
        }

        _ui.SetUiState(ent.Owner, FreightConsoleUiKey.Key,
            new FreightConsoleBuiState(here, ent.Comp.Offers, ent.Comp.NextRefresh, hasManifest, manifestSource,
                manifestEntries, sellable, wrong, value));
    }

    #endregion
}
