using Content.Server._NF.Bank;
using Content.Server.Cargo.Components;
using Content.Server.Hands.Systems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._Horizon.Freight;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Coordinates;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Horizon.Freight;

/// <summary>
/// Freight console: a manifest inserted into it spawns cargo crates on the grid's buy pallets;
/// crates delivered to their addressee's grid and placed on sell pallets are sold for credits,
/// trade tokens and (if the grid has a station bank ATM) a share paid into the ATM's sector account.
/// </summary>
public sealed class FreightConsoleSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    private readonly HashSet<EntityUid> _setEnts = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FreightConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<FreightConsoleComponent, EntInsertedIntoContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<FreightConsoleComponent, EntRemovedFromContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<FreightConsoleComponent, FreightSpawnMessage>(OnSpawn);
        SubscribeLocalEvent<FreightConsoleComponent, FreightSellMessage>(OnSell);
        SubscribeLocalEvent<FreightConsoleComponent, FreightRefreshMessage>(OnRefresh);
    }

    private void OnRefresh(Entity<FreightConsoleComponent> ent, ref FreightRefreshMessage args) => UpdateUi(ent);

    private void OnUiOpened(Entity<FreightConsoleComponent> ent, ref BoundUIOpenedEvent args) => UpdateUi(ent);

    private void OnSlotChanged(Entity<FreightConsoleComponent> ent, ref EntInsertedIntoContainerMessage args) => UpdateUi(ent);

    private void OnSlotChanged(Entity<FreightConsoleComponent> ent, ref EntRemovedFromContainerMessage args) => UpdateUi(ent);

    #region Destination / pallets

    /// <summary>Destination id of the grid this console stands on (grid, then station, then the console itself).</summary>
    private ProtoId<FreightDestinationPrototype>? GetDestination(Entity<FreightConsoleComponent> console)
    {
        var grid = Transform(console).GridUid;
        if (grid != null)
        {
            if (TryComp<FreightDestinationComponent>(grid, out var gridDest))
                return gridDest.Id;

            var station = _station.GetOwningStation(grid.Value);
            if (station != null && TryComp<FreightDestinationComponent>(station, out var stationDest))
                return stationDest.Id;
        }

        return console.Comp.Destination;
    }

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

    private bool Matches(FreightCrateComponent crate, ProtoId<FreightDestinationPrototype>? here)
    {
        // Wildcard crates are accepted by any freight console.
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

        var pallets = GetPallets(ent, BuySellType.Buy);
        var index = 0;
        var consoleCoords = ent.Owner.ToCoordinates();

        foreach (var entry in manifest.Entries)
        {
            if (!_proto.TryIndex(entry.Pool, out var pool) || pool.Recipients.Count == 0
                || !_proto.HasIndex(entry.Crate))
            {
                Log.Warning($"Freight manifest {ToPrettyString(manifestUid.Value)} has an invalid entry ({entry.Crate}/{entry.Pool})");
                continue;
            }

            for (var i = 0; i < entry.Count; i++)
            {
                var coords = pallets.Count > 0
                    ? Transform(pallets[index++ % pallets.Count]).Coordinates
                    : consoleCoords;

                var crate = Spawn(entry.Crate, coords);
                var recipient = PickRecipient(pool);

                var comp = EnsureComp<FreightCrateComponent>(crate);
                comp.Destination = recipient.Destination;
                comp.Credits = entry.Price ?? recipient.Credits;
                comp.Tokens = recipient.Tokens;
                comp.BankPayout = recipient.BankPayout;

                var destName = recipient.Destination is { } d && _proto.TryIndex(d, out var dp)
                    ? Loc.GetString(dp.Name)
                    : Loc.GetString("freight-any-destination");
                _meta.SetEntityDescription(crate,
                    Loc.GetString("freight-crate-description", ("destination", destName), ("addressee", manifest.Addressee)));
            }
        }

        Del(manifestUid.Value);
        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        _popup.PopupEntity(Loc.GetString("freight-console-spawned"), ent, args.Actor);
        UpdateUi(ent);
    }

    private FreightRecipient PickRecipient(FreightRecipientPoolPrototype pool)
    {
        var total = 0f;
        foreach (var r in pool.Recipients)
            total += Math.Max(r.Weight, 0f);

        if (total <= 0f)
            return _random.Pick(pool.Recipients);

        var roll = _random.NextFloat() * total;
        foreach (var r in pool.Recipients)
        {
            roll -= Math.Max(r.Weight, 0f);
            if (roll <= 0f)
                return r;
        }

        return pool.Recipients[^1];
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
        var hereName = here is { } h && _proto.TryIndex(h, out var hp)
            ? Loc.GetString(hp.Name)
            : Loc.GetString("freight-no-destination");

        var entries = new List<FreightManifestUiEntry>();
        var addressee = string.Empty;
        var hasManifest = false;

        if (ent.Comp.ManifestSlot.Item is { } item && TryComp<FreightManifestComponent>(item, out var manifest))
        {
            hasManifest = true;
            addressee = manifest.Addressee;
            foreach (var entry in manifest.Entries)
            {
                var name = _proto.TryIndex(entry.Crate, out var cp) ? cp.Name : entry.Crate.Id;
                var dests = new List<string>();
                var price = entry.Price ?? 0;
                if (_proto.TryIndex(entry.Pool, out var pool))
                {
                    foreach (var r in pool.Recipients)
                    {
                        var n = r.Destination is { } d && _proto.TryIndex(d, out var dp)
                            ? Loc.GetString(dp.Name)
                            : Loc.GetString("freight-any-destination");
                        if (!dests.Contains(n))
                            dests.Add(n);
                        if (entry.Price == null)
                            price = Math.Max(price, r.Credits);
                    }
                }

                entries.Add(new FreightManifestUiEntry(name, entry.Count, price, string.Join(", ", dests)));
            }
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
            new FreightConsoleBuiState(hereName, hasManifest, addressee, entries, sellable, wrong, value));
    }

    #endregion
}
