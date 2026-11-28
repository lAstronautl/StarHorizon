using Content.Server.Cargo.Systems;
using Content.Server.Radio.EntitySystems;
using System.Linq;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Server.StationEvents.Events;
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Shared._Horizon.Shipbreaking.Derelict;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared.GameTicking;
using Content.Shared.Cargo.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Horizon.Shipbreaking.Derelict;

public sealed class DerelictYardSystem : EntitySystem
{
    private const string DerelictDockTag = "DerelictDock";

    private static readonly TimeSpan FirstWarning = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LastWarning = TimeSpan.FromMinutes(1);

    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly LinkedLifecycleGridSystem _lifecycle = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly ShipyardSystem _shipyard = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ShuttlePreviewSystem _preview = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private sealed class Job
    {
        public EntityUid Station;
        public string Name = string.Empty;
        public int Price;
        public int Appraisal;
        public int Revenue;
        public TimeSpan Expires;
        public bool Warned5;
        public bool Warned1;
    }

    private readonly Dictionary<EntityUid, Job> _jobs = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DerelictYardConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<DerelictYardConsoleComponent, DerelictPurchaseMessage>(OnPurchase);
        SubscribeLocalEvent<DerelictYardConsoleComponent, DerelictPreviewRequestMessage>(OnPreviewRequest);
        SubscribeLocalEvent<DerelictYardConsoleComponent, DerelictFinishMessage>(OnFinishEarly);
        SubscribeLocalEvent<DerelictLeaseComponent, PostGridSplitEvent>(OnSplit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _jobs.Clear());
    }

    private void OnOpened(Entity<DerelictYardConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<DerelictYardConsoleComponent> ent)
    {
        var station = _station.GetOwningStation(ent);
        var rank = station != null ? EnsureComp<DerelictYardRankComponent>(station.Value) : null;
        var state = new DerelictYardState
        {
            Rank = rank?.Rank ?? 0,
            Experience = rank?.Experience ?? 0,
        };

        if (state.Rank + 1 < DerelictYardRankComponent.Thresholds.Length)
            state.NextRankAt = DerelictYardRankComponent.Thresholds[state.Rank + 1];

        foreach (var id in ent.Comp.Vessels)
        {
            if (!_proto.TryIndex(id, out var vessel))
                continue;

            state.Vessels.Add(new DerelictVesselEntry
            {
                Id = vessel.ID,
                Name = Loc.GetString(vessel.Name),
                Description = vessel.Description is { } description ? Loc.GetString(description) : string.Empty,
                Price = vessel.Price,
                Lifetime = vessel.Lifetime,
                MinRank = vessel.MinRank,
                Locked = vessel.MinRank > state.Rank,
            });
        }

        foreach (var (group, job) in _jobs)
        {
            if (job.Station != station)
                continue;

            state.Leases.Add(new DerelictLeaseEntry
            {
                Group = GetNetEntity(group),
                Name = job.Name,
                Left = job.Expires - _timing.CurTime,
                Share = job.Appraisal > 0 ? (float) job.Revenue / job.Appraisal : 0f,
            });
        }

        if (station != null && _bank.TryGetBalance(SectorBankAccount.Frontier, out var balance))
            state.Balance = balance;

        _ui.SetUiState(ent.Owner, DerelictYardUiKey.Key, state);
    }

    private void UpdateAllUis()
    {
        var consoles = EntityQueryEnumerator<DerelictYardConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var console))
        {
            UpdateUi((uid, console));
        }
    }

    private void OnPurchase(Entity<DerelictYardConsoleComponent> ent, ref DerelictPurchaseMessage args)
    {
        if (!ent.Comp.Vessels.Contains(args.Id) || !_proto.TryIndex<DerelictVesselPrototype>(args.Id, out var vessel))
            return;

        if (_station.GetOwningStation(ent) is not { } station)
        {
            _popup.PopupEntity(Loc.GetString("derelict-yard-no-station"), ent, args.Actor);
            return;
        }

        var rank = EnsureComp<DerelictYardRankComponent>(station);
        if (vessel.MinRank > rank.Rank)
        {
            _popup.PopupEntity(Loc.GetString("derelict-yard-rank-too-low"), ent, args.Actor);
            return;
        }

        if (!_bank.TryGetBalance(SectorBankAccount.Frontier, out var balance) || balance < vessel.Price ||
            !_bank.TrySectorWithdraw(SectorBankAccount.Frontier, vessel.Price, LedgerEntryType.StationWithdrawalSupplies))
        {
            _popup.PopupEntity(Loc.GetString("derelict-yard-no-funds"), ent, args.Actor);
            return;
        }

        if (!_shipyard.TryPurchaseShuttle(station, vessel.ShuttlePath, out var shuttle, priorityTag: DerelictDockTag))
        {
            _bank.TrySectorDeposit(SectorBankAccount.Frontier, vessel.Price, LedgerEntryType.StationDepositOther);
            _popup.PopupEntity(Loc.GetString("derelict-yard-failed"), ent, args.Actor);
            return;
        }

        // It is brought to the nearest port and then let go of every port, so it is easier to take apart.
        _docking.UndockDocks(shuttle.Value);

        // The job is kept under an entity of its own, so it can be closed out even when every grid of the wreck is gone.
        var group = Spawn(null, MapCoordinates.Nullspace);
        EnsureComp<DerelictLeaseComponent>(shuttle.Value).Group = group;
        EnsureComp<ShuttleUtilizationComponent>(shuttle.Value);
        MarkOrigin(shuttle.Value, group);

        _jobs[group] = new Job
        {
            Station = station,
            Name = Loc.GetString(vessel.Name),
            Price = vessel.Price,
            Appraisal = (int) _pricing.AppraiseGrid(shuttle.Value),
            Expires = _timing.CurTime + vessel.Lifetime,
        };

        Announce(station,
            Loc.GetString("derelict-yard-bought", ("name", Loc.GetString(vessel.Name)), ("minutes", (int) vessel.Lifetime.TotalMinutes)));

        UpdateAllUis();
    }

    /// <summary>
    /// The news of the yard goes to the supply radio channel, spoken by a yard console of the station.
    /// </summary>
    private void Announce(EntityUid station, string message)
    {
        var consoles = EntityQueryEnumerator<DerelictYardConsoleComponent>();
        while (consoles.MoveNext(out var uid, out _))
        {
            if (_station.GetOwningStation(uid) != station)
                continue;

            _radio.SendRadioMessage(uid, message, CargoOrderConsoleComponent.BaseAnnouncementChannel, uid, escapeMarkup: false);
            return;
        }
    }

    private void MarkOrigin(EntityUid shuttle, EntityUid group)
    {
        if (!TryComp<MapGridComponent>(shuttle, out var grid))
            return;

        var found = new HashSet<EntityUid>();
        _lookup.GetLocalEntitiesIntersecting(shuttle, grid.LocalAABB.Enlarged(1f), found,
            LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Uncontained);

        foreach (var uid in found)
        {
            if (uid != shuttle)
                EnsureComp<DerelictScrapOriginComponent>(uid).Group = group;
        }
    }

    public void CreditScrap(EntityUid source, int amount)
    {
        EntityUid? group = null;
        if (TryComp<DerelictScrapOriginComponent>(source, out var origin))
            group = origin.Group;
        else if (TryComp<DerelictLeaseComponent>(source, out var lease))
            group = lease.Group;

        if (group != null && _jobs.TryGetValue(group.Value, out var job))
            job.Revenue += amount;
    }

    /// <summary>
    /// Whether any lease runs out within the given time.
    /// </summary>
    public bool IsLeaseEnding(TimeSpan within)
    {
        foreach (var job in _jobs.Values)
        {
            if (job.Expires - _timing.CurTime <= within)
                return true;
        }

        return false;
    }

    private void OnFinishEarly(Entity<DerelictYardConsoleComponent> ent, ref DerelictFinishMessage args)
    {
        var group = GetEntity(args.Group);
        if (!_jobs.TryGetValue(group, out var job) || job.Station != _station.GetOwningStation(ent))
            return;

        Finish(group, job);
        _jobs.Remove(group);

        UpdateAllUis();
    }

    private void OnSplit(Entity<DerelictLeaseComponent> ent, ref PostGridSplitEvent args)
    {
        EnsureComp<DerelictLeaseComponent>(args.Grid).Group = ent.Comp.Group;

        EnsureComp<ShuttleUtilizationComponent>(ent);
        EnsureComp<ShuttleUtilizationComponent>(args.Grid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_jobs.Count == 0)
            return;

        List<EntityUid>? finished = null;
        foreach (var (group, job) in _jobs)
        {
            var left = job.Expires - _timing.CurTime;
            if (left <= TimeSpan.Zero)
            {
                (finished ??= new()).Add(group);
            }
            else if (left <= LastWarning && !job.Warned1)
            {
                job.Warned1 = true;
                Warn(job, 1);
            }
            else if (left <= FirstWarning && !job.Warned5)
            {
                job.Warned5 = true;
                Warn(job, 5);
            }
        }

        if (finished == null)
            return;

        foreach (var group in finished)
        {
            Finish(group, _jobs[group]);
            _jobs.Remove(group);
        }

        UpdateAllUis();
    }

    private void Warn(Job job, int minutes)
    {
        Announce(job.Station,
            Loc.GetString("derelict-yard-warning", ("minutes", minutes)));
    }

    private void Finish(EntityUid group, Job job)
    {
        Settle(job);

        var grids = new HashSet<EntityUid>();
        var leases = EntityQueryEnumerator<ShuttleUtilizationComponent, DerelictLeaseComponent>();
        while (leases.MoveNext(out var uid, out _, out var lease))
        {
            if (lease.Group == group)
                grids.Add(uid);
        }

        // The standard way to delete a grid with people on it: they are taken off first, so they are not deleted with it.
        foreach (var grid in grids)
        {
            _lifecycle.UnparentPlayersFromGrid(grid, true);
        }

        Del(group);
    }

    private void Settle(Job job)
    {
        var share = job.Appraisal > 0 ? (float) job.Revenue / job.Appraisal : 0f;
        var percent = (int) MathF.Round(share * 100f);
        var bonus = 0;
        var fee = 0;

        if (share >= 0.9f)
            bonus = (int) (job.Revenue * 0.25f);
        else if (share >= 0.7f)
            bonus = (int) (job.Revenue * 0.1f);
        else if (share < 0.3f)
            fee = (int) (job.Price * 0.1f);

        if (bonus > 0)
            _bank.TrySectorDeposit(SectorBankAccount.Frontier, bonus, LedgerEntryType.StationDepositOther);

        if (fee > 0 && !_bank.TrySectorWithdraw(SectorBankAccount.Frontier, fee, LedgerEntryType.StationWithdrawalSupplies))
            fee = 0;

        Announce(job.Station,
            Loc.GetString("derelict-yard-result",
                ("name", job.Name), ("percent", percent), ("revenue", job.Revenue), ("bonus", bonus), ("fee", fee)));

        var rank = EnsureComp<DerelictYardRankComponent>(job.Station);
        rank.Experience += job.Revenue;

        var newRank = rank.Rank;
        while (newRank + 1 < DerelictYardRankComponent.Thresholds.Length &&
               rank.Experience >= DerelictYardRankComponent.Thresholds[newRank + 1])
        {
            newRank++;
        }

        if (newRank == rank.Rank)
            return;

        rank.Rank = newRank;
        Announce(job.Station,
            Loc.GetString("derelict-yard-rank-up", ("rank", newRank)));
    }

    private void OnPreviewRequest(Entity<DerelictYardConsoleComponent> ent, ref DerelictPreviewRequestMessage args)
    {
        if (!ent.Comp.Vessels.Contains(args.Id) || !_proto.TryIndex<DerelictVesselPrototype>(args.Id, out var vessel))
            return;

        if (!_preview.TryGetPreview($"derelict:{vessel.ID}", vessel.ShuttlePath, vessel.Minimap, out var preview))
            return;

        _ui.ServerSendUiMessage(ent.Owner, DerelictYardUiKey.Key,
            new DerelictPreviewMessage(vessel.ID, preview.Tiles, preview.Colors), args.Actor);
    }
}
